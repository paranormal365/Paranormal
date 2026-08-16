using Ben.Video.Core.SidecarContracts;
using Ben.Video.Editor.Services;

namespace Ben.Video.Tests.Services;

/// <summary>
/// Item #70 phase 158 — the browser's view of what a connected sidecar can do. The whole point of
/// this type is graceful degradation across version skew, so these tests focus on the degraded
/// paths: a pre-158 sidecar (no capabilities endpoint) must still be credited with the segment
/// rendering it has always been able to do, and an unreachable/garbled one must never be credited
/// with a capability it didn't advertise.
/// </summary>
public sealed class SidecarCapabilitySetTests
{
    [Fact]
    public void Legacy_HasSegmentOnly()
    {
        // A v2 sidecar answers 404 for /v1/capabilities. That is NOT a failure — segment rendering
        // has worked since phase 123 and must keep working unchanged.
        Assert.True(SidecarCapabilitySet.Legacy.Has(SidecarCapabilities.Segment));
        Assert.False(SidecarCapabilitySet.Legacy.Has(SidecarCapabilities.Probe));
        Assert.False(SidecarCapabilitySet.Legacy.Has(SidecarCapabilities.Thumbnails));
        Assert.False(SidecarCapabilitySet.Legacy.Has(SidecarCapabilities.Concat));
        Assert.Null(SidecarCapabilitySet.Legacy.InstanceId);
    }

    [Fact]
    public void None_HasNothing()
    {
        Assert.False(SidecarCapabilitySet.None.Has(SidecarCapabilities.Segment));
        Assert.Empty(SidecarCapabilitySet.None.Capabilities);
        Assert.Null(SidecarCapabilitySet.None.InstanceId);
    }

    [Fact]
    public void FromResponse_NullInfo_DegradesToLegacyNotNone()
    {
        // Reaching this point means health + token check already succeeded, so segment rendering is
        // known-good even if the capabilities body was empty/unparseable. Degrading to None here
        // would needlessly disable rendering that demonstrably works.
        var set = SidecarCapabilitySet.FromResponse(null);

        Assert.True(set.Has(SidecarCapabilities.Segment));
        Assert.False(set.Has(SidecarCapabilities.Probe));
    }

    [Fact]
    public void FromResponse_CarriesCapabilitiesInstanceIdAndVersion()
    {
        var id = Guid.NewGuid();
        var set = SidecarCapabilitySet.FromResponse(new CapabilitiesInfo(
            ProtocolVersion: 3,
            InstanceId: id,
            Capabilities: [SidecarCapabilities.Segment, SidecarCapabilities.Probe, SidecarCapabilities.Thumbnails]));

        Assert.Equal(3, set.ProtocolVersion);
        Assert.Equal(id, set.InstanceId);
        Assert.True(set.Has(SidecarCapabilities.Segment));
        Assert.True(set.Has(SidecarCapabilities.Probe));
        Assert.True(set.Has(SidecarCapabilities.Thumbnails));
        Assert.False(set.Has(SidecarCapabilities.Concat));
    }

    [Fact]
    public void FromResponse_UnknownCapabilityStrings_AreCarriedButHarmless()
    {
        // A sidecar NEWER than this browser build may advertise capabilities this build has never
        // heard of. They must not throw or corrupt the set — they're simply never asked about.
        var set = SidecarCapabilitySet.FromResponse(new CapabilitiesInfo(
            ProtocolVersion: 99,
            InstanceId: Guid.NewGuid(),
            Capabilities: [SidecarCapabilities.Segment, "some-future-capability"]));

        Assert.True(set.Has(SidecarCapabilities.Segment));
        Assert.True(set.Has("some-future-capability"));
        Assert.False(set.Has(SidecarCapabilities.Probe));
    }

    [Fact]
    public void FromResponse_EmptyCapabilityList_GrantsNothing()
    {
        // Distinct from the null-body case above: the sidecar explicitly answered "I can do
        // nothing", which must be believed rather than upgraded to Legacy.
        var set = SidecarCapabilitySet.FromResponse(new CapabilitiesInfo(3, Guid.NewGuid(), []));

        Assert.False(set.Has(SidecarCapabilities.Segment));
        Assert.Empty(set.Capabilities);
    }

    [Fact]
    public void Has_IsCaseSensitive()
    {
        // The constants exist precisely so both sides compile against the same spelling; a
        // near-miss must not silently match.
        var set = SidecarCapabilitySet.FromResponse(new CapabilitiesInfo(3, Guid.NewGuid(), ["probe"]));

        Assert.True(set.Has("probe"));
        Assert.False(set.Has("Probe"));
    }
}
