using Ben.Video.Editor.Models;

namespace Ben.Video.Editor.Services;

/// <summary>
/// Pure static helper that implements magnetic snapping for timeline drag operations.
/// Collects snap target positions (markers and clip edges) and finds the nearest
/// target within a configurable threshold.
/// Isolated from Blazor/JSInterop so it can be unit-tested without a browser.
/// </summary>
public static class SnapEngine
{
    /// <summary>
    /// Collects all candidate snap-target positions in seconds from the current
    /// timeline state: every marker's time position plus every clip's start and
    /// end position across all tracks.
    /// </summary>
    /// <param name="tracks">All tracks currently on the timeline.</param>
    /// <param name="markers">All named cue-point markers on the timeline ruler.</param>
    /// <param name="excludeItemId">
    /// When repositioning a clip that's already on the timeline, its own start/end are otherwise
    /// always the nearest targets to wherever it currently sits, which defeats snapping to
    /// anything else. Pass that clip's id here to omit its own edges from the result; omit (or
    /// pass <c>null</c>) when collecting targets for a brand-new clip that isn't on the timeline yet.
    /// </param>
    /// <returns>
    /// A deduplicated, sorted list of timeline positions (seconds) that a drag
    /// position can snap to.
    /// </returns>
    public static IReadOnlyList<double> CollectSnapTargets(
        IEnumerable<TimelineTrack>  tracks,
        IEnumerable<TimelineMarker> markers,
        Guid?                       excludeItemId = null)
    {
        var targets = new HashSet<double>();

        // Marker positions
        foreach (var m in markers)
            targets.Add(m.TimeSeconds);

        // Clip start and end edges across all tracks
        foreach (var track in tracks)
        {
            foreach (var clip in track.VideoClips)
            {
                if (clip.Id == excludeItemId) continue;
                targets.Add(clip.TimelinePosition);
                targets.Add(clip.TimelinePosition + clip.EffectiveDuration);
            }

            foreach (var clip in track.AudioClips)
            {
                if (clip.Id == excludeItemId) continue;
                targets.Add(clip.TimelinePosition);
                targets.Add(clip.TimelinePosition + clip.Duration);
            }
        }

        return [.. targets.OrderBy(t => t)];
    }

    /// <summary>
    /// Snaps <paramref name="position"/> to the nearest value in
    /// <paramref name="targets"/> if it is within <paramref name="thresholdSeconds"/>.
    /// Returns <paramref name="position"/> unchanged when no target is within range
    /// or when <paramref name="targets"/> is empty.
    /// </summary>
    /// <param name="position">The raw drag position in seconds.</param>
    /// <param name="targets">Candidate snap positions (from <see cref="CollectSnapTargets"/>).</param>
    /// <param name="thresholdSeconds">
    /// Maximum distance in seconds to snap. Positions farther than this are not snapped.
    /// </param>
    /// <returns>The snapped position, or <paramref name="position"/> if no snap applies.</returns>
    public static double Snap(
        double               position,
        IReadOnlyList<double> targets,
        double               thresholdSeconds)
    {
        if (targets.Count == 0 || thresholdSeconds <= 0)
            return position;

        var best     = double.MaxValue;
        var bestDist = double.MaxValue;

        foreach (var t in targets)
        {
            var dist = Math.Abs(t - position);
            if (dist < bestDist)
            {
                bestDist = dist;
                best     = t;
            }
        }

        return bestDist <= thresholdSeconds ? best : position;
    }

    /// <summary>
    /// Returns the snap target that <paramref name="position"/> is currently
    /// snapped to, or <c>null</c> if no snap applies.
    /// Used to determine where to draw the snap guide line.
    /// </summary>
    public static double? ActiveSnapTarget(
        double               position,
        IReadOnlyList<double> targets,
        double               thresholdSeconds)
    {
        if (targets.Count == 0 || thresholdSeconds <= 0)
            return null;

        var best     = double.MaxValue;
        var bestDist = double.MaxValue;

        foreach (var t in targets)
        {
            var dist = Math.Abs(t - position);
            if (dist < bestDist)
            {
                bestDist = dist;
                best     = t;
            }
        }

        return bestDist <= thresholdSeconds ? best : null;
    }
}
