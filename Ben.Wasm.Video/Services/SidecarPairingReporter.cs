using Ben.Video.Editor.Services;
using System.Net.Http.Json;

namespace Ben.Wasm.Video.Services;

/// <summary>
/// Reports a successful sidecar pairing to the WebApi, attributed to the signed-in user.
/// </summary>
/// <remarks>
/// Uses a client carrying <see cref="BearerTokenHandler"/>, because attribution is the entire point
/// — an anonymous report would record that <i>a</i> sidecar exists, which the installer already
/// says. Silent when signed out or when no API is configured: those are ordinary states for this
/// host, not failures worth surfacing.
/// </remarks>
public sealed class SidecarPairingReporter : ISidecarPairingReporter
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly TokenStore _tokens;
    private readonly string? _apiBaseUrl;

    public SidecarPairingReporter(
        IHttpClientFactory httpClientFactory, TokenStore tokens, string? apiBaseUrl)
    {
        _httpClientFactory = httpClientFactory;
        _tokens = tokens;
        _apiBaseUrl = apiBaseUrl?.TrimEnd('/');
    }

    public async Task ReportPairedAsync(
        Guid installId, string? version, string? platform, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(_apiBaseUrl)) return;
        if (await _tokens.GetAccessTokenAsync() is null) return;

        try
        {
            var http = _httpClientFactory.CreateClient(
                Ben.Video.Editor.Extensions.ServiceCollectionExtensions.MediaLibraryHttpClientName);

            await http.PostAsJsonAsync(
                $"{_apiBaseUrl}/api/sidecar-telemetry/pairings",
                new { installId, version, platform },
                ct);
        }
        catch
        {
            // Never throws by contract — see ISidecarPairingReporter. The pairing already worked.
        }
    }
}
