using Ben.Video.Editor.Models;
using Ben.Video.Editor.Services;

namespace Ben.Video.Tests.Services;

/// <summary>
/// What the export is assembled from: the clips, the gaps between them, and which junctions blend.
/// </summary>
/// <remarks>
/// Both halves were wrong and both were invisible. Gaps were closed on export while the audio, the
/// overlays and the chapter marks kept their timeline positions, so everything after the first gap
/// played against the wrong picture. And transitions were matched to junctions by position, so one
/// transition anywhere on the track gave every other junction a fade nobody asked for (2026-09-05
/// audit, export-2 and transitions-2).
/// </remarks>
public sealed class ExportSegmentPlannerTests
{
    private static (string, Guid, double, double) At(string name, Guid id, double start, double duration)
        => (name, id, start, duration);

    // ── Plan ──────────────────────────────────────────────────────────────────

    [Fact]
    public void Clips_that_touch_produce_no_filler()
    {
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();

        var plan = ExportSegmentPlanner.Plan([At("a.mp4", a, 0, 5), At("b.mp4", b, 5, 3)]);

        Assert.Equal(2, plan.Count);
        Assert.All(plan, p => Assert.Equal(ExportSegmentKind.Clip, p.Kind));
    }

    [Fact]
    public void A_gap_between_two_clips_becomes_a_filler_of_exactly_that_length()
    {
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();

        var plan = ExportSegmentPlanner.Plan([At("a.mp4", a, 0, 5), At("b.mp4", b, 8, 3)]);

        Assert.Equal(3, plan.Count);
        Assert.Equal(ExportSegmentKind.Filler, plan[1].Kind);
        Assert.Equal(5, plan[1].Start);
        Assert.Equal(3, plan[1].Duration);
        Assert.Equal("b.mp4", plan[2].Segment);
        Assert.Equal(8, plan[2].Start);
    }

    /// <summary>
    /// A project whose first clip starts three seconds in opens with three seconds of black,
    /// exactly as the timeline shows.
    /// </summary>
    [Fact]
    public void A_leading_gap_counts_too()
    {
        var plan = ExportSegmentPlanner.Plan([At("a.mp4", Guid.NewGuid(), 3, 5)]);

        Assert.Equal(2, plan.Count);
        Assert.Equal(ExportSegmentKind.Filler, plan[0].Kind);
        Assert.Equal(0, plan[0].Start);
        Assert.Equal(3, plan[0].Duration);
    }

    /// <summary>
    /// Positions come from pixel arithmetic, so two clips meant to touch rarely land on the same
    /// double. A few milliseconds of black between every pair would be worse than the rounding.
    /// </summary>
    [Fact]
    public void A_rounding_error_is_not_a_gap()
    {
        var plan = ExportSegmentPlanner.Plan(
            [At("a.mp4", Guid.NewGuid(), 0, 5), At("b.mp4", Guid.NewGuid(), 5.004, 3)]);

        Assert.Equal(2, plan.Count);
    }

    [Fact]
    public void The_plan_is_in_timeline_order_whatever_order_it_was_given()
    {
        var plan = ExportSegmentPlanner.Plan(
            [At("later.mp4", Guid.NewGuid(), 10, 2), At("first.mp4", Guid.NewGuid(), 0, 5)]);

        Assert.Equal("first.mp4", plan[0].Segment);
        Assert.Equal("later.mp4", plan[^1].Segment);
    }

    /// <summary>
    /// A clip whose probe has not returned yet has no length, and there is nothing to render.
    /// </summary>
    [Fact]
    public void A_clip_with_no_duration_is_left_out()
    {
        var plan = ExportSegmentPlanner.Plan(
            [At("empty.mp4", Guid.NewGuid(), 0, 0), At("a.mp4", Guid.NewGuid(), 0, 5)]);

        Assert.Single(plan);
        Assert.Equal("a.mp4", plan[0].Segment);
    }

    [Fact]
    public void Nothing_on_the_timeline_plans_nothing()
    {
        Assert.Empty(ExportSegmentPlanner.Plan([]));
    }

    // ── MatchTransitions ──────────────────────────────────────────────────────

