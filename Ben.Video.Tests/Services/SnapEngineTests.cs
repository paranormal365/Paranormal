using Ben.Video.Editor.Models;
using Ben.Video.Editor.Services;

namespace Ben.Video.Tests.Services;

public sealed class SnapEngineTests
{
    // ── CollectSnapTargets ────────────────────────────────────────────────────

    [Fact]
    public void CollectSnapTargets_EmptyTracksAndMarkers_ReturnsEmpty()
    {
        var result = SnapEngine.CollectSnapTargets([], []);
        Assert.Empty(result);
    }

    [Fact]
    public void CollectSnapTargets_MarkerOnly_ReturnsMarkerPosition()
    {
        var marker = new TimelineMarker { TimeSeconds = 5.0, Label = "Intro" };
        var result = SnapEngine.CollectSnapTargets([], [marker]);
        Assert.Single(result);
        Assert.Equal(5.0, result[0]);
    }

    [Fact]
    public void CollectSnapTargets_VideoClip_ReturnsStartAndEnd()
    {
        var clip = new VideoClip { Duration = 10.0, TimelinePosition = 3.0 };
        var track = new TimelineTrack();
        track.Items.Add(clip);

        var result = SnapEngine.CollectSnapTargets([track], []);

        Assert.Contains(3.0,  result);
        Assert.Contains(13.0, result);
    }

    [Fact]
    public void CollectSnapTargets_AudioClip_ReturnsStartAndEnd()
    {
        var clip = new AudioClip { Duration = 4.0, TimelinePosition = 2.0 };
        var track = new TimelineTrack { Type = TrackType.Audio };
        track.Items.Add(clip);

        var result = SnapEngine.CollectSnapTargets([track], []);

        Assert.Contains(2.0, result);
        Assert.Contains(6.0, result);
    }

    [Fact]
    public void CollectSnapTargets_DuplicatePositions_Deduplicated()
    {
        // Two clips that share an edge at 10.0
        var clip1 = new VideoClip { Duration = 10.0, TimelinePosition = 0.0 };
        var clip2 = new VideoClip { Duration = 5.0,  TimelinePosition = 10.0 };
        var track = new TimelineTrack();
        track.Items.Add(clip1);
        track.Items.Add(clip2);

        var result = SnapEngine.CollectSnapTargets([track], []);

        // 0.0, 10.0 (shared), 15.0 — 10.0 appears once
        Assert.Equal(result.Distinct().Count(), result.Count);
        Assert.Contains(10.0, result);
    }

    [Fact]
    public void CollectSnapTargets_ResultIsSorted()
    {
        var clip = new VideoClip { Duration = 5.0, TimelinePosition = 7.0 };
        var marker = new TimelineMarker { TimeSeconds = 3.0, Label = "A" };
        var track = new TimelineTrack();
        track.Items.Add(clip);

        var result = SnapEngine.CollectSnapTargets([track], [marker]);

        for (var i = 1; i < result.Count; i++)
            Assert.True(result[i] >= result[i - 1], "Result should be sorted ascending");
    }

    [Fact]
    public void CollectSnapTargets_ExcludeItemId_OmitsThatClipsOwnEdges()
    {
        var dragged = new VideoClip { Duration = 10.0, TimelinePosition = 3.0 };
        var other   = new VideoClip { Duration = 5.0,  TimelinePosition = 20.0 };
        var track   = new TimelineTrack();
        track.Items.Add(dragged);
        track.Items.Add(other);

        var result = SnapEngine.CollectSnapTargets([track], [], excludeItemId: dragged.Id);

        Assert.DoesNotContain(3.0,  result); // dragged clip's own start
        Assert.DoesNotContain(13.0, result); // dragged clip's own end
        Assert.Contains(20.0, result);       // other clip's edges still present
        Assert.Contains(25.0, result);
    }

    [Fact]
    public void CollectSnapTargets_ExcludeItemId_OnAudioClip_OmitsItsOwnEdges()
    {
        var dragged = new AudioClip { Duration = 4.0, TimelinePosition = 2.0 };
        var track   = new TimelineTrack { Type = TrackType.Audio };
        track.Items.Add(dragged);

        var result = SnapEngine.CollectSnapTargets([track], [], excludeItemId: dragged.Id);

        Assert.Empty(result);
    }

    [Fact]
    public void CollectSnapTargets_NoExcludeItemId_IncludesEveryClipsEdges()
    {
        var clip  = new VideoClip { Duration = 10.0, TimelinePosition = 3.0 };
        var track = new TimelineTrack();
        track.Items.Add(clip);

        var result = SnapEngine.CollectSnapTargets([track], []);

        Assert.Contains(3.0,  result);
        Assert.Contains(13.0, result);
    }

    // ── Snap ─────────────────────────────────────────────────────────────────

    [Fact]
    public void Snap_NoTargets_ReturnsOriginalPosition()
    {
        var result = SnapEngine.Snap(5.0, [], 0.5);
        Assert.Equal(5.0, result);
    }

    [Fact]
    public void Snap_ZeroThreshold_ReturnsOriginalPosition()
    {
        var result = SnapEngine.Snap(5.0, [5.1], 0.0);
        Assert.Equal(5.0, result);
    }

    [Fact]
    public void Snap_WithinThreshold_SnapsToTarget()
    {
        var result = SnapEngine.Snap(5.3, [5.0, 10.0], 0.5);
        Assert.Equal(5.0, result);
    }

    [Fact]
    public void Snap_ExactlyAtThreshold_SnapsToTarget()
    {
        var result = SnapEngine.Snap(5.5, [5.0], 0.5);
        Assert.Equal(5.0, result);
    }

    [Fact]
    public void Snap_BeyondThreshold_ReturnsOriginalPosition()
    {
        var result = SnapEngine.Snap(5.6, [5.0], 0.5);
        Assert.Equal(5.6, result);
    }

    [Fact]
    public void Snap_MultipleCandidates_SnapsToNearest()
    {
        // Position 4.8 is 0.2 from 5.0 and 1.8 from 3.0
        var result = SnapEngine.Snap(4.8, [3.0, 5.0], 0.5);
        Assert.Equal(5.0, result);
    }

    // ── ActiveSnapTarget ─────────────────────────────────────────────────────

    [Fact]
    public void ActiveSnapTarget_NoTargets_ReturnsNull()
    {
        var result = SnapEngine.ActiveSnapTarget(5.0, [], 0.5);
        Assert.Null(result);
    }

    [Fact]
    public void ActiveSnapTarget_ZeroThreshold_ReturnsNull()
    {
        var result = SnapEngine.ActiveSnapTarget(5.0, [5.0], 0.0);
        Assert.Null(result);
    }

    [Fact]
    public void ActiveSnapTarget_WithinThreshold_ReturnsTarget()
    {
        var result = SnapEngine.ActiveSnapTarget(5.3, [5.0, 10.0], 0.5);
        Assert.Equal(5.0, result);
    }

    [Fact]
    public void ActiveSnapTarget_BeyondThreshold_ReturnsNull()
    {
        var result = SnapEngine.ActiveSnapTarget(5.6, [5.0], 0.5);
        Assert.Null(result);
    }

    [Fact]
    public void ActiveSnapTarget_MultipleCandidates_ReturnsNearest()
    {
        var result = SnapEngine.ActiveSnapTarget(4.8, [3.0, 5.0], 0.5);
        Assert.Equal(5.0, result);
    }
}
