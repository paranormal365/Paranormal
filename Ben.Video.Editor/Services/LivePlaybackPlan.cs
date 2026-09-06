using Ben.Video.Editor.Models;

namespace Ben.Video.Editor.Services;

/// <summary>What kind of thing fills a stretch of the timeline.</summary>
public enum LiveSegmentKind
{
    /// <summary>Nothing on the picture track here: black.</summary>
    Gap,
    Video,
    Image,
}

/// <summary>
/// One stretch of the picture track, as a player needs it.
/// </summary>
/// <param name="Start">Timeline second this stretch begins.</param>
/// <param name="End">Timeline second it ends.</param>
/// <param name="Kind">What to show.</param>
/// <param name="ClipId">Which clip's source to show, or <see cref="Guid.Empty"/> for a gap.</param>
/// <param name="SourceStart">Where in that source the stretch begins, in seconds.</param>
/// <param name="Speed">Playback rate for the element.</param>
/// <param name="Volume">The clip's own soundtrack gain, 0 to 1.</param>
public sealed record LiveSegment(
    double Start,
    double End,
    LiveSegmentKind Kind,
    Guid ClipId,
    double SourceStart,
    double Speed,
    double Volume);

/// <summary>One stretch of one audio clip.</summary>
/// <param name="Start">Timeline second it begins.</param>
/// <param name="End">Timeline second it ends.</param>
/// <param name="ClipId">Which clip's source to play.</param>
/// <param name="SourceStart">Where in that source to begin, in seconds.</param>
/// <param name="Volume">Its gain, 0 to 1.</param>
public sealed record LiveAudioSegment(
    double Start,
    double End,
    Guid ClipId,
    double SourceStart,
    double Volume);

/// <summary>
/// The whole timeline written out as something a player can follow without asking again.
/// </summary>
/// <param name="Picture">Every stretch of the picture track, in order, with no holes.</param>
/// <param name="Audio">Every audible stretch of every audio clip.</param>
/// <param name="Duration">Where the timeline ends.</param>
/// <remarks>
/// <para>A live player runs at sixty frames a second and cannot ask .NET what to show on each one
/// — the interop alone would cost more than the drawing. So the timeline is resolved once, into a
/// list the player follows on its own, and rebuilt whenever the timeline changes.</para>
///
/// <para><b>What a plan approximates.</b> Volume automation inside a clip becomes one number, the
/// clip's own gain, because a media element has a volume rather than a curve. Transitions become
/// hard cuts. Effects, callouts, titles and clip art are not in the picture at all — the overlays
/// are drawn over the player by the same components that draw them over the rendered preview, and
/// the effects are the reason the rendered preview still exists and is still what export is
/// checked against (2026-09-05 audit, decision D5).</para>
/// </remarks>
public sealed record LivePlaybackPlan(
    IReadOnlyList<LiveSegment> Picture,
    IReadOnlyList<LiveAudioSegment> Audio,
    double Duration)
{
    /// <summary>A plan for a timeline with nothing on it.</summary>
    public static LivePlaybackPlan Empty { get; } = new([], [], 0);

    /// <summary>Whether there is anything at all to play.</summary>
    public bool IsEmpty => Duration <= 0 || (Picture.Count == 0 && Audio.Count == 0);

    /// <summary>Every clip whose source file the player will need.</summary>
    /// <remarks>
    /// What the host resolves to blob URLs before playback starts. A source that cannot be found
    /// is what makes a segment fall back to the rendered preview instead.
    /// </remarks>
    public IReadOnlyList<Guid> RequiredSources =>
        Picture.Where(p => p.Kind is not LiveSegmentKind.Gap).Select(p => p.ClipId)
            .Concat(Audio.Select(a => a.ClipId))
            .Distinct()
            .ToList();

    /// <summary>
    /// Writes the timeline out as a plan.
    /// </summary>
    /// <param name="tracks">Every track, in any order.</param>
    public static LivePlaybackPlan Build(IReadOnlyList<TimelineTrack>? tracks)
    {
        if (tracks is null || tracks.Count == 0) return Empty;

        var picture = BuildPicture(tracks);
        var audio   = BuildAudio(tracks);

        var duration = Math.Max(
            picture.Count > 0 ? picture[^1].End : 0,
            audio.Count   > 0 ? audio.Max(a => a.End) : 0);

        return new LivePlaybackPlan(picture, audio, duration);
    }

    private static List<LiveSegment> BuildPicture(IReadOnlyList<TimelineTrack> tracks)
    {
        var track = tracks.Where(t => t.Type == TrackType.Video).OrderBy(t => t.Order).FirstOrDefault();

        List<LiveSegment> segments = [];
        if (track is null) return segments;

        var items = track.Items
            .Where(i => i is VideoClip or ImageClip && i.EffectiveLength > 0)
            .OrderBy(i => i.TimelinePosition)
            .ToList();

        var cursor = 0.0;

        foreach (var item in items)
        {
            var start = Math.Max(cursor, item.TimelinePosition);
            var end   = item.TimelinePosition + item.EffectiveLength;

            // Wholly buried under an earlier clip. Only possible in a project written before
            // overlap was prevented, and skipping it is what the timeline draws anyway.
            if (end <= cursor) continue;

            // Black between clips, written out rather than left as a hole, so the player never has
            // to ask what to do with a moment the plan does not mention.
            if (start > cursor)
                segments.Add(new LiveSegment(cursor, start, LiveSegmentKind.Gap, Guid.Empty, 0, 1, 0));

            segments.Add(item switch
            {
                VideoClip v => new LiveSegment(
                    start, end, LiveSegmentKind.Video, v.Id,
                    TimelineSequencer.SourceTimeOf(v, start),
                    v.Speed > 0 ? v.Speed : 1.0,
                    VolumeOf(v, track)),

                _ => new LiveSegment(start, end, LiveSegmentKind.Image, item.Id, 0, 1, 0),
            });

            cursor = end;
        }

        return segments;
    }

    private static List<LiveAudioSegment> BuildAudio(IReadOnlyList<TimelineTrack> tracks)
    {
        List<LiveAudioSegment> segments = [];

        foreach (var track in tracks.Where(t => t.Type == TrackType.Audio && !t.IsMuted))
            foreach (var clip in track.Items.OfType<AudioClip>()
                         .Where(c => !c.MuteAudio && c.EffectiveLength > 0)
                         .OrderBy(c => c.TimelinePosition))
            {
                segments.Add(new LiveAudioSegment(
                    clip.TimelinePosition,
                    clip.TimelinePosition + clip.EffectiveLength,
                    clip.Id,
                    clip.StartTrim,
                    Clamp(clip.Volume)));
            }

        return segments;
    }

    private static double VolumeOf(VideoClip clip, TimelineTrack track) =>
        track.IsMuted || clip.MuteAudio || !clip.HasAudio ? 0 : Clamp(clip.Volume);

    /// <summary>
    /// A media element's volume is 0 to 1, and assigning anything else throws — which stops the
    /// player, not just the sound.
    /// </summary>
    private static double Clamp(double raw) => double.IsFinite(raw) ? Math.Clamp(raw, 0, 1) : 1;
}
