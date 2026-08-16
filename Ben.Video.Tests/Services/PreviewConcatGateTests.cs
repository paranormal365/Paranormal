using Ben.Video.Editor.Services;

namespace Ben.Video.Tests.Services;

/// <summary>
/// Item #70 phase 161. The gate deciding whether the Working Window preview assembles natively.
///
/// <para>The invariant worth stating plainly: <b>the conservative direction is always wasm</b>. A
/// wrong "use sidecar" produces a visibly broken preview — dropped footage, or a hard cut where
/// the user placed a crossfade — while a wrong "use wasm" costs only the speed this phase wins
/// back. Every one of these tests is really asserting that asymmetry.</para>
/// </summary>
public sealed class PreviewConcatGateTests
{
    private static Func<string, Guid?> AllRemote(params string[] names)
    {
        var map = names.ToDictionary(n => n, _ => Guid.NewGuid());
        return n => map.TryGetValue(n, out var id) ? id : null;
    }

    private static Func<string, Guid?> NoneRemote() => _ => null;

    [Fact]
    public void AllBackgroundSegmentsRetainedRemotely_UsesSidecar()
    {
        var names = new[] { "bgseg_a.mp4", "bgseg_b.mp4" };

        var decision = PreviewConcatGate.Decide(
            names, hasTransitions: false, sidecarConcatAvailable: true, AllRemote(names));

        Assert.Equal(PreviewConcatDecision.UseSidecar, decision);
    }

    [Fact]
    public void Transitions_StayWasm()
    {
        // A stream copy cannot blend a junction — taking the native path here would silently turn
        // the user's crossfade into a hard cut.
        var names = new[] { "bgseg_a.mp4", "bgseg_b.mp4" };

        var decision = PreviewConcatGate.Decide(
            names, hasTransitions: true, sidecarConcatAvailable: true, AllRemote(names));

        Assert.Equal(PreviewConcatDecision.HasTransitions, decision);
    }

    [Fact]
    public void MixedSegmentSet_StaysWasm()
    {
        // One clip was edited this pass and re-encoded in-browser, so it exists only in MEMFS.
        // We deliberately do NOT upload it to make the set uniform — that complexity is exactly
        // what dual residency exists to avoid.
        var names = new[] { "bgseg_a.mp4", "preview_vid_b.mp4" };

        var decision = PreviewConcatGate.Decide(
            names, hasTransitions: false, sidecarConcatAvailable: true, AllRemote(names));

        Assert.Equal(PreviewConcatDecision.MixedOrUnpinnedSegments, decision);
    }

    [Fact]
    public void SidecarUnavailable_StaysWasm()
    {
        var names = new[] { "bgseg_a.mp4" };

        var decision = PreviewConcatGate.Decide(
            names, hasTransitions: false, sidecarConcatAvailable: false, AllRemote(names));

        Assert.Equal(PreviewConcatDecision.SidecarUnavailable, decision);
    }

    [Fact]
    public void AnySegmentMissingFromTheIndex_StaysWasm()
    {
        // Partial availability is the dangerous case: concatenating only the segments that happen
        // to have remote twins would silently drop the rest of the timeline.
        var names = new[] { "bgseg_a.mp4", "bgseg_b.mp4" };
        var onlyFirst = AllRemote("bgseg_a.mp4");

        var decision = PreviewConcatGate.Decide(
            names, hasTransitions: false, sidecarConcatAvailable: true, onlyFirst);

        Assert.Equal(PreviewConcatDecision.SegmentsNotRetainedRemotely, decision);
    }

    [Fact]
    public void NoSegmentsAtAll_ReportsNothingToAssemble()
    {
        var decision = PreviewConcatGate.Decide(
            [], hasTransitions: false, sidecarConcatAvailable: true, NoneRemote());

        Assert.Equal(PreviewConcatDecision.NothingToAssemble, decision);
    }

    [Fact]
    public void TransitionsReportedEvenWhenSidecarIsDown()
    {
        // Reason precedence: report the intrinsic blocker (transitions) rather than an incidental
        // one, so a diagnostics read-out doesn't send someone chasing a sidecar problem that
        // wouldn't have changed the outcome.
        var names = new[] { "bgseg_a.mp4" };

        var decision = PreviewConcatGate.Decide(
            names, hasTransitions: true, sidecarConcatAvailable: false, NoneRemote());

        Assert.Equal(PreviewConcatDecision.HasTransitions, decision);
    }

    [Fact]
    public void SegmentNamesAreMatchedByPrefixNotSubstring()
    {
        // A name merely CONTAINING the marker isn't a background segment; only one produced by the
        // background renderer (which always prefixes) carries the pinned codec/fps guarantees the
        // stream copy depends on.
        var names = new[] { "preview_vid_bgseg_lookalike.mp4" };

        var decision = PreviewConcatGate.Decide(
            names, hasTransitions: false, sidecarConcatAvailable: true, AllRemote(names));

        Assert.Equal(PreviewConcatDecision.MixedOrUnpinnedSegments, decision);
    }
}
