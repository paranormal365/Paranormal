namespace Ben.Video.Editor.Services;

/// <summary>Why an export assembly did or didn't qualify for the sidecar.</summary>
public enum ExportAssembleDecision
{
    UseSidecar,
    SidecarUnavailable,
    /// <summary>Transitions need an xfade filtergraph, not a concat.</summary>
    HasTransitions,
    /// <summary>At least one segment has no retained twin on the sidecar.</summary>
    SegmentsNotRetainedRemotely,
    /// <summary>At least one audio clip has no OPFS-backed source, so it can't be uploaded.</summary>
    AudioNotUploadable,
    NothingToAssemble,
}

/// <summary>
/// Decides whether an export's concat (+ audio mix) can run in the sidecar — item #70 phase 162.
///
/// <para>Same shape and same conservatism as <see cref="PreviewConcatGate"/>: pure, static, and
/// biased toward wasm. The stakes are higher here though — a preview is transient and re-renders
/// on the next edit, whereas a wrong decision on an export produces a <b>file the user keeps</b>.
/// So every condition is required, and the fallback is total: on any failure the caller re-runs
/// today's pipeline from the segments still in MEMFS (dual residency), with no rework.</para>
/// </summary>
public static class ExportAssembleGate
{
    /// <param name="segmentNames">Ordered export segments (already rendered).</param>
    /// <param name="hasTransitions">Any same-track transition needing an xfade.</param>
    /// <param name="sidecarAssembleAvailable">Connected AND advertising export-assemble.</param>
    /// <param name="remoteIds">Segment name → retained remote id, or null.</param>
    /// <param name="audioClipsAllOpfsBacked">Every standalone audio clip has an OPFS source.</param>
    public static ExportAssembleDecision Decide(
        IReadOnlyList<string> segmentNames,
        bool hasTransitions,
        bool sidecarAssembleAvailable,
        Func<string, Guid?> remoteIds,
        bool audioClipsAllOpfsBacked)
    {
        if (segmentNames.Count == 0) return ExportAssembleDecision.NothingToAssemble;
        if (hasTransitions) return ExportAssembleDecision.HasTransitions;
        if (!sidecarAssembleAvailable) return ExportAssembleDecision.SidecarUnavailable;

        // All-or-nothing: assembling a subset would silently ship an export missing footage — the
        // single worst outcome available to this code path.
        if (segmentNames.Any(n => remoteIds(n) is null))
            return ExportAssembleDecision.SegmentsNotRetainedRemotely;

        // An audio clip with no OPFS source can't be uploaded, and mixing without it would ship an
        // export that is silently missing a track.
        if (!audioClipsAllOpfsBacked) return ExportAssembleDecision.AudioNotUploadable;

        return ExportAssembleDecision.UseSidecar;
    }
}
