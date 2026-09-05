namespace Ben.Video.Editor.Models;

/// <summary>
/// The kind of timeline track.
/// </summary>
public enum TrackType
{
    /// <summary>A video track that holds VideoClip items.</summary>
    Video,

    /// <summary>An audio-only track that holds AudioClip items.</summary>
    Audio
}

/// <summary>
/// Base record for every item that can be placed on a timeline track.
/// Concrete types: <see cref="VideoClip"/>, <see cref="AudioClip"/>,
/// <see cref="Transition"/>, <see cref="TextOverlay"/>.
/// </summary>
public abstract record TrackItem
{
    /// <summary>Unique identifier for this item.</summary>
    public Guid Id { get; init; } = Guid.NewGuid();

    /// <summary>Display name shown on the timeline tile.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Start position on the timeline in seconds.</summary>
    public double TimelinePosition { get; set; }

    /// <summary>Duration of this item on the timeline in seconds.</summary>
    public double Duration { get; set; }

    /// <summary>
    /// The media-bin entry this was placed from, when it was.
    /// </summary>
    /// <remarks>
    /// One source can be placed on the timeline as many times as you like, so the bin needs to know
    /// how many of its own entries are in use — the "on timeline ×2" a card shows. Null for
    /// anything that predates the bin, and for the bin entries themselves.
    /// </remarks>
    public Guid? SourceBinId { get; set; }

    /// <summary>
    /// How much of the timeline this item occupies, in seconds.
    /// </summary>
    /// <remarks>
    /// The one place to ask. Video and audio clips answer with their trimmed length and everything
    /// else with its plain duration; before this, each caller special-cased <c>VideoClip</c> and
    /// quietly used the untrimmed <see cref="Duration"/> for audio (2026-09-05 audit, audio-11).
    /// Speed is deliberately not applied here — that changes how long the render is, not how much
    /// room the item takes on the timeline.
    /// </remarks>
    public virtual double EffectiveLength => Duration;

    /// <summary>Sort order within the track — kept in sync with <see cref="TimelinePosition"/> so
    /// the sequential (video/audio/transition) row's flex layout renders chips in the right
    /// order. NOT used for overlay stacking — see <see cref="LayerIndex"/>.</summary>
    public int Order { get; set; }

    /// <summary>
    /// Monotonically-increasing creation order, meaningful only for overlay items (CalloutClip,
    /// TextOverlay, ClipArtClip — items that render in their own timeline row per item, stacked
    /// by this value, rather than the shared sequential video/audio row). Unlike <see cref="Order"/>,
    /// this deliberately does NOT track <see cref="TimelinePosition"/> — item #36-adjacent backlog
    /// item #39: "everything added gets its own layer, each layer higher than any added before it,"
    /// independent of where on the timeline it starts. Unused (stays 0) on VideoClip/AudioClip/
    /// ImageClip/Transition.
    /// </summary>
    public int LayerIndex { get; set; }

    /// <summary>
    /// Set to <c>true</c> on project load for clips whose source media file is not yet
    /// written to ffmpeg MEMFS.  Cleared when the user re-links the file via ClipBrowser.
    /// Does not affect clips imported in the same session (they are always <c>false</c>).
    /// </summary>
    public bool IsMediaMissing { get; set; }

    /// <summary>
    /// Original file name (no path) stored so the UI can hint which file to re-link.
    /// </summary>
    public string? OriginalFileName { get; set; }

    /// <summary>
    /// The media-library file this clip's media came from, when it came from one.
    /// </summary>
    /// <remarks>
    /// <para>A project was not portable. A clip persisted only its original file name and its
    /// stored extension, and restoring read <i>this</i> browser's storage by clip id — so a project
    /// opened anywhere else, or after the storage was cleared, came back with every clip missing
    /// and a manual re-link as the only way out. Help promised you could "pick it up on another
    /// machine" (2026-09-05 audit, F14).</para>
    ///
    /// <para>This is what makes that true: with the file's own id the editor can fetch it again
    /// from the server. Null for a clip imported straight off somebody's disk, which nothing can
    /// re-fetch — the file exists only where they put it.</para>
    /// </remarks>
    public Guid? SourceFileId { get; set; }

    /// <summary>
    /// How large the source file was, so a re-fetch can be checked against it.
    /// </summary>
    /// <remarks>
    /// Free to record and enough to catch the common mistake: a re-link that picked the wrong file,
    /// or a server file replaced since the project was saved. See <see cref="SourceContentHash"/>
    /// for when the stronger check is available.
    /// </remarks>
    public long? SourceFileSize { get; set; }

    /// <summary>
    /// A SHA-256 of the source file, when one was cheap enough to take.
    /// </summary>
    /// <remarks>
    /// <para>Deliberately optional. Hashing needs the whole file in memory at once — the browser's
    /// digest has no streaming form — so it is taken only under a size ceiling and left null above
    /// it. Session footage is routinely hundreds of megabytes, and stalling an import to hash one
    /// would cost more than the check is worth.</para>
    ///
    /// <para>Null therefore means "not taken", never "did not match". Verification uses the
    /// strongest thing it has.</para>
    /// </remarks>
    public string? SourceContentHash { get; set; }

    /// <summary>
    /// File extension of the source clip stored in OPFS (e.g. <c>".mp4"</c>).
    /// <c>null</c> when the clip has not been persisted to OPFS (imported in the same
    /// session or on a browser that does not support OPFS).
    /// </summary>
    public string? OpfsExt { get; set; }

    /// <summary>
    /// Id of another <see cref="TrackItem"/> this one is linked to (item #52) — a
    /// <see cref="VideoClip"/> paired with an <see cref="AudioClip"/> from the same source
    /// take, so their relative offset can be shown and independently trimmed to produce a J-cut
    /// (linked clip's edit point leads) or L-cut (linked clip's edit point trails). The link is
    /// always symmetric: both items store each other's Id. Unused (stays <c>null</c>) on any
    /// item that isn't part of a link. Trimming/moving one side of a link does not move the
    /// other automatically — see <c>ClipStore.LinkClips</c>'s doc comment for why that's a
    /// deliberate scope cut.
    /// </summary>
    public Guid? LinkedClipId { get; set; }
}
