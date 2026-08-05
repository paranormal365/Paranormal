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
        var url      = $"{_apiOptions.BaseUrl.TrimEnd('/')}/api/media-library/files";
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
        Guid fileId, CancellationToken cancellationToken = default)
    {
        var url      = $"{_apiOptions.BaseUrl.TrimEnd('/')}/api/upload-files/{fileId}/download";
        var response = await SendAsync(HttpMethod.Get, url, cancellationToken);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsByteArrayAsync(cancellationToken);
    }

    private async Task<HttpResponseMessage> SendAsync(
        HttpMethod method, string url, CancellationToken ct)
    {
        var client  = _httpClientFactory.CreateClient();
        var request = new HttpRequestMessage(method, url);

        var token = _tokenStore.AccessToken;
        if (!string.IsNullOrEmpty(token))
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        return await client.SendAsync(request, ct);
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
