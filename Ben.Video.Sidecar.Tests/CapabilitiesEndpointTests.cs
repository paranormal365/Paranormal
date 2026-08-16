using System.Net;
using System.Net.Http.Json;
using Ben.Video.Core.SidecarContracts;

namespace Ben.Video.Sidecar.Tests;

/// <summary>
/// Item #70 phase 158 — <c>GET /v1/capabilities</c> is how a newer browser decides which offload
/// features it may use against this particular sidecar. The two branches that matter are "ffprobe
/// verified ⇒ probe/thumbnails advertised" and "no ffprobe ⇒ segment only", because the second is
/// what keeps a build without a bundled ffprobe working exactly as it did before this phase.
/// </summary>
public sealed class CapabilitiesEndpointTests : IClassFixture<SidecarWebApplicationFactory>
{
    private readonly SidecarWebApplicationFactory _factory;

    public CapabilitiesEndpointTests(SidecarWebApplicationFactory factory) => _factory = factory;

    [Fact]
    public async Task Capabilities_RequiresToken()
    {
        var client = _factory.CreateAuthenticatedClient(token: null);

        var response = await client.GetAsync("/v1/capabilities");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Capabilities_WithoutFfprobe_OmitsOnlyTheFfprobeDependentOnes()
    {
        // The base fixture deliberately configures no ffprobe override, so it must NOT claim
        // probe/thumbnail support it can't deliver.
        //
        // Item #70 phase 160 widened this from "segment only": concat is a stream copy over
        // segments the sidecar already holds and needs nothing but ffmpeg, so it is correctly
        // advertised even without ffprobe. The invariant being tested is "don't advertise what
        // this build can't do", not a frozen list.
        var client = _factory.CreateAuthenticatedClient(token: _factory.ReadGeneratedPairingToken());

        var info = await client.GetFromJsonAsync<CapabilitiesInfo>(
            "/v1/capabilities", SidecarJsonOptions.Default);

        Assert.NotNull(info);
        Assert.Contains(SidecarCapabilities.Segment, info!.Capabilities);
        Assert.Contains(SidecarCapabilities.Concat, info.Capabilities);
        Assert.DoesNotContain(SidecarCapabilities.Probe, info.Capabilities);
        Assert.DoesNotContain(SidecarCapabilities.Thumbnails, info.Capabilities);
    }

    [Fact]
    public async Task Capabilities_ReportsProtocolVersionAndStableInstanceId()
    {
        var client = _factory.CreateAuthenticatedClient(token: _factory.ReadGeneratedPairingToken());

        var first  = await client.GetFromJsonAsync<CapabilitiesInfo>("/v1/capabilities", SidecarJsonOptions.Default);
        var second = await client.GetFromJsonAsync<CapabilitiesInfo>("/v1/capabilities", SidecarJsonOptions.Default);

        Assert.Equal(3, first!.ProtocolVersion);
        Assert.NotEqual(Guid.Empty, first.InstanceId);
        // Same process ⇒ same id. Phase 160 relies on a CHANGED id meaning "restarted, drop every
        // retained-segment id", so a spuriously-changing id would silently defeat that cache.
        Assert.Equal(first.InstanceId, second!.InstanceId);
    }

    [Fact]
    public async Task Health_StillReportsSameProtocolVersion()
    {
        // /v1/health deliberately gained NO new fields this phase (adding one would break older
        // browsers' strict parse) — but its version number must agree with /v1/capabilities.
        var client = _factory.CreateAuthenticatedClient(token: null);

        var health = await client.GetFromJsonAsync<HealthInfo>("/v1/health", SidecarJsonOptions.Default);

        Assert.Equal(3, health!.ProtocolVersion);
    }
}

/// <summary>Same endpoint, but against a fixture that DOES supply an ffprobe binary.</summary>
public sealed class CapabilitiesWithFfprobeTests : IClassFixture<CapabilitiesWithFfprobeTests.WithFfprobeFactory>
{
    public sealed class WithFfprobeFactory : SidecarWebApplicationFactory
    {
        public WithFfprobeFactory() => WithFfprobe = true;
    }

    private readonly WithFfprobeFactory _factory;

    public CapabilitiesWithFfprobeTests(WithFfprobeFactory factory) => _factory = factory;

    [Fact]
    public async Task Capabilities_WithFfprobe_AdvertisesProbeAndThumbnails()
    {
        var client = _factory.CreateAuthenticatedClient(token: _factory.ReadGeneratedPairingToken());

        var info = await client.GetFromJsonAsync<CapabilitiesInfo>(
            "/v1/capabilities", SidecarJsonOptions.Default);

        Assert.Contains(SidecarCapabilities.Segment, info!.Capabilities);
        Assert.Contains(SidecarCapabilities.Probe, info.Capabilities);
        Assert.Contains(SidecarCapabilities.Thumbnails, info.Capabilities);
    }
}
