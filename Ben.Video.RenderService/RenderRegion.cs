namespace Ben.Video.RenderService;

/// <summary>
/// Lifecycle of one timeline region's cached preview render, tracked against its content
/// <see cref="RenderRegion.Signature"/>. <see cref="RenderingRough"/>/<see cref="Rough"/> are
/// reserved for the two-pass background renderer (item #36 phase D) — phase A only ever
/// transitions between <see cref="Stale"/> and <see cref="Fine"/>.
/// </summary>
public enum RenderRegionState
{
    /// <summary>No cached render matches the current signature.</summary>
    Stale,

    /// <summary>Background renderer reserved — fast/low-quality pass in progress.</summary>
    RenderingRough,

    /// <summary>Background renderer reserved — fast/low-quality pass cached, fine pass not started.</summary>
    Rough,

    /// <summary>Background renderer reserved — full-quality pass in progress.</summary>
    RenderingFine,

    /// <summary>A render matching the current signature is cached and ready to use.</summary>
    Fine,
}

/// <summary>
/// What the host (the editor) reports for one primary-track clip on every recompute — everything
/// needed to detect staleness, nothing about how it renders. <see cref="Start"/>/<see cref="Duration"/>
/// are display-only (timeline-bar layout); deliberately NOT part of <see cref="RenderRegionTracker"/>'s
/// staleness comparison, so repositioning/reordering a clip never invalidates its cached render — see
/// <see cref="RenderRegion.Signature"/>.
/// </summary>
public sealed record RenderRegionInput(Guid ClipId, double Start, double Duration, string Signature);

/// <summary>
/// One primary-track clip's render state, as tracked by <see cref="RenderRegionTracker"/>. Mutable —
/// owned by the tracker, read by the UI.
/// </summary>
public sealed class RenderRegion
{
    public required Guid   ClipId   { get; init; }
    public double          Start    { get; internal set; }
    public double          Duration { get; internal set; }

    /// <summary>
    /// Identity of the exact content this region would render — a hash of every input that
    /// affects the segment's rendered bytes (source, trim, effects, speed, preview scale, etc.).
    /// Two regions with equal signatures are guaranteed to render identical output. Deliberately
    /// excludes <see cref="Start"/> and track order — see <see cref="RenderRegionInput"/>.
    /// </summary>
    public string Signature { get; internal set; } = string.Empty;

    public RenderRegionState State      { get; internal set; } = RenderRegionState.Stale;
    public int               ProgressPct { get; internal set; }

    /// <summary>The rendered segment's storage handle (a MEMFS filename for the real backend),
    /// set by <see cref="RenderRegionTracker.MarkRendered"/>. Null until <see cref="State"/> first
    /// reaches <see cref="RenderRegionState.Rough"/> or <see cref="RenderRegionState.Fine"/>.
    /// Opaque to this project — only the render backend and its consumer (assembly) interpret it.</summary>
    public string? SegmentName { get; internal set; }
}
