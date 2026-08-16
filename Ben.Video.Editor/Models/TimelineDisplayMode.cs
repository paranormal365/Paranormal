namespace Ben.Video.Editor.Models;

/// <summary>
/// Controls how tick labels are displayed on the timeline ruler.
/// </summary>
public enum TimelineDisplayMode
{
    /// <summary>Show labels as HH:MM:SS timecode.</summary>
    Time,

    /// <summary>Show labels as absolute frame numbers (assumes 30 fps until project-level FPS is exposed).</summary>
    Frames,
}
