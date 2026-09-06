namespace Ben.Video.Editor.Models;

/// <summary>
/// An audio-only clip placed on an Audio track.
/// Requires the AudioTracks feature flag.
/// </summary>
public sealed record AudioClip : TrackItem, IHasVolumeAutomation
{
    /// <summary>Trim start within the source audio file in seconds.</summary>
    public double StartTrim { get; set; }

    /// <summary>Trim end within the source audio file in seconds.</summary>
    public double EndTrim { get; set; }

    /// <summary>
    /// How long this clip actually occupies on the timeline, once its trims are taken into account.
    /// </summary>
    /// <remarks>
    /// <see cref="VideoClip"/> has had this since the beginning; audio did not, so every shared
    /// path — track length, ripple delete, where the next import lands, splitting at the playhead —
    /// silently fell back to <see cref="TrackItem.Duration"/>, the length of the whole source file.
    /// A trimmed thirty-second track therefore still reserved three minutes (2026-09-05 audit,
    /// audio-11).
    /// </remarks>
    public double TrimmedDuration =>
        EndTrim > StartTrim ? EndTrim - StartTrim : Duration;

    /// <inheritdoc />
    public override double EffectiveLength => TrimmedDuration;


    /// <summary>
    /// Scalar gain fallback (0.0 = silence, 1.0 = unity, 2.0 ≈ +6 dB).
    /// Used when VolumeAutomation has fewer than 2 keyframes.
    /// </summary>
    public double Volume { get; set; } = 1.0;

    /// <summary>Ordered automation keyframes (sorted by Position ascending).</summary>
    public List<VolumeKeyframe> VolumeAutomation { get; set; } = [];

    /// <summary>Fade-in duration in seconds (0 = no fade).</summary>
    public double FadeInSeconds { get; set; }

    /// <summary>Fade-out duration in seconds (0 = no fade).</summary>
    public double FadeOutSeconds { get; set; }

    /// <summary>
    /// Left-channel gain multiplier, applied on top of <see cref="Volume"/>/automation
    /// (0.0 = silence, 1.0 = unity, 2.0 ≈ +6 dB). Lets a stereo clip's channels be balanced
    /// independently — e.g. muting a noisy channel or correcting a too-hot mic (backlog #10).
    /// </summary>
    public double LeftVolume { get; set; } = 1.0;

    /// <summary>Right-channel gain multiplier — see <see cref="LeftVolume"/>.</summary>
    public double RightVolume { get; set; } = 1.0;

    /// <summary>MEMFS filename of the source audio file (set after the file is written to ffmpeg MEMFS).</summary>
    public string? MemFsName { get; set; }

    /// <summary>
    /// Browser object URL (blob URL) for the source audio file.
    /// Populated after import so WaveSurfer can render the waveform without going through ffmpeg.
    /// </summary>
    public string? BlobUrl { get; set; }

    /// <summary>Waveform peak data for timeline rendering (populated after load).</summary>
    public float[]? WaveformPeaks { get; set; }

    /// <summary>
    /// Returns the linearly-interpolated gain at a normalised position [0,1] within the clip.
    /// Falls back to the scalar <see cref="Volume"/> when fewer than 2 keyframes are present.
    /// </summary>
    public double GetVolumeAt(double position)
    {
        if (VolumeAutomation.Count < 2) return Volume;

        var before = VolumeAutomation.LastOrDefault(k => k.Position <= position);
        var after  = VolumeAutomation.FirstOrDefault(k => k.Position >  position);

        if (before is null) return after!.Volume;
        if (after  is null) return before.Volume;

        var t = (position - before.Position) / (after.Position - before.Position);
        return before.Volume + t * (after.Volume - before.Volume);
    }
}
