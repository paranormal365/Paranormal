using Ben.Video.Editor.Models;

namespace Ben.Video.Editor.Services;

/// <summary>What one entry in the assembled output is.</summary>
public enum ExportSegmentKind
{
    /// <summary>A rendered clip.</summary>
    Clip,

    /// <summary>Black and silence, standing in for a gap on the timeline.</summary>
    Filler,
}

/// <summary>One piece of the output, in the order it plays.</summary>
/// <param name="Kind">Whether this is a rendered clip or a stretch of nothing.</param>
/// <param name="Segment">The rendered file, for a clip. Empty for a gap.</param>
/// <param name="ClipId">The clip this came from, so a transition can be matched to a junction.</param>
/// <param name="Start">Where it begins on the timeline, in seconds.</param>
/// <param name="Duration">How long it lasts, in seconds.</param>
public readonly record struct ExportSegment(
    ExportSegmentKind Kind,
    string Segment,
    Guid ClipId,
    double Start,
    double Duration);

/// <summary>
/// The order the output is assembled in, and where the gaps are.
/// </summary>
/// <remarks>
/// <para>Two things went wrong without this. Segments were concatenated back to back, so a gap
/// between two clips simply vanished from the render — while the audio, the overlays and the
/// chapter marks all kept their timeline positions, so everything after the gap played against the
/// wrong picture (2026-09-05 audit, export-2).</para>
///
/// <para>And transitions were matched to junctions by index: <c>transitions[i]</c> was applied to
/// the junction between segment i and i+1, whatever pair those actually were. One transition
/// anywhere on the track therefore gave <i>every</i> junction a crossfade — the ones nobody asked
/// for defaulting to a one-second fade (transitions-2).</para>
///
/// <para>Pure, so the arithmetic that decides what the render looks like can be checked without
/// rendering anything.</para>
/// </remarks>
public static class ExportSegmentPlanner
{
    /// <summary>Gaps shorter than this are rounding, not silence.</summary>
    public const double MinimumGapSeconds = 0.02;

    /// <summary>
    /// Lays out the rendered clips in timeline order, inserting a filler wherever the timeline has
    /// nothing.
    /// </summary>
    /// <param name="placed">
    /// Each rendered clip: the file, the clip's id, where it starts and how long it lasts.
    /// </param>
    public static IReadOnlyList<ExportSegment> Plan(
        IEnumerable<(string Segment, Guid ClipId, double Start, double Duration)> placed)
    {
        ArgumentNullException.ThrowIfNull(placed);

        var ordered = placed.OrderBy(p => p.Start).ToList();
        var plan    = new List<ExportSegment>(ordered.Count);
        var cursor  = 0.0;

        foreach (var (segment, clipId, start, duration) in ordered)
        {
            if (duration <= 0) continue;

            // A leading gap counts too: a project whose first clip starts at three seconds opens
            // with three seconds of black, exactly as the timeline shows.
            var gap = start - cursor;
            if (gap >= MinimumGapSeconds)
            {
                plan.Add(new ExportSegment(ExportSegmentKind.Filler, string.Empty, Guid.Empty, cursor, gap));
                cursor += gap;
            }

            plan.Add(new ExportSegment(ExportSegmentKind.Clip, segment, clipId, Math.Max(start, cursor), duration));
            cursor = Math.Max(start, cursor) + duration;
        }

        return plan;
    }

    /// <summary>
    /// Which transition belongs to each junction, by the clips it names.
    /// </summary>
    /// <returns>
    /// One entry per junction — that is, one fewer than the number of segments. Null where the two
    /// clips meeting at that junction have no transition between them.
    /// </returns>
    /// <remarks>
    /// Matching by the pair of clip ids rather than by position is the whole point: a transition
    /// belongs to two specific clips, and any other junction is a cut.
    /// </remarks>
    public static IReadOnlyList<Transition?> MatchTransitions(
        IReadOnlyList<ExportSegment> plan, IEnumerable<Transition> transitions)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(transitions);

        var byPair = transitions
            .GroupBy(t => (t.FromClipId, t.ToClipId))
            .ToDictionary(g => g.Key, g => g.First());

        var junctions = new List<Transition?>();

        for (var i = 0; i < plan.Count - 1; i++)
        {
            var from = plan[i];
            var to   = plan[i + 1];

            // A gap on either side is a cut: there is nothing to blend into or out of.
            if (from.Kind != ExportSegmentKind.Clip || to.Kind != ExportSegmentKind.Clip)
            {
                junctions.Add(null);
                continue;
            }

            junctions.Add(byPair.TryGetValue((from.ClipId, to.ClipId), out var match) ? match : null);
        }

        return junctions;
    }

    /// <summary>
    /// How long the assembled output will be, given the plan and the transitions applied to it.
    /// </summary>
    /// <remarks>
    /// Each crossfade makes the render shorter by its own length, because the two clips play at
    /// once for that stretch. This is the number the timeline should agree with.
    /// </remarks>
    public static double TotalDuration(
        IReadOnlyList<ExportSegment> plan, IReadOnlyList<Transition?> junctions)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(junctions);

        var total = plan.Sum(p => p.Duration);
        return total - junctions.Where(t => t is not null).Sum(t => t!.Duration);
    }
}
