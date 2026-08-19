using Ben.Video.Editor.Services;
using Ben.Web.Services.WebApi;

namespace Ben.Web.Services;

/// <summary>
/// Reports a successful sidecar pairing from the Blazor Server host.
/// </summary>
/// <remarks>
/// The WASM host's counterpart sends the browser's own bearer token; here the circuit already holds
/// one, so <see cref="IWebApiClient"/> does the work and the token never reaches the browser.
/// <c>PostVoidAsync</c> rather than <c>PostAsync</c> because the endpoint answers 204 and reading a
/// body from an empty response would fail.
/// </remarks>
public sealed class SidecarPairingReporter : ISidecarPairingReporter
{
    private readonly IWebApiClient _api;

    public SidecarPairingReporter(IWebApiClient api) => _api = api;

    public async Task ReportPairedAsync(
        Guid installId, string? version, string? platform, CancellationToken ct = default)
    {
        try
        {
            await _api.PostVoidAsync("/api/sidecar-telemetry/pairings",
                new { installId, version, platform }, ct);
        }
        catch
        {
            // Never throws by contract — see ISidecarPairingReporter. The pairing already worked,
            // and a signed-out or unreachable API is an ordinary state, not a failure to report.
        }
    }
}
