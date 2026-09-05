using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Ben.Video.Editor.Extensions;
using Ben.Video.Editor.Models;
using Microsoft.Extensions.Options;

namespace Ben.Video.Editor.Services;

/// <summary>
/// Default <see cref="IMediaLibraryProvider"/> that communicates with the
/// AverageBen WebAPI at <c>VideoEditorOptions.MediaLibraryBaseUrl</c>.
///
/// API surface used:
///   GET  {base}/api/media-library/files        → IEnumerable of UploadFileRecord-compatible JSON
///   GET  {base}/api/media-library/scopes       → the cases and visits the library can be scoped by
///   GET  {base}/api/upload-files/{id}/download → raw file bytes
///
/// The listing endpoint changed with backlog item 91, and the change is worth knowing about:
/// <c>/api/upload-files</c> returns only files the caller <i>owns</i>, while
/// <c>/api/media-library/files</c> returns the full set they may see — owned, shared with them,
/// shared with their group, and attached to a case they can reach. This host's Server tab was
/// therefore silently narrower than the same tab on the Blazor Server site, which has always used
/// the aggregating endpoint. They now agree, and anyone who wants the old behaviour picks the
/// Personal scope.
///
/// Auth is intentionally NOT handled here. The provider resolves the named
/// <see cref="System.Net.Http.HttpClient"/> (<c>"BenVideo.MediaLibrary"</c>) which the
/// host application configures with its own auth delegating handler via the
/// <c>configureHttpClient</c> parameter of <c>AddBenVideoEditor()</c>. The WebAPI
/// then determines which files to return based on the supplied credentials.
/// </summary>
public sealed class HttpMediaLibraryProvider : IMediaLibraryProvider, IMediaLibraryScopeSource
{
    private readonly HttpClient              _http;
    private readonly VideoEditorOptions      _options;

    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition      = JsonIgnoreCondition.WhenWritingNull,
    };

    public HttpMediaLibraryProvider(
        IHttpClientFactory        httpClientFactory,
        IOptions<VideoEditorOptions> options)
    {
        _http    = httpClientFactory.CreateClient(ServiceCollectionExtensions.MediaLibraryHttpClientName);
        _options = options.Value;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<MediaLibraryFile>> GetFilesAsync(
        MediaLibraryScope? scope = null, CancellationToken cancellationToken = default)
    {
        var baseUrl = _options.MediaLibraryBaseUrl?.TrimEnd('/') ?? string.Empty;
        // Narrowed server-side as well as below: the aggregating endpoint also serves documents
        // and everything else the library holds, and there is no reason to ship a case's PDFs to
        // the browser so that the filter on the next line can drop them.
        var url     = $"{baseUrl}/api/media-library/files?contentTypePrefixes=video/,audio/,image/"
                    + ScopeQuery(scope);

        // Read the response rather than letting GetFromJsonAsync throw: a 401 here is somebody
        // needing to sign in, and its raw exception message was what the panel used to show
        // (2026-09-05 audit, F11).
        var response = await _http.GetAsync(url, cancellationToken);

        if (response.StatusCode is System.Net.HttpStatusCode.Unauthorized
                                or System.Net.HttpStatusCode.Forbidden)
            throw new MediaLibraryUnauthorizedException();

        response.EnsureSuccessStatusCode();

        var records = await response.Content.ReadFromJsonAsync<List<UploadFileDto>>(
            _jsonOptions, cancellationToken) ?? [];

        return records
            // Images included, matching the site host. The editor places stills as overlays, and
            // this host used to be the only one where they were missing from the tab.
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

    /// <inheritdoc />
    public async Task<byte[]> DownloadFileAsync(
        Guid fileId, CancellationToken cancellationToken = default, IProgress<double>? progress = null)
    {
        var baseUrl = _options.MediaLibraryBaseUrl?.TrimEnd('/') ?? string.Empty;
        var url     = $"{baseUrl}/api/upload-files/{fileId}/download";

        // Phase 150 — stream with progress reporting (best-effort: only when the response reports
        // Content-Length) instead of ReadAsByteArrayAsync, matching DemoMediaLibraryProvider so
        // real hosts get the same per-file progress UI on the Server tab.
        using var response = await _http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();
        var totalBytes = response.Content.Headers.ContentLength;

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var buffer = new MemoryStream(totalBytes is > 0 ? (int)totalBytes.Value : 81920);
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

    /// <inheritdoc />
    public async Task<IReadOnlyList<MediaLibraryScopeGroup>> GetScopeGroupsAsync(
        CancellationToken cancellationToken = default)
    {
        var baseUrl = _options.MediaLibraryBaseUrl?.TrimEnd('/') ?? string.Empty;

        try
        {
            var groups = await _http.GetFromJsonAsync<List<ScopeGroupDto>>(
                $"{baseUrl}/api/media-library/scopes", _jsonOptions, cancellationToken) ?? [];

            return groups
                .Select(g => new MediaLibraryScopeGroup(
                    g.Id,
                    g.Title ?? "Untitled",
                    (g.Investigations ?? []).Select(i => new MediaLibraryScopeItem(i.Id, i.Label ?? "Untitled")).ToList()))
                .ToList()
                .AsReadOnly();
        }
        catch (HttpRequestException)
        {
            // Signed out, or an API that predates this endpoint. Offering no groups leaves All and
            // Personal working, which is a smaller loss than a tab that refuses to load.
            return [];
        }
    }

    /// <summary>
    /// The scope as a query string, or empty for no scope.
    /// </summary>
    /// <remarks>
    /// Ids go through <c>Uri.EscapeDataString</c> even though a Guid cannot contain a character
    /// that needs escaping — the habit is what matters, since the day one of these stops being a
    /// Guid is not the day anybody remembers to add it.
    /// </remarks>
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

    // ── Private DTO (matches UploadFileRecord JSON shape) ─────────────────────

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
