using Ben.Video.Editor.Services;

namespace Ben.Video.Tests.Services;

/// <summary>
/// Item #70 phase 162. Same conservatism as <see cref="PreviewConcatGate"/>, but the stakes are
/// higher: a preview is transient and re-renders on the next edit, while a wrong decision here
/// produces <b>a file the user keeps</b>. Every test below is an "assembling a subset would ship a
/// broken export" guard.
/// </summary>
public sealed class ExportAssembleGateTests
{
    private static Func<string, Guid?> AllRemote(params string[] names)
    {
        var map = names.ToDictionary(n => n, _ => Guid.NewGuid());
        return n => map.TryGetValue(n, out var id) ? id : null;
    }

    [Fact]
    public void AllConditionsMet_UsesSidecar()
    {
        var names = new[] { "seg_a.mp4", "seg_b.mp4" };

        Assert.Equal(ExportAssembleDecision.UseSidecar, ExportAssembleGate.Decide(
            names, hasTransitions: false, sidecarAssembleAvailable: true,
            AllRemote(names), audioClipsAllOpfsBacked: true));
    }

    [Fact]
    public void Transitions_StayWasm()
    {
        var names = new[] { "seg_a.mp4", "seg_b.mp4" };

        Assert.Equal(ExportAssembleDecision.HasTransitions, ExportAssembleGate.Decide(
            names, hasTransitions: true, sidecarAssembleAvailable: true,
            AllRemote(names), audioClipsAllOpfsBacked: true));
    }

    [Fact]
    public void SidecarUnavailable_StaysWasm()
    {
        var names = new[] { "seg_a.mp4" };

        Assert.Equal(ExportAssembleDecision.SidecarUnavailable, ExportAssembleGate.Decide(
            names, hasTransitions: false, sidecarAssembleAvailable: false,
            AllRemote(names), audioClipsAllOpfsBacked: true));
    }

    [Fact]
    public void AnySegmentMissingRemotely_StaysWasm()
    {
        // The worst available outcome: assembling only the segments with remote twins would ship
        // an export silently missing footage.
        var names = new[] { "seg_a.mp4", "seg_b.mp4" };

        Assert.Equal(ExportAssembleDecision.SegmentsNotRetainedRemotely, ExportAssembleGate.Decide(
            names, hasTransitions: false, sidecarAssembleAvailable: true,
            AllRemote("seg_a.mp4"), audioClipsAllOpfsBacked: true));
    }

    [Fact]
    public void AudioClipWithoutOpfsSource_StaysWasm()
    {
        // It can't be uploaded, and mixing without it would ship an export missing a track.
        var names = new[] { "seg_a.mp4" };

        Assert.Equal(ExportAssembleDecision.AudioNotUploadable, ExportAssembleGate.Decide(
            names, hasTransitions: false, sidecarAssembleAvailable: true,
            AllRemote(names), audioClipsAllOpfsBacked: false));
    }

    [Fact]
    public void NoSegments_ReportsNothingToAssemble()
    {
        Assert.Equal(ExportAssembleDecision.NothingToAssemble, ExportAssembleGate.Decide(
            [], hasTransitions: false, sidecarAssembleAvailable: true,
            _ => Guid.NewGuid(), audioClipsAllOpfsBacked: true));
    }

    [Fact]
    public void NoAudioClipsAtAll_CountsAsUploadable()
    {
        // Vacuous truth: "every audio clip is OPFS-backed" over an empty set must not block a
        // perfectly valid audio-free export.
        var names = new[] { "seg_a.mp4" };

        Assert.Equal(ExportAssembleDecision.UseSidecar, ExportAssembleGate.Decide(
            names, hasTransitions: false, sidecarAssembleAvailable: true,
            AllRemote(names), audioClipsAllOpfsBacked: true));
    }
}
