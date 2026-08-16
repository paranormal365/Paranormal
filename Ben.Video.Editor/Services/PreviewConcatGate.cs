namespace Ben.Video.Editor.Services;

/// <summary>Why a preview assembly did or didn't qualify for the native path — the reason is kept
/// (rather than a bare bool) so the diagnostics panel can explain a fallback instead of leaving
/// "why is this still slow?" unanswerable.</summary>
public enum PreviewConcatDecision
{
    /// <summary>Every condition met — submit one sidecar concat job.</summary>
    UseSidecar,
    SidecarUnavailable,
    /// <summary>A transition needs an xfade blend, which a stream copy cannot do.</summary>
    HasTransitions,
    /// <summary>At least one segment was encoded in-browser this pass, so it has no remote twin.</summary>
    MixedOrUnpinnedSegments,
    /// <summary>All segments are background-rendered, but at least one isn't in the remote index —
    /// typically rendered before the sidecar connected, or dropped when it restarted.</summary>
    SegmentsNotRetainedRemotely,
    NothingToAssemble,
}

/// <summary>
/// Decides whether the Working Window's assembly can run as one sidecar concat instead of an
/// in-browser one — item #70 phase 161.
///
/// <para>Pure and static so every branch is testable without a sidecar, a browser, or ffmpeg. The
/// caller supplies already-resolved facts; this makes no I/O and holds no state.</para>
///
/// <para><b>The conservative direction is always "use wasm".</b> A wrong "yes" produces a visibly
/// broken preview (missing footage, or a hard cut where the user placed a crossfade); a wrong "no"
/// costs only the speed this phase is trying to win back. Every condition below is therefore
/// required, not merely preferred.</para>
/// </summary>
public static class PreviewConcatGate
{
    /// <summary>Prefix marking a segment produced by the background renderer with pinned
    /// codec/dimensions/fps — the same marker the existing stream-copy fast path keys on.</summary>
    public const string BackgroundSegmentPrefix = "bgseg_";

    /// <param name="orderedSegmentNames">Assembly inputs, in timeline order.</param>
    /// <param name="hasTransitions">True when any junction needs an xfade blend.</param>
    /// <param name="sidecarConcatAvailable">Connected AND advertising the concat capability.</param>
    /// <param name="remoteIds">Resolver from segment name to its retained remote id, or null.</param>
    public static PreviewConcatDecision Decide(
        IReadOnlyList<string> orderedSegmentNames,
        bool hasTransitions,
        bool sidecarConcatAvailable,
        Func<string, Guid?> remoteIds)
    {
        if (orderedSegmentNames.Count == 0) return PreviewConcatDecision.NothingToAssemble;

        // Checked before availability so the reason reported is the intrinsic one: a timeline with
        // transitions can't take this path even with a perfectly healthy sidecar.
        if (hasTransitions) return PreviewConcatDecision.HasTransitions;

        // Any synchronously-encoded segment (preview_vid_/preview_img_) exists only in MEMFS. We
        // deliberately do NOT upload those to make the set uniform — that is exactly the
        // complexity dual residency (phase 160) exists to avoid, and it would move more bytes than
        // the offload saves.
        if (!orderedSegmentNames.All(n => n.StartsWith(BackgroundSegmentPrefix, StringComparison.Ordinal)))
            return PreviewConcatDecision.MixedOrUnpinnedSegments;

        if (!sidecarConcatAvailable) return PreviewConcatDecision.SidecarUnavailable;

        // All-or-nothing: a partial set would splice a subset and silently drop footage.
        if (orderedSegmentNames.Any(n => remoteIds(n) is null))
            return PreviewConcatDecision.SegmentsNotRetainedRemotely;

        return PreviewConcatDecision.UseSidecar;
    }
}
