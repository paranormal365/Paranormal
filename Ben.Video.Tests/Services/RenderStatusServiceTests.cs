using Ben.Video.Editor.Models;
using Ben.Video.Editor.Services;
using Ben.Video.RenderService;
using Microsoft.Extensions.Options;

namespace Ben.Video.Tests.Services;

public sealed class RenderStatusServiceTests
{
    private static (ClipStore Clips, RenderStatusService Status) CreateService()
    {
        var clips  = new ClipStore(Options.Create(new VideoEditorOptions()));
        var status = new RenderStatusService(
            clips, new PreviewQualityService(), new ExportResolutionService(), new RenderRegionTracker());
        return (clips, status);
    }

    [Fact]
    public void AddingVideoClip_CreatesStaleRegion()
    {
        var (clips, status) = CreateService();

        clips.AddClip(new VideoClip { MemFsName = "a.mp4", Duration = 5.0, EndTrim = 5.0 });

        var region = Assert.Single(status.Regions);
        Assert.Equal(RenderRegionState.Stale, region.State);
    }

    [Fact]
    public void MarkAllCurrentRendered_MarksEveryRegionFine()
    {
        var (clips, status) = CreateService();
        clips.AddClip(new VideoClip { MemFsName = "a.mp4", Duration = 5.0, EndTrim = 5.0 });
        clips.AddClip(new VideoClip { MemFsName = "b.mp4", Duration = 3.0, EndTrim = 3.0 });

        status.MarkAllCurrentRendered();

        Assert.All(status.Regions, r => Assert.Equal(RenderRegionState.Fine, r.State));
    }

    [Fact]
    public void MarkAllCurrentRendered_PreservesExistingSegmentName()
    {
        // Item #36 phase C: a region the background render worker already rendered carries a
        // SegmentName. MarkAllCurrentRendered used to call MarkRendered without one, silently
        // wiping it back to null on every Preview click and permanently hiding the background
        // segment from later consumption. It must now pass the existing value back through.
        var tracker = new RenderRegionTracker();
        var clips   = new ClipStore(Options.Create(new VideoEditorOptions()));
        var status  = new RenderStatusService(clips, new PreviewQualityService(), new ExportResolutionService(), tracker);
        clips.AddClip(new VideoClip { MemFsName = "a.mp4", Duration = 5.0, EndTrim = 5.0 });
        var region = status.Regions.Single();
        tracker.MarkRendered(region.ClipId, region.Signature, "bg_segment.mp4");

        status.MarkAllCurrentRendered();

        Assert.Equal("bg_segment.mp4", status.Regions.Single().SegmentName);
    }

    [Fact]
    public void MarkAllCurrentRendered_PreservesRoughState()
    {
        // Item #36 phase D: a Rough region still has its background fine pass pending. Promoting
        // it to Fine here would make PickNext skip that fine pass forever, silently freezing the
        // clip at rough quality (and painting the bar bright green over rough content).
        var tracker = new RenderRegionTracker();
        var clips   = new ClipStore(Options.Create(new VideoEditorOptions()));
        var status  = new RenderStatusService(clips, new PreviewQualityService(), new ExportResolutionService(), tracker);
        clips.AddClip(new VideoClip { MemFsName = "a.mp4", Duration = 5.0, EndTrim = 5.0 });
        var region = status.Regions.Single();
        tracker.MarkRendered(region.ClipId, region.Signature, "rough_seg.mp4", RenderRegionState.Rough);

        status.MarkAllCurrentRendered();

        Assert.Equal(RenderRegionState.Rough, status.Regions.Single().State);
        Assert.Equal("rough_seg.mp4", status.Regions.Single().SegmentName);
    }

    [Fact]
    public void MarkAllCurrentRendered_LeavesInFlightRendersAlone()
    {
        var tracker = new RenderRegionTracker();
        var clips   = new ClipStore(Options.Create(new VideoEditorOptions()));
        var status  = new RenderStatusService(clips, new PreviewQualityService(), new ExportResolutionService(), tracker);
        clips.AddClip(new VideoClip { MemFsName = "a.mp4", Duration = 5.0, EndTrim = 5.0 });
        var region = status.Regions.Single();
        tracker.MarkProgress(region.ClipId, RenderRegionState.RenderingRough, 40);

        status.MarkAllCurrentRendered();

        Assert.Equal(RenderRegionState.RenderingRough, status.Regions.Single().State);
        Assert.Equal(40, status.Regions.Single().ProgressPct);
    }

