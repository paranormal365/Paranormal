using Ben.Video.Editor.Models;

namespace Ben.Video.Tests.Models;

public sealed class TimelineTrackTests
{
    private static TimelineTrack MakeVideoTrack(int order = 0) => new()
    {
        Label = "Video 1",
        Type  = TrackType.Video,
        Order = order
    };

    [Fact]
    public void NewTrack_Items_IsEmpty()
    {
        var track = MakeVideoTrack();

        Assert.Empty(track.Items);
    }

    [Fact]
    public void TotalDuration_EmptyTrack_ReturnsZero()
    {
        var track = MakeVideoTrack();

        Assert.Equal(0, track.TotalDuration);
    }

    [Fact]
    public void TotalDuration_WithClips_ReturnsMaxEndTime()
    {
        var track = MakeVideoTrack();
        track.Items.Add(new VideoClip { TimelinePosition = 0,   Duration = 5 });
        track.Items.Add(new VideoClip { TimelinePosition = 5,   Duration = 3 });

        Assert.Equal(8, track.TotalDuration);
    }

    /// <summary>
    /// Item #57 T1: a Transition's own TimelinePosition/Duration overlap the junction between its
    /// two clips (see ClipStore.AddTransition — centred on the boundary, TimelinePosition =
    /// fromEnd - duration/2), so its own end point is always strictly less than its "to" clip's
    /// real end (enforced by TransitionEditor's duration clamp: duration &lt;= min(fromDur,
    /// toDur) * 0.9, so duration/2 &lt; toDur). TotalDuration's existing Max-over-all-items
    /// formula is therefore already correct with transitions present — this pins that down as a
    /// regression test rather than leaving it as an unverified assumption.
    /// </summary>
    [Fact]
    public void TotalDuration_TransitionOverlappingJunction_DoesNotInflateOrShrinkTotal()
    {
        var track  = MakeVideoTrack();
        var fromId = Guid.NewGuid();
        var toId   = Guid.NewGuid();
        track.Items.Add(new VideoClip { Id = fromId, TimelinePosition = 0, Duration = 5 });
        track.Items.Add(new VideoClip { Id = toId,   TimelinePosition = 5, Duration = 3 }); // ends at 8

        // Transition centred on the t=5 boundary, 1.0s duration → spans 4.5..5.5.
        track.Items.Add(new Transition
        {
            FromClipId       = fromId,
            ToClipId         = toId,
            TimelinePosition = 4.5,
            Duration         = 1.0,
        });

        Assert.Equal(8, track.TotalDuration); // dominated by the second clip's real end, not 5.5
    }

    [Fact]
    public void VideoClips_ReturnsOnlyVideoClipItems()
    {
        var track = MakeVideoTrack();
        track.Items.Add(new VideoClip { Name = "v1" });
        track.Items.Add(new TextOverlay { Name = "t1" });

        Assert.Single(track.VideoClips);
        Assert.Equal("v1", track.VideoClips.First().Name);
    }

    [Fact]
    public void TextOverlays_ReturnsOnlyTextOverlayItems()
    {
        var track = MakeVideoTrack();
        track.Items.Add(new VideoClip { Name = "v1" });
        track.Items.Add(new TextOverlay { Name = "t1", TimelinePosition = 1, Duration = 2 });

        Assert.Single(track.TextOverlays);
        Assert.Equal("t1", track.TextOverlays.First().Name);
    }
}
