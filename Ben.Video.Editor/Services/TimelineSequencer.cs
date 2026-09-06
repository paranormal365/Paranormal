using Ben.Video.Editor.Models;

namespace Ben.Video.Editor.Services;

/// <summary>One audio clip that should be audible right now, and where inside it to be.</summary>
/// <param name="Clip">The clip.</param>
/// <param name="SourceTime">Where in its own source file, in seconds.</param>
/// <param name="Volume">Its gain at this moment, automation included.</param>
public readonly record struct LiveAudioCue(AudioClip Clip, double SourceTime, double Volume);

/// <summary>
/// What the timeline looks and sounds like at one instant.
/// </summary>
/// <param name="Picture">
/// The clip whose frame should be on screen — a video or an image — or null in a gap.
/// </param>
/// <param name="PictureSourceTime">Where inside the picture clip's own source file, in seconds.</param>
/// <param name="PictureVolume">
/// The picture clip's own audio gain, or zero when it is muted, silent, or on a muted track.
/// </param>
/// <param name="NextCutAt">
/// The timeline time at which the picture changes — the end of this clip, the start of the next,
/// or <see cref="double.PositiveInfinity"/> when nothing else follows. What a player uses to know
/// when to swap elements and what to load into the idle one.
/// </param>
/// <param name="Audio">Every audio clip that should be playing, from every unmuted audio track.</param>
public readonly record struct LiveFrame(
    TrackItem? Picture,
    double PictureSourceTime,
    double PictureVolume,
    double NextCutAt,
    IReadOnlyList<LiveAudioCue> Audio)
{
    /// <summary>Nothing to show: black, but not necessarily silence.</summary>
    public bool IsGap => Picture is null;

    /// <summary>Nothing at all at this moment — no picture and no sound.</summary>
    public bool IsEmpty => IsGap && Audio.Count == 0;
}

/// <summary>
/// Reads the timeline at an instant, the way a player has to.
/// </summary>
/// <remarks>
/// <para><b>Why this exists.</b> Today's preview is a proxy: after every edit the editor re-encodes
/// a small video of the whole timeline and plays that. It is correct, and it is what export is
/// verified against — but a person editing an hour of footage waits for an encode to see a cut.
/// Camtasia plays the timeline itself, seeking between the source files as the playhead crosses
/// them. That is a sequence player, and this is the question it has to answer sixty times a
/// second: what is on screen, where inside its source, what is audible, and when does that
/// change (2026-09-05 audit, decision D5).</para>
///
/// <para><b>Why it is pure.</b> Everything hard about a sequence player is in this answer — the
/// trim arithmetic, the speed mapping, gaps, muted tracks, the boundary where one clip becomes the
/// next. Answering it in a component would put all of it behind a browser and a rendering loop.
/// Here it is a function of the timeline and a number.</para>
///
/// <para><b>What it deliberately does not do.</b> The picture comes from the first video track,
/// which is the base layer export composites everything else onto. A live player made of two video
/// elements cannot composite a second video track, so a timeline that uses one is a timeline whose
/// live preview is missing a layer — the rendered preview remains the honest one, and remains what
/// export is checked against.</para>
/// </remarks>
public static class TimelineSequencer
{
    /// <summary>
    /// Resolves the timeline at <paramref name="timelineTime"/>.
    /// </summary>
    /// <param name="tracks">Every track, in any order.</param>
    /// <param name="timelineTime">Seconds from the start of the project.</param>
    public static LiveFrame Resolve(IReadOnlyList<TimelineTrack>? tracks, double timelineTime)
    {
        if (tracks is null || tracks.Count == 0) return Empty;

        // A negative or nonsensical clock reads as the start rather than as nothing: a player that
        // shows black because a number arrived as NaN is harder to diagnose than one that shows
        // the first frame.
        var t = double.IsFinite(timelineTime) ? Math.Max(0, timelineTime) : 0;

        var pictureTrack = PictureTrack(tracks);
        var picture      = pictureTrack is null ? null : PictureAt(pictureTrack, t);

        return new LiveFrame(
            picture,
            picture is null ? 0 : SourceTimeOf(picture, t),
            picture is null ? 0 : PictureVolumeOf(picture, pictureTrack!, t),
            NextCutAt(pictureTrack, picture, t),
            AudibleAt(tracks, t));
    }

    /// <summary>
    /// The clip that comes after <paramref name="timelineTime"/> on the picture track, for a
    /// player that wants it loaded before the cut arrives.
    /// </summary>
    /// <remarks>
    /// A sequence player's whole quality is whether the next clip is ready when the playhead
    /// reaches it. Seeking a video element takes long enough to see, so the second element is
    /// loaded and seeked while the first one is still playing.
    /// </remarks>
    public static TrackItem? NextPicture(IReadOnlyList<TimelineTrack>? tracks, double timelineTime)
    {
        var track = tracks is null ? null : PictureTrack(tracks);
        if (track is null) return null;

        var t = double.IsFinite(timelineTime) ? Math.Max(0, timelineTime) : 0;

        return Pictures(track)
            .Where(i => i.TimelinePosition > t)
            .OrderBy(i => i.TimelinePosition)
            .FirstOrDefault();
    }

