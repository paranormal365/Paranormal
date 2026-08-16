namespace Ben.Video.Editor.Models;

/// <summary>
/// A single volume automation point within a clip.
/// Position is normalised [0.0, 1.0] relative to the clip's trimmed duration,
/// so the envelope scales automatically when trim or speed changes.
/// Volume is a linear gain multiplier: 0.0 = silence, 1.0 = unity (0 dB), 2.0 ≈ +6 dB.
/// </summary>
public sealed class VolumeKeyframe
{
    public Guid   Id       { get; init; } = Guid.NewGuid();
    public double Position { get; set; }  // [0.0, 1.0]
    public double Volume   { get; set; }  // [0.0, 2.0]
}
