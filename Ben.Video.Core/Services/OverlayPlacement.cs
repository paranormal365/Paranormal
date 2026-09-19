namespace Ben.Video.Core.Services;

/// <summary>
/// Where a newly added overlay goes and how long it lasts.
/// </summary>
/// <param name="Position">Timeline position in seconds.</param>
/// <param name="Duration">Length in seconds.</param>
/// <remarks>
/// <para>Callouts, titles and clip art are all added the same way and were all placed differently:
/// the assets gallery put them after the end of everything, so a second one landed five seconds
/// beyond the first (callouts-7); "+ Text" was placed by the playhead's progress fraction times
/// the timeline's length, which is two clocks multiplied together (titles-8); and the timeline's
/// Callout button read the media clock, which in clip preview counts from the selected clip's own
/// start rather than from the start of the timeline.</para>
///
/// <para>Three call sites, three answers to one question — so the question moved here
/// (2026-09-05 audit, phase 11).</para>
/// </remarks>
public readonly record struct OverlayPlacement(double Position, double Duration)
{
    /// <summary>How long a new overlay lasts when the timeline is long enough to hold it.</summary>
    public const double PreferredDurationSeconds = 5.0;

    /// <summary>The shortest a new overlay is ever created.</summary>
    /// <remarks>
    /// An overlay is trimmed by its edges, and an edge under a second is hard to grab, so a
    /// nearly-empty timeline still gets something a person can take hold of.
    /// </remarks>
    public const double MinimumDurationSeconds = 1.0;

    /// <summary>
    /// Places an overlay at the playhead.
    /// </summary>
    /// <param name="playheadTimelineTime">
    /// The playhead as a <b>timeline</b> position. Never the media clock: in clip preview that
    /// counts from the selected clip's start, and an overlay placed by it lands earlier than the
    /// playhead by exactly the clip's start — which looks right only for a clip that begins at zero.
    /// </param>
    /// <param name="timelineTotalDuration">The length of everything currently on the timeline.</param>
    /// <remarks>
    /// The duration is capped at the timeline's own length because an overlay hanging past the end
    /// of the video it annotates is never what somebody meant, and the export drops what falls
    /// beyond the last frame anyway.
    /// </remarks>
    public static OverlayPlacement AtPlayhead(double playheadTimelineTime, double timelineTotalDuration)
    {
        var position = double.IsFinite(playheadTimelineTime) ? Math.Max(0, playheadTimelineTime) : 0;

        var duration = double.IsFinite(timelineTotalDuration)
            ? Math.Min(PreferredDurationSeconds, Math.Max(MinimumDurationSeconds, timelineTotalDuration))
            : PreferredDurationSeconds;

        return new(position, duration);
    }
}
