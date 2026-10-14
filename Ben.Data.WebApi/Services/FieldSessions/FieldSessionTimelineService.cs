using System.Security.Cryptography;
using System.Text.Json;
using Ben.Data.Common.Interfaces;
using Ben.Service.Models.FieldSessions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Memory;

namespace Ben.Data.WebApi.Services.FieldSessions;

/// <summary>One file of a session as the timeline needs it: where its bytes are, and when it changed.</summary>
public sealed record TimelineSourceFile(
    Guid FileId, string RelativePath, string? StoragePath, string? BundleEntryPath, DateTime Stamp);

/// <summary>Everything that decides what a timeline says — and so everything its cache key is made of.</summary>
/// <param name="Redact">
/// What this audience may see of the document, applied before anything is read out of it: the
/// share link's sweep, the archive's public point. Null for the owner and the group.
/// </param>
/// <param name="Variant">
/// Names <paramref name="Redact"/> for the cache key — "share:withheld", "public:{published}:{review}" —
/// so two audiences never share an entry.
/// </param>
public sealed record TimelineRequest(
    Guid SessionId,
    bool IsBundle,
    string DocumentStoragePath,
    Guid DocumentUploadFileId,
    DateTime DocumentStamp,
    IReadOnlyList<TimelineSourceFile> Files,
    TimelinePositionPolicy Policy,
    Func<string, string?>? Redact,
    string Variant);

/// <summary>A built timeline, as the bytes that go on the wire, and a tag for them.</summary>
public sealed record TimelinePayload(byte[] Json, string ETag);

/// <summary>
/// Builds a session's <see cref="SessionTimeline"/> once and serves it to everybody watching.
/// </summary>
/// <remarks>
/// <para><b>Why a cache, and why its own.</b> Every viewer of a session asks for the same timeline,
/// and building one means reading the whole document and opening every recording's header. A group
/// watching a night together used to cost that per person, per page load. Now it is once per
/// session per audience, until something about the session changes.</para>
///
/// <para>Its own <see cref="MemoryCache"/> with a size limit, not the shared one: setting a limit on
/// the shared cache would make every other caller that does not give a size throw.</para>
///
/// <para><b>The key is everything the answer depends on</b> — the document and its stamp, every
/// file and its stamp (files attach after the document), the position policy and the audience's
/// variant (a published copy's publication and review state). A change to any of them is a new key,
/// so a stale timeline is never served; the old entry simply ages out.</para>
///
/// <para><b>Access is the caller's, and comes FIRST.</b> This never decides who may see a session;
/// every endpoint checks that before asking here, so a cached answer cannot be reached by somebody
/// the uncached one would have refused.</para>
/// </remarks>
public sealed class FieldSessionTimelineService
{
    /// <summary>The cache's budget, in kilobytes of JSON.</summary>
    public const long CacheKilobytes = 64 * 1024;

    private static readonly TimeSpan Sliding = TimeSpan.FromMinutes(20);
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private readonly IFileStorageService _storage;
    private readonly IBenBundleStore _bundles;
    private readonly ILogger<FieldSessionTimelineService> _log;
    private readonly MemoryCache _cache = new(new MemoryCacheOptions { SizeLimit = CacheKilobytes });

    /// <summary>How many timelines were built rather than served from the cache — for tests.</summary>
    public int Builds => _builds;
    private int _builds;

    public FieldSessionTimelineService(
        IFileStorageService storage, IBenBundleStore bundles, ILogger<FieldSessionTimelineService> log)
    {
        _storage = storage;
        _bundles = bundles;
        _log = log;
    }

    /// <summary>The timeline for this request, or null when the session's document is gone.</summary>
    public async Task<TimelinePayload?> GetAsync(TimelineRequest request, CancellationToken ct)
    {
        var key = Key(request);
        if (_cache.TryGetValue(key, out TimelinePayload? cached) && cached is not null) return cached;

        var built = await BuildAsync(request, ct);
        if (built is null) return null;

        _cache.Set(key, built, new MemoryCacheEntryOptions
        {
            Size = built.Json.Length / 1024 + 1,
            SlidingExpiration = Sliding,
        });
        return built;
    }