    [Fact]
    public void A_transition_lands_on_the_junction_between_the_two_clips_it_names()
    {
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();
        var c = Guid.NewGuid();

        var plan = ExportSegmentPlanner.Plan(
            [At("a.mp4", a, 0, 5), At("b.mp4", b, 5, 5), At("c.mp4", c, 10, 5)]);

        var junctions = ExportSegmentPlanner.MatchTransitions(plan,
            [new Transition { FromClipId = b, ToClipId = c, Duration = 0.75 }]);

        Assert.Equal(2, junctions.Count);
        Assert.Null(junctions[0]);
        Assert.Equal(0.75, junctions[1]!.Duration);
    }

    /// <summary>
    /// The defect this replaced: one transition used to fade every junction on the track.
    /// </summary>
    [Fact]
    public void Junctions_the_transition_does_not_name_stay_cuts()
    {
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();
        var c = Guid.NewGuid();
        var d = Guid.NewGuid();

        var plan = ExportSegmentPlanner.Plan(
            [At("a.mp4", a, 0, 5), At("b.mp4", b, 5, 5), At("c.mp4", c, 10, 5), At("d.mp4", d, 15, 5)]);

        var junctions = ExportSegmentPlanner.MatchTransitions(plan,
            [new Transition { FromClipId = a, ToClipId = b, Duration = 1.0 }]);

        Assert.Equal(3, junctions.Count);
        Assert.NotNull(junctions[0]);
        Assert.Null(junctions[1]);
        Assert.Null(junctions[2]);
    }

    /// <summary>
    /// There is nothing to blend into or out of across a stretch of black.
    /// </summary>
    [Fact]
    public void A_transition_across_a_gap_is_ignored()
    {
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();

        var plan = ExportSegmentPlanner.Plan([At("a.mp4", a, 0, 5), At("b.mp4", b, 9, 5)]);

        var junctions = ExportSegmentPlanner.MatchTransitions(plan,
            [new Transition { FromClipId = a, ToClipId = b, Duration = 1.0 }]);

        Assert.Equal(2, junctions.Count);
        Assert.All(junctions, Assert.Null);
    }

    [Fact]
    public void A_transition_naming_clips_that_are_not_on_the_timeline_matches_nothing()
    {
        var plan = ExportSegmentPlanner.Plan(
            [At("a.mp4", Guid.NewGuid(), 0, 5), At("b.mp4", Guid.NewGuid(), 5, 5)]);

        var junctions = ExportSegmentPlanner.MatchTransitions(plan,
            [new Transition { FromClipId = Guid.NewGuid(), ToClipId = Guid.NewGuid(), Duration = 1.0 }]);

        Assert.Single(junctions);
        Assert.Null(junctions[0]);
    }

    [Fact]
    public void One_segment_has_no_junctions()
    {
        var plan = ExportSegmentPlanner.Plan([At("a.mp4", Guid.NewGuid(), 0, 5)]);

        Assert.Empty(ExportSegmentPlanner.MatchTransitions(plan, []));
    }

    // ── TotalDuration ─────────────────────────────────────────────────────────

    [Fact]
    public void The_total_includes_the_gaps()
    {
        var plan = ExportSegmentPlanner.Plan(
            [At("a.mp4", Guid.NewGuid(), 0, 5), At("b.mp4", Guid.NewGuid(), 8, 3)]);

        Assert.Equal(11, ExportSegmentPlanner.TotalDuration(plan, [null, null]));
    }

    /// <summary>
    /// A crossfade plays both clips at once, so the render is shorter by its length. This is the
    /// number the timeline has to agree with, or everything after the blend drifts.
    /// </summary>
    [Fact]
    public void Each_transition_makes_the_render_shorter_by_its_own_length()
    {
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();

        var plan = ExportSegmentPlanner.Plan([At("a.mp4", a, 0, 5), At("b.mp4", b, 5, 5)]);
        var junctions = ExportSegmentPlanner.MatchTransitions(plan,
            [new Transition { FromClipId = a, ToClipId = b, Duration = 1.5 }]);

        Assert.Equal(8.5, ExportSegmentPlanner.TotalDuration(plan, junctions));
    }
}