    /// <summary>
    /// Where inside <paramref name="item"/>'s own source file the timeline is at
    /// <paramref name="timelineTime"/>.
    /// </summary>
    /// <remarks>
    /// Trim first, then speed. A clip trimmed to start ten seconds in and played at double speed is
    /// two seconds further into its source for every second of timeline. The result is held inside
    /// the trimmed region, because the timeline lays clips out by their untrimmed length in places
    /// and a speed above one would otherwise run the source past its own end.
    /// </remarks>
    public static double SourceTimeOf(TrackItem item, double timelineTime)
    {
        var elapsed = Math.Max(0, timelineTime - item.TimelinePosition);

        return item switch
        {
            VideoClip v => Math.Clamp(
                v.StartTrim + elapsed * (v.Speed > 0 ? v.Speed : 1.0),
                v.StartTrim,
                v.StartTrim + v.TrimmedDuration),

            AudioClip a => Math.Clamp(a.StartTrim + elapsed, a.StartTrim, a.StartTrim + a.TrimmedDuration),

            // An image has no source clock: every moment of it is the same frame.
            _ => 0,
        };
    }

    // ── The picture ───────────────────────────────────────────────────────────

    private static readonly LiveFrame Empty =
        new(null, 0, 0, double.PositiveInfinity, []);

    /// <summary>
    /// The base layer: the first video track, which is what export composites everything onto.
    /// </summary>
    private static TimelineTrack? PictureTrack(IReadOnlyList<TimelineTrack> tracks) =>
        tracks.Where(t => t.Type == TrackType.Video).OrderBy(t => t.Order).FirstOrDefault();

    /// <summary>Only the two kinds of item that carry a frame. Overlays are drawn over the top.</summary>
    private static IEnumerable<TrackItem> Pictures(TimelineTrack track) =>
        track.Items.Where(i => i is VideoClip or ImageClip);

    private static TrackItem? PictureAt(TimelineTrack track, double t) =>
        Pictures(track)
            .Where(i => Contains(i, t))
            // Sequential items never overlap (see TrackLayout), but a project written before that
            // was enforced can still contain one. Later start wins, which is what the timeline
            // draws on top.
            .OrderByDescending(i => i.TimelinePosition)
            .FirstOrDefault();

    private static bool Contains(TrackItem item, double t) =>
        t >= item.TimelinePosition && t < item.TimelinePosition + Math.Max(0, item.EffectiveLength);

    /// <summary>
    /// When the picture stops being this picture.
    /// </summary>
    private static double NextCutAt(TimelineTrack? track, TrackItem? picture, double t)
    {
        if (track is null) return double.PositiveInfinity;

        // Inside a clip, the cut is wherever this clip ends — the next one may begin later, and
        // the gap between them is black.
        if (picture is not null)
            return picture.TimelinePosition + Math.Max(0, picture.EffectiveLength);

        // In a gap, it is the start of whatever comes next.
        var next = Pictures(track)
            .Where(i => i.TimelinePosition > t)
            .OrderBy(i => i.TimelinePosition)
            .FirstOrDefault();

        return next?.TimelinePosition ?? double.PositiveInfinity;
    }

    // ── The sound ─────────────────────────────────────────────────────────────

    /// <summary>
    /// A picture clip's own soundtrack, which its video element is playing.
    /// </summary>
    /// <remarks>
    /// Muting a track means what it says here, unlike in the old preview where a muted track still
    /// played and was still mixed into the export (2026-09-05 audit, audio-5).
    /// </remarks>
    private static double PictureVolumeOf(TrackItem picture, TimelineTrack track, double t)
    {
        if (track.IsMuted) return 0;

        if (picture is not VideoClip video) return 0;
        if (video.MuteAudio || !video.HasAudio) return 0;

        return Gain(video.GetVolumeAt(NormalisedPosition(video, t)));
    }

    private static IReadOnlyList<LiveAudioCue> AudibleAt(IReadOnlyList<TimelineTrack> tracks, double t)
    {
        List<LiveAudioCue>? cues = null;

        foreach (var track in tracks)
        {
            if (track.Type != TrackType.Audio || track.IsMuted) continue;

            foreach (var clip in track.Items.OfType<AudioClip>())
            {
                if (clip.MuteAudio || !Contains(clip, t)) continue;

                cues ??= [];
                cues.Add(new LiveAudioCue(
                    clip,
                    SourceTimeOf(clip, t),
                    Gain(clip.GetVolumeAt(NormalisedPosition(clip, t)))));
            }
        }

        return cues ?? (IReadOnlyList<LiveAudioCue>)[];
    }

    /// <summary>Where the playhead sits inside a clip, as 0 to 1 — what volume automation reads.</summary>
    private static double NormalisedPosition(TrackItem item, double t)
    {
        var length = Math.Max(0, item.EffectiveLength);

        return length <= 0 ? 0 : Math.Clamp((t - item.TimelinePosition) / length, 0, 1);
    }

    /// <summary>
    /// A gain a media element will accept.
    /// </summary>
    /// <remarks>
    /// Automation and the scalar volume both allow more than unity, which ffmpeg can do and an
    /// HTML media element cannot: assigning a volume above 1 throws, and the player stops.
    /// </remarks>
    private static double Gain(double raw) =>
        double.IsFinite(raw) ? Math.Clamp(raw, 0, 1) : 1;
}