    /// <summary>
    /// The response every timeline endpoint gives: the JSON, its tag, and a 304 for a caller who
    /// already holds this exact version.
    /// </summary>
    /// <remarks>
    /// <c>private, no-cache</c>: the browser may keep it but must ask first, because a file
    /// attached a minute later is a different timeline. Asking costs a 304 and no body.
    /// </remarks>
    public static IActionResult Respond(ControllerBase controller, TimelinePayload payload)
    {
        var response = controller.Response;
        response.Headers.ETag = payload.ETag;
        response.Headers.CacheControl = "private, no-cache";
        if (controller.Request.Headers.IfNoneMatch.ToString() == payload.ETag)
            return controller.StatusCode(StatusCodes.Status304NotModified);
        return controller.File(payload.Json, "application/json");
    }

    /// <summary>A session's files as the timeline reads them, stamped with when each last changed.</summary>
    public static IReadOnlyList<TimelineSourceFile> Sources(IEnumerable<Ben.Data.Source.Entities.FieldSessionUploadFile> files)
        => files
            .Where(f => f.UploadFileId is not null || f.BundleEntryPath is { Length: > 0 })
            .Select(f => new TimelineSourceFile(
                f.Id, f.RelativePath, f.UploadFile?.StoragePath, f.BundleEntryPath, f.DateUpdated ?? f.DateCreated))
            .ToList();

    internal static string Key(TimelineRequest r)
    {
        var files = string.Join(",", r.Files
            .OrderBy(f => f.FileId)
            .Select(f => $"{f.FileId:N}:{f.Stamp.Ticks}"));
        return $"{r.SessionId:N}|{r.DocumentUploadFileId:N}:{r.DocumentStamp.Ticks}|{files}|{r.Policy}|{r.Variant}";
    }

    private async Task<TimelinePayload?> BuildAsync(TimelineRequest request, CancellationToken ct)
    {
        if (!_storage.Exists(request.DocumentStoragePath)) return null;

        string document;
        await using (var stream = request.IsBundle
                         ? await _bundles.OpenEntryAsync(request.DocumentStoragePath, BenBundle.DocumentEntryPath, ct)
                         : await _storage.OpenReadAsync(request.DocumentStoragePath, ct))
        {
            if (stream is null) return null;
            using var reader = new StreamReader(stream);
            document = await reader.ReadToEndAsync(ct);
        }

        if (request.Redact is { } redact)
        {
            if (redact(document) is not { } redacted) return null;
            document = redacted;
        }

        var files = new List<TimelineFile>(request.Files.Count);
        foreach (var file in request.Files)
            files.Add(new TimelineFile(file.FileId, file.RelativePath, await DurationAsync(request, file, ct)));

        SessionTimeline timeline;
        try
        {
            timeline = FieldSessionTimelineBuilder.Build(document, files, request.Policy);
        }
        catch (JsonException ex)
        {
            _log.LogWarning(ex, "Session {SessionId}'s document is not JSON", request.SessionId);
            return null;
        }

        Interlocked.Increment(ref _builds);
        var json = JsonSerializer.SerializeToUtf8Bytes(timeline, Json);
        var tag = "\"" + Convert.ToHexString(SHA256.HashData(json))[..32].ToLowerInvariant() + "\"";
        return new TimelinePayload(json, tag);
    }

    /// <summary>A recording's length from its own header; null for anything else or anything unreadable.</summary>
    private async Task<double?> DurationAsync(TimelineRequest request, TimelineSourceFile file, CancellationToken ct)
    {
        if (!FieldSessionTimelineBuilder.IsAudio(file.RelativePath) && !FieldSessionTimelineBuilder.IsVideo(file.RelativePath))
            return null;
        try
        {
            Stream? stream = file.BundleEntryPath is { Length: > 0 } entry
                ? await _bundles.OpenEntryAsync(request.DocumentStoragePath, entry, ct)
                : file.StoragePath is { Length: > 0 } path && _storage.Exists(path)
                    ? await _storage.OpenReadAsync(path, ct)
                    : null;
            if (stream is null) return null;
            await using (stream) return MediaDuration.Seconds(stream);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            // The browser reads the file too; a length the server could not read is a slower
            // timeline, not a broken one.
            _log.LogInformation(ex, "Could not read the length of {Path} in session {SessionId}", file.RelativePath, request.SessionId);
            return null;
        }
    }
}
