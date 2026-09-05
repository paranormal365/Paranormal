namespace Ben.Video.Editor.Services;

/// <summary>
/// Turning a time into a frame number, and back.
/// </summary>
/// <remarks>
/// <para>The counter read one past the end at the end. It floored the time into a frame index and
/// then displayed the index plus one, which is right everywhere except the last moment: playing to
/// the end of a three hundred frame clip showed "F0301 / 0300", a frame that does not exist
/// (2026-09-05 audit, preview-14).</para>
///
/// <para>Pure, because frame arithmetic is the sort of thing that is either exactly right or
/// quietly wrong at one end, and that is much easier to see in a test than on screen.</para>
/// </remarks>
public static class FrameMath
{
    /// <summary>How many frames a clip of this length holds.</summary>
    public static int TotalFrames(double durationSeconds, double fps)
    {
        if (durationSeconds <= 0 || fps <= 0) return 0;
        return Math.Max(1, (int)Math.Ceiling(durationSeconds * fps));
    }

    /// <summary>
    /// The frame being shown at this moment, counting from one.
    /// </summary>
    /// <remarks>
    /// Clamped to the last frame: a player sitting exactly on its own duration is at the end, not
    /// one past it.
    /// </remarks>
    public static int FrameAt(double seconds, double fps, double durationSeconds)
    {
        if (fps <= 0) return 0;

        var total = TotalFrames(durationSeconds, fps);
        if (total == 0) return 0;

        var index = (int)Math.Floor(Math.Max(0, seconds) * fps);
        return Math.Clamp(index + 1, 1, total);
    }

    /// <summary>When a frame begins, in seconds.</summary>
    public static double TimeOfFrame(int frameNumber, double fps) =>
        fps <= 0 ? 0 : Math.Max(0, frameNumber - 1) / fps;

    /// <summary>One frame's worth of time, for stepping.</summary>
    public static double FrameDuration(double fps) => fps <= 0 ? 0 : 1.0 / fps;
}
