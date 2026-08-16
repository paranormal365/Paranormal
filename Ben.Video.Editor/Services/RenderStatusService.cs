using Ben.Video.RenderService;

namespace Ben.Video.Editor.Services;

/// <summary>
/// Bridges <see cref="ClipStore"/>'s primary-track clips into <see cref="RenderRegionTracker"/> —
/// the Editor-specific half of item #36 phase A. Builds each clip's <see cref="RenderSignatureBuilder"/>
/// signature and syncs it on every <see cref="ClipStore.OnChange"/>, so the timeline's per-region bar
/// reflects real per-clip staleness instead of the old whole-timeline boolean
/// (<see cref="PreviewFreshnessService"/>, kept as the fallback when this is unused).
///
/// Phase A has no per-clip renderer yet — <see cref="MarkAllCurrentRendered"/> is called once after
/// a (still whole-pipeline) Preview render succeeds, marking every region matching its
/// currently-computed signature as rendered. Any edit made after that only grays out the regions
/// it actually affected, which is the visible improvement phase A ships. A real per-clip renderer
/// (later phases) will call <see cref="RenderRegionTracker.MarkRendered"/> per clip instead.
/// </summary>
public sealed class RenderStatusService : IDisposable
{
    private readonly ClipStore _clips;
    private readonly PreviewQualityService _quality;
    private readonly ExportResolutionService _resolution;
    private readonly RenderRegionTracker _tracker;

    public RenderStatusService(
        ClipStore clips,
        PreviewQualityService quality,
        ExportResolutionService resolution,
        RenderRegionTracker tracker)
    {
        _clips      = clips;
        _quality    = quality;
        _resolution = resolution;
        _tracker    = tracker;

        _clips.OnChange     += Resync;
        _quality.OnChanged  += Resync;
        _resolution.OnChanged += Resync;

        Resync();
    }

    public IReadOnlyList<RenderRegion> Regions => _tracker.Regions;

    /// <summary>The preview render's target width/height — same basis <see cref="RenderSignatureBuilder"/>
    /// hashes into every region's signature. Exposed so <see cref="PreviewSegmentCache"/> consumers
    /// (phase B) key/encode segments at the exact dimensions a region's signature assumes.</summary>
    public (int Width, int Height) PreviewDimensions() => ComputePreviewDimensions();

    public event Action? OnChanged
    {
        add    => _tracker.OnChanged += value;
        remove => _tracker.OnChanged -= value;
    }

    /// <summary>Marks every region whose current signature was just rendered by the (whole-pipeline,
    /// phase A) Preview pass as up to date. See class remarks. Passes each region's own current
    /// <see cref="RenderRegion.SegmentName"/> back through rather than defaulting to null — a region
    /// the background render worker (phase C) already rendered keeps its segment name, so a later
    /// Preview click can still reuse it; only regions with no background segment stay null.
    /// Two-pass nuances (phase D): a <see cref="RenderRegionState.Rough"/> region stays Rough —
    /// promoting it to Fine here would silently cancel its pending background fine pass and freeze
    /// it at rough quality forever — and regions with an in-flight background render
    /// (RenderingRough/RenderingFine) are left entirely alone so their live progress isn't stomped.</summary>
    public void MarkAllCurrentRendered()
    {
        foreach (var region in _tracker.Regions)
        {
            if (region.State is RenderRegionState.RenderingRough or RenderRegionState.RenderingFine)
                continue;
            var resultState = region.State == RenderRegionState.Rough
                ? RenderRegionState.Rough
                : RenderRegionState.Fine;
            _tracker.MarkRendered(region.ClipId, region.Signature, region.SegmentName, resultState);
        }
    }

    private void Resync()
    {
        var (previewWidth, previewHeight) = ComputePreviewDimensions();

        var inputs = _clips.PrimaryVideoTrack.VideoClips
            .Select(c => new RenderRegionInput(
                c.Id, c.TimelinePosition, c.EffectiveDuration,
                RenderSignatureBuilder.ForVideoClip(c, previewWidth, previewHeight)))
            .Concat(_clips.PrimaryVideoTrack.ImageClips
                .Select(c => new RenderRegionInput(
                    c.Id, c.TimelinePosition, c.Duration > 0 ? c.Duration : 5.0,
                    RenderSignatureBuilder.ForImageClip(c, previewWidth, previewHeight))))
            .ToList();

        _tracker.Sync(inputs);
    }

    private (int Width, int Height) ComputePreviewDimensions()
    {
        var scale = _quality.ScalePercent / 100.0;
        var width  = (int)Math.Round(_resolution.Width  * scale / 2) * 2; // even dims for yuv420p
        var height = (int)Math.Round(_resolution.Height * scale / 2) * 2;
        return (Math.Max(2, width), Math.Max(2, height));
    }

    public void Dispose()
    {
        _clips.OnChange       -= Resync;
        _quality.OnChanged    -= Resync;
        _resolution.OnChanged -= Resync;
    }
}
