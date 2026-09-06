namespace Ben.Video.Editor.Models;

/// <summary>
/// Represents a single horizontal track row on the timeline (video or audio).
/// Each track holds an ordered list of <see cref="TrackItem"/> objects.
/// </summary>
public sealed class TimelineTrack
{
    /// <summary>Unique identifier for this track.</summary>
    public Guid Id { get; init; } = Guid.NewGuid();

    /// <summary>Display label shown on the track header (e.g. "Video 1", "Audio 1").</summary>
    public string Label { get; set; } = string.Empty;

    /// <summary>Whether this is a video or audio track.</summary>
    public TrackType Type { get; init; }

    /// <summary>Vertical sort order among all tracks (lower = higher on screen).</summary>
    public int Order { get; set; }

    /// <summary>Whether the track is muted (audio suppressed during playback and export).</summary>
    public bool IsMuted { get; set; }

    /// <summary>
    /// Whether everything else should drop in level while this track is playing.
    /// </summary>
    /// <remarks>
    /// <para>What a narration track needs. Music and room tone sit at a level chosen for the
    /// stretches with nobody talking, and the moment a voice comes in they are too loud — so the
    /// usual remedy is a volume envelope drawn by hand around every line, redrawn whenever the
    /// timing moves (2026-09-05 audit, the completeness critic's ducking item).</para>
    ///
    /// <para>Off by default. A project with no narration track mixes exactly as it did.</para>
    /// </remarks>
    public bool DucksOthers { get; set; }

    /// <summary>Whether the track is locked (items cannot be moved or trimmed).</summary>
    public bool IsLocked { get; set; }

    /// <summary>All items on this track, ordered by <see cref="TrackItem.Order"/>.</summary>
    public List<TrackItem> Items { get; init; } = [];

    // ── Convenience helpers ──────────────────────────────────────────────────

    /// <summary>Video clips on this track (only populated for Video tracks).</summary>
    public IEnumerable<VideoClip> VideoClips =>
        Items.OfType<VideoClip>().OrderBy(c => c.Order);

    /// <summary>Audio clips on this track (only populated for Audio tracks).</summary>
    public IEnumerable<AudioClip> AudioClips =>
        Items.OfType<AudioClip>().OrderBy(c => c.Order);

    /// <summary>Transitions between clips (requires Transitions feature flag).</summary>
    public IEnumerable<Transition> Transitions =>
        Items.OfType<Transition>().OrderBy(t => t.TimelinePosition);

    /// <summary>Text overlays on this track (requires TextOverlays feature flag).</summary>
    public IEnumerable<TextOverlay> TextOverlays =>
        Items.OfType<TextOverlay>().OrderBy(o => o.TimelinePosition);

    /// <summary>Image clips on this track (requires ImageClips feature flag).</summary>
    public IEnumerable<ImageClip> ImageClips =>
        Items.OfType<ImageClip>().OrderBy(c => c.Order);

    /// <summary>Callout/annotation clips on this track.</summary>
    public IEnumerable<CalloutClip> CalloutClips =>
        Items.OfType<CalloutClip>().OrderBy(c => c.Order);

    /// <summary>Clipart, shape, and catalog-asset overlay clips on this track.</summary>
    public IEnumerable<ClipArtClip> ClipArtClips =>
        Items.OfType<ClipArtClip>().OrderBy(c => c.Order);

    /// <summary>Total duration of all items on this track in seconds.</summary>
    /// <remarks>
    /// Uses <see cref="VideoClip.TrimmedDuration"/> rather than the raw
    /// <see cref="TrackItem.Duration"/> for video clips — Duration is the full
    /// source-media length, which stays unchanged by trimming/splitting, so a
    /// trimmed or split clip would otherwise inflate the reported total past
    /// what's actually rendered on the timeline (chip width uses TrimmedDuration
    /// too, via VideoTimeline.razor's ItemDuration helper — keep both in sync).
    /// </remarks>
    public double TotalDuration =>
        Items.Count == 0 ? 0 : Items.Max(i => i.TimelinePosition + (i is VideoClip vc ? vc.TrimmedDuration : i.Duration));
}
