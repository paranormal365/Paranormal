namespace Ben.Video.Editor.Models;

/// <summary>
/// A named cue point on the timeline ruler.
/// Markers are global to the timeline (not bound to any track).
/// <see cref="Label"/> doubles as chapter title metadata when exporting with
/// chapters embedded (see <c>ExportService.EmbedChaptersAsync</c>).
/// </summary>
public sealed class TimelineMarker
{
    /// <summary>Unique identifier.</summary>
    public Guid Id { get; init; } = Guid.NewGuid();

    /// <summary>Display label shown on the ruler flag. Defaults to the timecode.</summary>
    public string Label { get; set; } = string.Empty;

    /// <summary>Position on the timeline in seconds from the start.</summary>
    public double TimeSeconds { get; set; }

    /// <summary>
    /// CSS colour string for the marker flag (e.g. "#f59e0b").
    /// Assigned from the preset palette when the marker is created.
    /// </summary>
    public string Color { get; set; } = "#f59e0b";
}
