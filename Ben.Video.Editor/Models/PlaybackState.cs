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

    /// <summary>Current playhead position in seconds.</summary>
    public double CurrentTime { get; init; }

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
