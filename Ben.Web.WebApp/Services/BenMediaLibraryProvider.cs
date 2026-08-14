using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Ben.Video.Editor.Models;
using Ben.Video.Editor.Services;
using Ben.Web.WebApp.Services.WebApi;
using Microsoft.Extensions.Options;

namespace Ben.Web.WebApp.Services;

/// <summary>
/// Ben-specific IMediaLibraryProvider that forwards the circuit's bearer token
/// to the WebApi so the response is scoped to the authenticated user's files.
/// Registered after AddBenVideoEditor() in Program.cs to override the default
/// HttpMediaLibraryProvider (ASP.NET Core DI resolves the last registration).
/// </summary>
public sealed class BenMediaLibraryProvider : IMediaLibraryProvider
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IWebApiTokenStore _tokenStore;
    private readonly WebApiOptions _apiOptions;

    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public BenMediaLibraryProvider(
        IHttpClientFactory httpClientFactory,
        IWebApiTokenStore tokenStore,
        IOptions<WebApiOptions> apiOptions)
    {
        _httpClientFactory = httpClientFactory;
        _tokenStore        = tokenStore;
        _apiOptions        = apiOptions.Value;
    }

    public async Task<IReadOnlyList<MediaLibraryFile>> GetFilesAsync(
        CancellationToken cancellationToken = default)
    {
        // Explicitly scoped to video/audio/image — the aggregation endpoint now also returns
        // documents and other file types for the general-purpose media library, so this keeps
        // Ben.Video's picker payload the same size it always was (the .Where below is a
        // belt-and-suspenders client-side filter, not the primary mechanism).
        var url      = $"{_apiOptions.BaseUrl.TrimEnd('/')}/api/media-library/files?contentTypePrefixes=video/,audio/,image/";
        var response = await SendAsync(HttpMethod.Get, url, cancellationToken);

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
}
