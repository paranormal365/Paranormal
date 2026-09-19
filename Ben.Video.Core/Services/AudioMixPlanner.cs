using Ben.Video.Editor.Models;

namespace Ben.Video.Editor.Services;

/// <summary>
/// One audio clip, trimmed and positioned, on its way into the mix.
/// </summary>
/// <param name="Source">The clip's file in the engine's filesystem.</param>
/// <param name="Start">Where in the source the clip begins, in seconds.</param>
/// <param name="End">Where in the source it ends.</param>
/// <param name="Filter">
/// The clip's own volume, balance and fades, already positioned on the timeline by an
/// <c>adelay</c> when it does not start at zero.
/// </param>
public readonly record struct AudioMixSegment(string Source, double Start, double End, string Filter);

/// <summary>
/// Works out which audio clips go into the mix, and what each one sounds like.
/// </summary>
/// <remarks>
/// <para>This lived inside the export, which is why the Working Window had no sound: a separate
/// audio track was inaudible while you edited, and the only way to hear whether the music sat right
/// against the picture was the full-quality Preview, which re-renders the whole timeline
/// (2026-09-05 audit, audio-6). Editing to a soundtrack you cannot hear is not editing.</para>
///
/// <para>Pulling the arithmetic out gives the preview and the export the same answer by
/// construction, and makes the part that is easy to get wrong — a clip's trim, its position, its
/// fades — checkable without an engine.</para>
/// </remarks>
public static class AudioMixPlanner
{
    /// <summary>
    /// The segments to render and mix, in track order.
    /// </summary>
    /// <param name="clips">
    /// The audible audio clips. Muted tracks are the caller's business to exclude — the store
    /// knows which tracks are muted and this does not.
    /// </param>
    public static IReadOnlyList<AudioMixSegment> Plan(IEnumerable<AudioClip> clips)
    {
        ArgumentNullException.ThrowIfNull(clips);

        var segments = new List<AudioMixSegment>();

        foreach (var clip in clips)
        {
            if (clip.MemFsName is null) continue;

            var start = clip.StartTrim;
            var end   = clip.EndTrim > clip.StartTrim ? clip.EndTrim : clip.Duration;

            // A clip with nothing left after its trims contributes nothing, and asking ffmpeg for
            // a zero-length segment fails the whole mix rather than quietly producing silence.
            if (end - start <= 0) continue;

            segments.Add(new AudioMixSegment(clip.MemFsName, start, end, BuildFilter(clip, end - start)));
        }

        return segments;
    }

    /// <summary>
    /// A clip's own sound, moved to where it sits on the timeline.
    /// </summary>
    /// <remarks>
    /// The delay is part of the filter rather than a separate offset for the mix to apply, which
    /// is what lets <c>amix</c> combine the segments with no position arithmetic of its own.
    /// </remarks>
    public static string BuildFilter(AudioClip clip, double clipDuration)
    {
        ArgumentNullException.ThrowIfNull(clip);

        var chain   = ExportArgBuilders.BuildAudioClipFilterChain(clip, clipDuration);
        var delayMs = (int)Math.Round(Math.Max(0, clip.TimelinePosition) * 1000.0);

        return delayMs > 0 ? $"{chain},adelay={delayMs}:all=1" : chain;
    }
}