    [Fact]
    public void EditingOneClip_OnlyGraysOutThatRegion()
    {
        var (clips, status) = CreateService();
        clips.AddClip(new VideoClip { MemFsName = "a.mp4", Duration = 5.0, EndTrim = 5.0 });
        clips.AddClip(new VideoClip { MemFsName = "b.mp4", Duration = 3.0, EndTrim = 3.0 });
        status.MarkAllCurrentRendered();

        var target = clips.PrimaryVideoTrack.VideoClips.First(c => c.MemFsName == "a.mp4");
        clips.UpdateTrim(target.Id, 0.5, 5.0);

        var editedRegion   = status.Regions.Single(r => r.ClipId == target.Id);
        var untouchedRegion = status.Regions.Single(r => r.ClipId != target.Id);
        Assert.Equal(RenderRegionState.Stale, editedRegion.State);
        Assert.Equal(RenderRegionState.Fine,  untouchedRegion.State);
    }

    [Fact]
    public void RepositioningClip_DoesNotInvalidateRenderedRegion()
    {
        var (clips, status) = CreateService();
        clips.AddClip(new VideoClip { MemFsName = "a.mp4", Duration = 5.0, EndTrim = 5.0 });
        status.MarkAllCurrentRendered();
        var clipId = status.Regions.Single().ClipId;

        clips.MoveClip(clipId, 10.0);

        Assert.Equal(RenderRegionState.Fine, status.Regions.Single().State);
    }

    [Fact]
    public void RemovingClip_RemovesItsRegion()
    {
        var (clips, status) = CreateService();
        clips.AddClip(new VideoClip { MemFsName = "a.mp4", Duration = 5.0, EndTrim = 5.0 });
        var clipId = status.Regions.Single().ClipId;

        clips.RemoveClip(clipId);

        Assert.Empty(status.Regions);
    }

    // ── PreviewDimensions — item #36 phase B relies on this matching what's hashed
    // into each region's signature (see RenderSignatureBuilderTests) ──────────

    [Fact]
    public void PreviewDimensions_FullScale_MatchesExportResolution()
    {
        var clips = new ClipStore(Options.Create(new VideoEditorOptions()));
        var resolution = new ExportResolutionService();
        resolution.SetResolution("1920x1080");
        var quality = new PreviewQualityService();
        quality.SetScalePercent(100); // explicit — the service's own default is no longer 100%
        var status = new RenderStatusService(clips, quality, resolution, new RenderRegionTracker());

        Assert.Equal((1920, 1080), status.PreviewDimensions());
    }

    [Fact]
    public void PreviewDimensions_HalfScale_HalvesResolution()
    {
        var clips = new ClipStore(Options.Create(new VideoEditorOptions()));
        var resolution = new ExportResolutionService();
        resolution.SetResolution("1920x1080");
        var quality = new PreviewQualityService();
        quality.SetScalePercent(50);
        var status = new RenderStatusService(clips, quality, resolution, new RenderRegionTracker());

        Assert.Equal((960, 540), status.PreviewDimensions());
    }

    [Fact]
    public void ChangingPreviewQuality_ChangesEveryRegionSignature()
    {
        // Confirms the region signature really does depend on PreviewDimensions() — a quality
        // change must gray out every region even though no clip content changed, since the
        // assembled preview's own resolution is about to differ.
        var clips   = new ClipStore(Options.Create(new VideoEditorOptions()));
        var quality = new PreviewQualityService();
        var status  = new RenderStatusService(clips, quality, new ExportResolutionService(), new RenderRegionTracker());
        clips.AddClip(new VideoClip { MemFsName = "a.mp4", Duration = 5.0, EndTrim = 5.0 });
        status.MarkAllCurrentRendered();

        quality.SetScalePercent(50);

        Assert.Equal(RenderRegionState.Stale, status.Regions.Single().State);
    }
}
