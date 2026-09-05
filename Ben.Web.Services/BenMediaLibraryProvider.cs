using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Ben.Video.Editor.Models;
using Ben.Video.Editor.Services;
using Ben.Web.Services.WebApi;
using Microsoft.Extensions.Options;

namespace Ben.Web.Services;

/// <summary>
/// Ben-specific IMediaLibraryProvider that forwards the circuit's bearer token
/// to the WebApi so the response is scoped to the authenticated user's files.
/// Registered after AddBenVideoEditor() in Program.cs to override the default
/// HttpMediaLibraryProvider (ASP.NET Core DI resolves the last registration).
/// </summary>
public sealed class BenMediaLibraryProvider : IMediaLibraryProvider, IMediaLibraryScopeSource
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IWebApiTokenStore _tokenStore;

    /// <summary>Optional: a host that can let the browser fetch a file for itself.</summary>
    private readonly IMediaTicketMinter? _ticketMinter;
    private readonly WebApiOptions _apiOptions;

    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public BenMediaLibraryProvider(
        IHttpClientFactory httpClientFactory,
        IWebApiTokenStore tokenStore,
        IOptions<WebApiOptions> apiOptions,
        IMediaTicketMinter? ticketMinter = null)
    {
        _httpClientFactory = httpClientFactory;
        _tokenStore        = tokenStore;
        _apiOptions        = apiOptions.Value;
        _ticketMinter      = ticketMinter;
    }

    public async Task<IReadOnlyList<MediaLibraryFile>> GetFilesAsync(
        MediaLibraryScope? scope = null, CancellationToken cancellationToken = default)
    {
        // Explicitly scoped to video/audio/image — the aggregation endpoint now also returns
        // documents and other file types for the general-purpose media library, so this keeps
        // Ben.Video's picker payload the same size it always was (the .Where below is a
        // belt-and-suspenders client-side filter, not the primary mechanism).
        var url      = $"{_apiOptions.BaseUrl.TrimEnd('/')}/api/media-library/files?contentTypePrefixes=video/,audio/,image/"
                     + ScopeQuery(scope);
        var response = await SendAsync(HttpMethod.Get, url, cancellationToken);

        // A refusal is not an empty library. Returning [] for any failure showed the Server tab as
        // "no files" to somebody whose session had simply expired — which reads as "you have not
        // uploaded anything" (2026-09-05 audit, site-11).
        if (response.StatusCode is System.Net.HttpStatusCode.Unauthorized
                                or System.Net.HttpStatusCode.Forbidden)
            throw new Ben.Video.Editor.Services.MediaLibraryUnauthorizedException();

        if (!response.IsSuccessStatusCode)
            return [];

        var records = await response.Content.ReadFromJsonAsync<List<UploadFileDto>>(
            _jsonOptions, cancellationToken) ?? [];

        return records
            .Where(r => r.ContentType != null &&
                       (r.ContentType.StartsWith("video/", StringComparison.OrdinalIgnoreCase) ||
                        r.ContentType.StartsWith("audio/", StringComparison.OrdinalIgnoreCase) ||
                        r.ContentType.StartsWith("image/", StringComparison.OrdinalIgnoreCase)))
            .Select(r => new MediaLibraryFile
            {
                Id          = r.Id,
                FileName    = r.FileName    ?? string.Empty,
                ContentType = r.ContentType ?? string.Empty,
                FileSize    = r.FileSize,
                Description = r.Description,
                DateCreated = r.DateCreated,
            })
            .ToList()
            .AsReadOnly();
    }

    /// <summary>
    /// A URL the browser can fetch this file from, when the host can mint one.
    /// </summary>
    /// <remarks>
    /// The point of this is what it avoids. <see cref="DownloadFileAsync"/> runs on the server
    /// under Blazor Server: the file is pulled into server memory, copied again into a byte array,
    /// and shipped to the browser over the circuit — three copies of a file the browser could have
    /// fetched itself, with a 2 GB ceiling on the way (2026-09-05 audit, site-2 and media-6).
    /// </remarks>
    public Task<string?> GetDownloadUrlAsync(Guid fileId, CancellationToken cancellationToken = default)
        => Task.FromResult(_ticketMinter?.Mint(fileId, "download"));

    public async Task<byte[]> DownloadFileAsync(
        Guid fileId, CancellationToken cancellationToken = default, IProgress<double>? progress = null)
    {
        var url = $"{_apiOptions.BaseUrl.TrimEnd('/')}/api/upload-files/{fileId}/download";

        // Streamed rather than ReadAsByteArrayAsync so the Server tab's per-file progress bar
        // gets real numbers. Best-effort by contract: when the WebApi doesn't send a
        // Content-Length (chunked responses) there is nothing to compute a fraction against,
        // so no intermediate report is made and only the terminal 1.0 fires.
        using var response = await SendAsync(
            HttpMethod.Get, url, cancellationToken, HttpCompletionOption.ResponseHeadersRead);
        response.EnsureSuccessStatusCode();
        var totalBytes = response.Content.Headers.ContentLength;

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);

        // The capacity used to be (int)totalBytes, which is negative for anything over 2 GB and
        // throws before a byte is read. Uploads have had no size cap since the limits were removed,
        // so a large source really can be that big (2026-09-05 audit, media-6). Clamped rather than
        // widened, because MemoryStream cannot hold more than int.MaxValue anyway — the real fix
        // for a file that size is GetDownloadUrlAsync, which never brings it here at all.
        var capacity = totalBytes is > 0 and <= int.MaxValue ? (int)totalBytes.Value : 81920;
        using var buffer = new MemoryStream(capacity);
        var chunk = new byte[81920];
        long readSoFar = 0;
        int bytesRead;
        while ((bytesRead = await stream.ReadAsync(chunk, cancellationToken)) > 0)
        {
            buffer.Write(chunk, 0, bytesRead);
            readSoFar += bytesRead;
            if (totalBytes is > 0)
                progress?.Report(Math.Min(1.0, (double)readSoFar / totalBytes.Value));
        }
        progress?.Report(1.0);
        return buffer.ToArray();
    }

    private async Task<HttpResponseMessage> SendAsync(
        HttpMethod method, string url, CancellationToken ct,
        HttpCompletionOption completionOption = HttpCompletionOption.ResponseContentRead)
    {
        var client  = _httpClientFactory.CreateClient();
        var request = new HttpRequestMessage(method, url);

        var token = _tokenStore.AccessToken;
        if (!string.IsNullOrEmpty(token))
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        return await client.SendAsync(request, completionOption, ct);
    }

    /// <summary>
    /// The cases and visits this person's media can be scoped to.
    /// </summary>
    /// <remarks>
    /// The editor gets labels and ids and nothing else — it renders them and hands an id back. That
    /// is deliberate: a general-purpose editor component should not learn what a case is.
    /// </remarks>
    public async Task<IReadOnlyList<MediaLibraryScopeGroup>> GetScopeGroupsAsync(
        CancellationToken cancellationToken = default)
    {
        var url      = $"{_apiOptions.BaseUrl.TrimEnd('/')}/api/media-library/scopes";
        var response = await SendAsync(HttpMethod.Get, url, cancellationToken);

        // No groups leaves All and Personal working, which is a far smaller loss than a media tab
        // that will not load.
        if (!response.IsSuccessStatusCode) return [];

        var groups = await response.Content.ReadFromJsonAsync<List<ScopeGroupDto>>(
            _jsonOptions, cancellationToken) ?? [];

        return groups
            .Select(g => new MediaLibraryScopeGroup(
                g.Id,
                g.Title ?? "Untitled",
                (g.Investigations ?? []).Select(i => new MediaLibraryScopeItem(i.Id, i.Label ?? "Untitled")).ToList()))
            .ToList()
            .AsReadOnly();
    }

    /// <summary>The scope as a query string, or empty for no scope.</summary>
    private static string ScopeQuery(MediaLibraryScope? scope)
    {
        if (scope is null || scope.Kind == MediaLibraryScopeKind.All) return string.Empty;

        var query = $"&scope={scope.Wire}";
        if (scope.CaseId is { } caseId)
            query += $"&caseId={Uri.EscapeDataString(caseId.ToString())}";
        if (scope.InvestigationId is { } investigationId)
            query += $"&investigationId={Uri.EscapeDataString(investigationId.ToString())}";

        return query;
    }

    // ── Private DTO matching the UploadFileRecord fields we need ─────────────

    private sealed class UploadFileDto
    {
        public Guid     Id          { get; set; }
        public string?  FileName    { get; set; }
        public string?  ContentType { get; set; }
        public long     FileSize    { get; set; }
        public string?  Description { get; set; }
        public DateTime DateCreated { get; set; }
    }

    private sealed class ScopeGroupDto
    {
        public Guid Id { get; set; }
        public string? Title { get; set; }
        public List<ScopeItemDto>? Investigations { get; set; }
    }

    private sealed class ScopeItemDto
    {
        public Guid Id { get; set; }
        public string? Label { get; set; }
    }
}
