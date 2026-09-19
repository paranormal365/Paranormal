using Ben.Video.Editor.Models;
using Ben.Video.Editor.Services;

namespace Ben.Video.Tests.Services;

/// <summary>
/// The two clocks: where the playhead is on the timeline, and where the player is inside whatever
/// it happens to be showing.
/// </summary>
/// <remarks>
/// <para>They were one field. Selecting a clip on the timeline loaded that clip's raw source into
/// the preview, which reset the clock to zero and started counting from the clip's own start —
/// and everything that means a position on the timeline read that same field. Split cut early by
/// exactly the clip's start position, a marker landed somewhere unrelated, and a new title was
/// placed at a fraction of the wrong thing. For the first clip on a track the two clocks agree,
/// which is why it looked correct (2026-09-05 audit, F6, timeline-1, timeline-3, titles-8).</para>
/// </remarks>
public sealed class PlaybackClockTests
{
    [Fact]
    public void Timeline_playback_moves_both_clocks_together()
    {
        var playback = new PlaybackService();
        playback.NotifyLoaded(PlaybackMode.Timeline, 30);

        playback.NotifyTimeUpdate(12.5);

        Assert.Equal(12.5, playback.State.CurrentTime);
        Assert.Equal(12.5, playback.State.TimelineTime);
    }

    /// <summary>
    /// Previewing one clip must not drag the timeline's playhead along with it.
    /// </summary>
    [Fact]
    public void Previewing_a_single_clip_leaves_the_timeline_playhead_where_it_was()
    {
        var playback = new PlaybackService();
        playback.NotifyLoaded(PlaybackMode.Timeline, 30);
        playback.NotifyTimeUpdate(18);

        playback.NotifyLoaded(PlaybackMode.Clip, 4.8);
        playback.NotifyTimeUpdate(2.0);

        Assert.Equal(2.0, playback.State.CurrentTime);      // inside that clip
        Assert.Equal(18, playback.State.TimelineTime);      // the timeline has not moved
    }

    [Fact]
    public void Loading_the_timeline_sends_its_playhead_to_the_start()
    {
        var playback = new PlaybackService();
        playback.NotifyLoaded(PlaybackMode.Timeline, 30);
        playback.NotifyTimeUpdate(18);

        playback.NotifyLoaded(PlaybackMode.Timeline, 42);

        Assert.Equal(0, playback.State.TimelineTime);
    }

    [Fact]
    public void Seeking_the_timeline_moves_the_timeline_playhead()
    {
        var playback = new PlaybackService();
        playback.NotifyLoaded(PlaybackMode.Timeline, 30);

        playback.RequestSeek(9);

        Assert.Equal(9, playback.State.TimelineTime);
    }

    [Fact]
    public void Seeking_inside_a_clip_preview_does_not_move_the_timeline_playhead()
    {
        var playback = new PlaybackService();
        playback.NotifyLoaded(PlaybackMode.Timeline, 30);
        playback.NotifyTimeUpdate(18);
        playback.NotifyLoaded(PlaybackMode.Clip, 4.8);

        playback.RequestSeek(3);

        Assert.Equal(3, playback.State.CurrentTime);
        Assert.Equal(18, playback.State.TimelineTime);
    }

    /// <summary>Selecting a clip on the timeline: the playhead goes to its start.</summary>
    [Fact]
    public void SetTimelineTime_moves_only_the_timeline_playhead()
    {
        var playback = new PlaybackService();
        playback.NotifyLoaded(PlaybackMode.Clip, 4.8);
        playback.NotifyTimeUpdate(1.5);

        playback.SetTimelineTime(10);

        Assert.Equal(10, playback.State.TimelineTime);
        Assert.Equal(1.5, playback.State.CurrentTime);
    }

    /// <summary>
    /// Negative times clamp to the start, and a value that changes nothing raises nothing — a
    /// notification per pointermove during a drag is a re-render of the whole editor.
    /// </summary>
    [Fact]
    public void SetTimelineTime_clamps_to_the_start_and_stays_quiet_when_nothing_moves()
    {
        var playback = new PlaybackService();
        playback.SetTimelineTime(12);

        var raised = 0;
        playback.OnStateChanged += () => raised++;

        playback.SetTimelineTime(-4);
        Assert.Equal(0, playback.State.TimelineTime);
        Assert.Equal(1, raised);

        playback.SetTimelineTime(0);
        Assert.Equal(1, raised);
    }

    [Fact]
    public void Clearing_the_preview_resets_both_clocks()
    {
        var playback = new PlaybackService();
        playback.NotifyLoaded(PlaybackMode.Timeline, 30);
        playback.NotifyTimeUpdate(11);

        playback.NotifyCleared();

        Assert.Equal(0, playback.State.CurrentTime);
        Assert.Equal(0, playback.State.TimelineTime);
    }
}
