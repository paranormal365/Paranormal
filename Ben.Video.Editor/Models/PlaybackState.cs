namespace Ben.Video.Editor.Models;

/// <summary>
/// The current preview context — whether a single clip or the full assembled timeline is loaded.
/// </summary>
public enum PlaybackMode
{
    /// <summary>No preview loaded.</summary>
    None,
    /// <summary>A single clip from the browser is previewed.</summary>
    Clip,
    /// <summary>The full assembled timeline composition is previewed.</summary>
    Timeline
}

/// <summary>
/// Immutable snapshot of playback state shared across editor components via
/// <see cref="Services.PlaybackService"/>.
/// </summary>
public sealed record PlaybackState
{
    /// <summary>What is currently loaded in the preview panel.</summary>
    public PlaybackMode Mode { get; init; } = PlaybackMode.None;

    /// <summary>
    /// Position within whatever is loaded in the preview, in seconds.
    /// </summary>
    /// <remarks>
    /// This is <b>media time</b>: what the underlying player reports. In
    /// <see cref="PlaybackMode.Timeline"/> that is also the timeline's own clock, but in
    /// <see cref="PlaybackMode.Clip"/> it is time within that one clip, counted from its own start.
    /// Anything asking "where is the playhead on the timeline" wants
    /// <see cref="TimelineTime"/> instead.
    /// </remarks>
    public double CurrentTime { get; init; }

    /// <summary>
    /// Where the playhead is on the timeline, in seconds from the start of the project.
    /// </summary>
    /// <remarks>
    /// <para>The two clocks used to be one field, and everything that means a position on the
    /// timeline — split, markers, placing a title, the ruler — read it. Previewing a single clip
    /// set it to that clip's own time starting at zero, so after clicking a clip that begins ten
    /// seconds in, "split at the playhead" cut ten seconds early and a marker landed in the wrong
    /// place (2026-09-05 audit, F6 and timeline-1).</para>
    ///
    /// <para>While a single clip is being previewed this holds still at the last timeline position
    /// rather than following that clip, because the timeline's playhead has not moved — nothing on
    /// the timeline is playing.</para>
    /// </remarks>
    public double TimelineTime { get; init; }

    /// <summary>Total duration of the loaded media in seconds.</summary>
    public double Duration { get; init; }

    /// <summary>Whether the video is currently playing.</summary>
    public bool IsPlaying { get; init; }

    /// <summary>
    /// Playhead position as a fraction of total duration (0–1).
    /// Returns 0 when Duration is zero to avoid division by zero.
    /// </summary>
    public double Progress => Duration > 0 ? CurrentTime / Duration : 0;

    /// <summary>Display label shown in the preview panel badge.</summary>
    public string ModeLabel => Mode switch
    {
        PlaybackMode.Clip     => "Clip Preview",
        PlaybackMode.Timeline => "Timeline Preview",
        _                     => string.Empty
    };
}
