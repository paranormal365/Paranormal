using Ben.Video.Core.Services;
using Ben.Video.Editor.Models;
using Ben.Video.Editor.Services;
using Microsoft.Extensions.Options;

namespace Ben.Video.Tests.Services;

/// <summary>
/// A clip is as long as it is. The timeline and the render must not disagree about how long.
/// </summary>
/// <remarks>
/// <para><c>VideoClip.EffectiveDuration</c> is trim ÷ speed, documented as "the real wall-clock
/// length the clip occupies", and the export feeds exactly that to the segment planner.
/// <c>VideoClip.EffectiveLength</c> — what the timeline draws every chip from, and what overlap
/// detection, ripple, transition junctions and the project's total duration are all built on —
/// ignored speed entirely.</para>
///
/// <para>So a clip set to 2× kept its full width on the timeline while the render made it half as
/// long, and the planner filled the difference with black. Two seconds of nothing appeared in the
/// middle of an exhibit, and the timeline showed no gap at all: the one arrangement nobody could
/// see was the broken one (2026-09-18 audit).</para>
/// </remarks>
public sealed class SpeedIsTheSameLengthEverywhereTests
{
    private static (ClipStore Store, TimelineTrack Track, VideoClip A, VideoClip B) TwoClips()
    {
        var store = new ClipStore(Options.Create(new VideoEditorOptions { MultiTrack = true, Transitions = true }));
        var track = store.Tracks[0];
        var a = new VideoClip { Name = "a", Duration = 4, MemFsName = "a.mp4" };
        var b = new VideoClip { Name = "b", Duration = 4, MemFsName = "b.mp4" };
        store.AddClipToTrack(track.Id, a);
        store.AddClipToTrack(track.Id, b);
        return (store, track, a, b);
    }

    /// <summary>The plan the export builds, from the very numbers ExportService hands it.</summary>
    private static IReadOnlyList<ExportSegment> PlanFor(TimelineTrack track) =>
        ExportSegmentPlanner.Plan(track.Items.OfType<VideoClip>()
            .Select(c => ($"{c.Name}.seg", c.Id, c.TimelinePosition,
                          c.EffectiveDuration > 0 ? c.EffectiveDuration : c.Duration)));

    [Fact]
    public void The_length_the_timeline_draws_is_the_length_the_export_renders()
    {
        var (store, _, a, _) = TwoClips();

        store.UpdateClipSpeed(a.Id, 2.0);

        Assert.Equal(a.EffectiveDuration, a.EffectiveLength, 3);
    }

    [Fact]
    public void Black_in_the_render_is_a_gap_you_can_see_on_the_timeline()
    {
        // Speeding a clip up leaves a hole where the rest of it used to be. The render fills that
        // hole with black, and it is not this method's business to decide whether the user wanted
        // the following clips pulled back — only that the hole is drawn where it is rendered.
        var (store, track, a, b) = TwoClips();

        store.UpdateClipSpeed(a.Id, 2.0);

        var filler = PlanFor(track).Where(seg => seg.Kind == ExportSegmentKind.Filler).ToList();
        var black  = filler.Sum(f => f.Duration);
        var gapOnTimeline = b.TimelinePosition - (a.TimelinePosition + a.EffectiveLength);

        Assert.Equal(black, Math.Max(0, gapOnTimeline), 2);
    }

    [Fact]
    public void Slowing_a_clip_down_does_not_make_it_overlap_the_next_one()
    {
        var (store, track, a, b) = TwoClips();

        store.UpdateClipSpeed(a.Id, 0.5);

        // Measured against the plan, not against EffectiveLength — asking the number that is wrong
        // whether it is wrong gets you a pass.
        var plan  = PlanFor(track);
        var first = plan.Single(seg => seg.ClipId == a.Id);
        var next  = plan.Single(seg => seg.ClipId == b.Id);

        Assert.Equal(b.TimelinePosition, next.Start, 2);
        Assert.True(first.Start + first.Duration <= next.Start + 0.05,
            $"at half speed a runs to {first.Start + first.Duration:F2}s in the render while the " +
            $"timeline has b starting at {b.TimelinePosition:F2}s — the render pushes b later and " +
            "every overlay after it lands against the wrong picture.");
    }

    [Fact]
    public void A_clip_at_normal_speed_is_unchanged()
    {
        var (store, track, a, b) = TwoClips();

        store.UpdateClipSpeed(a.Id, 1.0);

        Assert.Equal(4, a.EffectiveLength, 3);
        Assert.Equal(4, b.TimelinePosition, 3);
        Assert.DoesNotContain(PlanFor(track), seg => seg.Kind == ExportSegmentKind.Filler);
    }
}
