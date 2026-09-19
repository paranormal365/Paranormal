using Ben.Video.Editor.Models;

namespace Ben.Video.Editor.Services;

/// <summary>
/// What it means for a track to be laid out sensibly: its clips run one after another, in order,
/// without overlapping.
/// </summary>
/// <remarks>
/// <para>Nothing enforced this. A drag wrote whatever position the pointer ended on, so a clip
/// could be dropped on top of another and stay there; a second local import landed at zero
/// regardless of what was already at zero. It was invisible too, because the lane drew its clips
/// end to end and clamped any negative gap to nothing — so the picture showed them neatly adjacent
/// while the model had them stacked, and the track's own length, the ruler and the export dialog
/// each reported something different (2026-09-05 audit, F5 and timeline-10).</para>
///
/// <para>Overlays are exempt on purpose. A callout, title or piece of clip art is drawn *over* the
/// picture and is meant to overlap whatever is beneath it; only the sequential items — video,
/// audio and image clips — hold a place in time that nothing else can hold at once. Transitions
/// are exempt for the same reason: one is a property of the junction between two clips, not
/// another thing sitting in the lane.</para>
///
/// <para>Pure and static so the rules can be tested without a store, a browser or a render.</para>
/// </remarks>
public static class TrackLayout
{
    /// <summary>
    /// Two positions within this many seconds of each other are the same position.
    /// </summary>
    /// <remarks>
    /// A drag produces doubles from pixel arithmetic, so "touching" almost never comes out exactly
    /// equal. A millisecond is far below anything a person can see at any zoom and far above the
    /// error that arithmetic introduces.
    /// </remarks>
    public const double Tolerance = 0.001;

    /// <summary>The items that occupy time on a track, in the order they play.</summary>
    public static IReadOnlyList<TrackItem> SequentialItems(TimelineTrack track)
    {
        ArgumentNullException.ThrowIfNull(track);

        return track.Items
            .Where(IsSequential)
            .OrderBy(i => i.TimelinePosition)
            .ToList();
    }

    /// <summary>Whether this kind of item holds a place in time that nothing else may hold.</summary>
    public static bool IsSequential(TrackItem item) =>
        item is VideoClip or AudioClip or ImageClip;

    /// <summary>
    /// How much two clips are allowed to overlap because a transition joins them.
    /// </summary>
    /// <remarks>
    /// A crossfade is not decoration: the two clips genuinely play at once for its duration, and
    /// the render is shorter than the sum of its parts by exactly that much. So the pair is allowed
    /// to overlap by the transition's length and no more — which is also how the timeline can show
    /// the truth, rather than claiming a length the export does not produce (2026-09-05 audit,
    /// transitions-3).
    /// </remarks>
    public static double AllowedOverlap(TimelineTrack track, TrackItem first, TrackItem second)
    {
        ArgumentNullException.ThrowIfNull(track);

        return track.Items.OfType<Transition>()
            .Where(t => (t.FromClipId == first.Id && t.ToClipId == second.Id)
                     || (t.FromClipId == second.Id && t.ToClipId == first.Id))
            .Select(t => t.Duration)
            .DefaultIfEmpty(0)
            .Max();
    }

    /// <summary>
    /// Whether something of <paramref name="duration"/> seconds placed at
    /// <paramref name="position"/> would land on top of anything already on the track.
    /// </summary>
    /// <param name="excludeItemId">
    /// The item being moved, so it does not count as overlapping itself.
    /// </param>
    public static bool Overlaps(
        TimelineTrack track, double position, double duration, Guid? excludeItemId = null)
    {
        ArgumentNullException.ThrowIfNull(track);

        return FirstOverlapping(track, position, duration, excludeItemId) is not null;
    }

    /// <summary>The first item the given span would land on, if any.</summary>
    public static TrackItem? FirstOverlapping(
        TimelineTrack track, double position, double duration, Guid? excludeItemId = null)
    {
        ArgumentNullException.ThrowIfNull(track);

        var end = position + duration;

        return SequentialItems(track).FirstOrDefault(other =>
        {
            if (other.Id == excludeItemId) return false;

            // A transition joining the two buys exactly its own duration of overlap.
            var allowed = excludeItemId is { } movingId
                ? track.Items.OfType<Transition>()
                       .Where(t => (t.FromClipId == movingId && t.ToClipId == other.Id)
                                || (t.FromClipId == other.Id && t.ToClipId == movingId))
                       .Select(t => t.Duration)
                       .DefaultIfEmpty(0).Max()
                : 0;

            return other.TimelinePosition < end - allowed - Tolerance
                && other.TimelinePosition + other.EffectiveLength > position + allowed + Tolerance;
        });
    }

    /// <summary>
    /// Where a track ends: the far edge of its last sequential item.
    /// </summary>
    public static double EndOf(TimelineTrack track)
    {
        ArgumentNullException.ThrowIfNull(track);

        var items = SequentialItems(track);
        return items.Count == 0 ? 0 : items.Max(i => i.TimelinePosition + i.EffectiveLength);
    }

    /// <summary>
    /// Checks the invariant and describes the first breach, or returns null when the track is fine.
    /// </summary>
    /// <remarks>
    /// Returns a message rather than throwing so the caller decides how loud to be — the store
    /// asserts with it in debug builds, and the tests read it.
    /// </remarks>
    public static string? Validate(TimelineTrack track)
    {
        ArgumentNullException.ThrowIfNull(track);

        var items = SequentialItems(track);

        for (var i = 0; i < items.Count; i++)
        {
            var item = items[i];

            if (item.TimelinePosition < -Tolerance)
                return $"'{item.Name}' starts at {item.TimelinePosition:0.###}s, before the beginning.";

            // A clip with no length yet is normal: it is added the moment the file is picked and
            // gains its duration when the probe comes back. Nothing to complain about, and it
            // cannot overlap anything either.

            if (i == 0) continue;

            var previous = items[i - 1];
            var previousEnd = previous.TimelinePosition + previous.EffectiveLength;
            var allowed = AllowedOverlap(track, previous, item);

            if (item.TimelinePosition < previousEnd - allowed - Tolerance)
                return $"'{item.Name}' starts at {item.TimelinePosition:0.###}s, "
                     + $"inside '{previous.Name}' which runs to {previousEnd:0.###}s"
                     + (allowed > 0 ? $" (a transition allows {allowed:0.###}s of it)." : ".");
        }

        return null;
    }
}
