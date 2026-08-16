namespace Ben.Video.Editor.Models;

/// <summary>
/// Shared contract for timeline items that support per-clip volume
/// and keyframe-based volume automation.
/// </summary>
public interface IHasVolumeAutomation
{
    /// <summary>Scalar gain fallback used when VolumeAutomation has fewer than 2 keyframes.</summary>
    double Volume { get; set; }

    /// <summary>Ordered list of automation keyframes (sorted by Position ascending).</summary>
    List<VolumeKeyframe> VolumeAutomation { get; set; }

    /// <summary>
    /// Returns the linearly-interpolated gain at a normalised clip position [0,1].
    /// Falls back to the scalar <see cref="Volume"/> when automation is inactive.
    /// </summary>
    double GetVolumeAt(double position);
}
