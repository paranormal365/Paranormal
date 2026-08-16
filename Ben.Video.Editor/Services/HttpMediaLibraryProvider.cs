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
///   GET  {base}/api/upload-files               → IEnumerable of UploadFileRecord-compatible JSON
///   GET  {base}/api/upload-files/{id}/download → raw file bytes
///
/// Auth is intentionally NOT handled here. The provider resolves the named
/// <see cref="System.Net.Http.HttpClient"/> (<c>"BenVideo.MediaLibrary"</c>) which the
/// host application configures with its own auth delegating handler via the
/// <c>configureHttpClient</c> parameter of <c>AddBenVideoEditor()</c>. The WebAPI
/// then determines which files to return based on the supplied credentials.
/// </summary>
public sealed class HttpMediaLibraryProvider : IMediaLibraryProvider
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
        CancellationToken cancellationToken = default)
    {
        var baseUrl = _options.MediaLibraryBaseUrl?.TrimEnd('/') ?? string.Empty;
        var url     = $"{baseUrl}/api/upload-files";

        var records = await _http.GetFromJsonAsync<List<UploadFileDto>>(
            url, _jsonOptions, cancellationToken) ?? [];

        return records
            .Where(r => r.ContentType != null &&
                        (r.ContentType.StartsWith("video/", StringComparison.OrdinalIgnoreCase) ||
                         r.ContentType.StartsWith("audio/", StringComparison.OrdinalIgnoreCase)))
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
}
