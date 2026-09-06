namespace Ben.Video.Editor.Models;

/// <summary>
/// Feature flags that control which capabilities are active in the video editor.
/// Configure globally at DI registration time via AddBenVideoEditor(options => ...).
/// Individual placements can override any flag via [Parameter] on &lt;VideoEditor /&gt;.
/// </summary>
public sealed class VideoEditorOptions
{
    /// <summary>
    /// Enable multiple video and audio tracks on the timeline.
    /// When false the editor operates in single-track mode (one video track, no separate audio track).
    /// Default: false
    /// </summary>
    public bool MultiTrack { get; set; } = false;

    /// <summary>
    /// Whether the first thing imported into an empty project is placed on the timeline for you.
    /// </summary>
    /// <remarks>
    /// Imports go to the media bin and are placed when you ask, which is how an editor with a bin
    /// behaves. The exception is the very first file into an empty project: nobody opens an editor,
    /// picks a video and wants to look at an empty timeline. Default: true.
    /// </remarks>
    public bool AutoPlaceFirstImport { get; set; } = true;

    /// <summary>
    /// Enable dedicated audio tracks independent from video clips.
    /// Requires MultiTrack = true to show multiple audio tracks; a single audio track
    /// is shown even in single-track mode when this is true.
    /// Default: false
    /// </summary>
    public bool AudioTracks { get; set; } = false;

    /// <summary>
    /// Enable transition effects (fade, dissolve, wipe, etc.) between clips on the timeline.
    /// Default: false
    /// </summary>
    public bool Transitions { get; set; } = false;

    /// <summary>
    /// Enable timed text and title overlays that render on top of the video.
    /// Default: false
    /// </summary>
    public bool TextOverlays { get; set; } = false;

    /// <summary>
    /// Allow image files (PNG, JPG, GIF, WebP) to be imported as timeline clips.
    /// Images are displayed for a configurable duration and converted to video segments on export.
    /// Default: true
    /// </summary>
    public bool ImageClips { get; set; } = true;

    /// <summary>
    /// Maximum number of video tracks shown in multi-track mode.
    /// Ignored when MultiTrack = false.
    /// Default: 4
    /// </summary>
    public int MaxVideoTracks { get; set; } = 4;

    /// <summary>
    /// Maximum number of audio tracks shown when AudioTracks = true.
    /// Default: 2
    /// </summary>
    public int MaxAudioTracks { get; set; } = 2;

    /// <summary>
    /// Enable draggable in/out trim handles directly on video clip chips in the timeline.
    /// When true, users can drag the left edge (StartTrim) or right edge (EndTrim) of a
    /// clip chip without opening the ClipEditor side panel.
    /// Default: false
    /// </summary>
    public bool InlineTrimming { get; set; } = true;

    /// <summary>
    /// Enable timeline markers (named cue points) on the ruler.
    /// When true, an "Add Marker" button appears in the toolbar and the M keyboard shortcut
    /// places a marker at the current playhead position.
    /// Default: true
    /// </summary>
    public bool Markers { get; set; } = true;

    /// <summary>
    /// Enable per-clip visual effects (brightness, contrast, saturation, fade in/out)
    /// in the ClipEditor side panel.
    /// When false the Effects section is hidden; effects already set on clips are still
    /// applied during export.
    /// Default: false
    /// </summary>
    public bool VideoEffects { get; set; } = false;

    /// <summary>
    /// Enable the online media library panel in the ClipBrowser, allowing users to browse
    /// and import files from the AverageBen media library (or any compatible API).
    /// Requires <see cref="MediaLibraryBaseUrl"/> to be set.
    /// Default: false
    /// </summary>
    public bool MediaLibrary { get; set; } = false;

    /// <summary>
    /// Base URL of the media library API, e.g. <c>"https://api.averageben.com"</c>.
    /// Used by <see cref="Ben.Video.Editor.Services.HttpMediaLibraryProvider"/>.
    /// Leave null when <see cref="MediaLibrary"/> is false.
    /// </summary>
    public string? MediaLibraryBaseUrl { get; set; }

    /// <summary>
    /// URL used to HTTP POST a newly created project document to a WebAPI.
    /// When set, <see cref="Ben.Video.Editor.Services.ProjectService.SaveToServerAsync"/> will
    /// POST the serialized <c>.benvideo</c> JSON to this endpoint.
    /// Leave null to disable server save (local download only).
    /// Example: <c>"https://api.averageben.com/api/projects"</c>
    /// </summary>
    public string? DocumentPostUrl { get; set; }

    /// <summary>
    /// URL used to HTTP PUT/PATCH an existing project document on a WebAPI.
    /// When set, <see cref="Ben.Video.Editor.Services.ProjectService.SaveToServerAsync"/> can
    /// target this endpoint to update an already-persisted document.
    /// Leave null to fall back to <see cref="DocumentPostUrl"/> for every save.
    /// Example: <c>"https://api.averageben.com/api/projects/{id}"</c>
    /// </summary>
    public string? DocumentSaveUrl { get; set; }

    /// <summary>
    /// Enable project Save / Load in the Toolbar.
    /// When true, "Save" downloads a <c>.benvideo</c> JSON file and "Open" restores
    /// a previously saved project.  Media files must be re-linked after loading because
    /// they cannot be embedded in the project file.
    /// Default: false
    /// </summary>
    public bool ProjectPersistence { get; set; } = false;

    /// <summary>
    /// Enable magnetic snapping when dragging clips onto the timeline.
    /// When true, drop positions snap to the nearest timeline marker and clip
    /// start/end edges within <see cref="SnapThresholdSeconds"/>.
    /// Default: true
    /// </summary>
    public bool Snapping { get; set; } = true;

    /// <summary>
    /// The maximum distance in seconds within which a drag position snaps to a
    /// nearby marker or clip edge. Ignored when <see cref="Snapping"/> is false.
    /// Default: 0.5
    /// </summary>
    public double SnapThresholdSeconds { get; set; } = 0.5;

    /// <summary>
    /// Initial zoom scale applied to the timeline ruler when the editor loads.
    /// A value of 1.0 means fit-to-view (80 px/s); values up to 10 expand the ruler.
    /// Default: 1.0
    /// </summary>
    public double DefaultTimelineZoom { get; set; } = 1.0;

    /// <summary>
    /// Default label style shown on the timeline ruler ticks.
    /// Default: <see cref="Models.TimelineDisplayMode.Time"/> (HH:MM:SS timecode).
    /// </summary>
    public TimelineDisplayMode TimelineDisplayMode { get; set; } = TimelineDisplayMode.Time;

    /// <summary>
    /// Show the Export Error Log item in the File menu so users can download a
    /// plain-text log of ffmpeg and JS interop errors encountered in this session.
    /// Default: false
    /// </summary>
    public bool ErrorLog { get; set; } = false;

    /// <summary>
    /// Enable ripple editing on the timeline.
    /// When <c>true</c>, removing a clip automatically shifts all subsequent clips on the
    /// same track left to close the gap, and dragging a clip to a new position pushes
    /// (or pulls) all subsequent clips by the same delta.
    /// Default: false
    /// </summary>
    public bool RippleEdit { get; set; } = false;

    /// <summary>
    /// Enable alpha-channel compositing when stacking video tracks.
    /// When <c>true</c>, video inputs are decoded in <c>yuva420p</c> pixel format
    /// so that transparent regions in upper tracks reveal the tracks beneath them.
    /// When <c>false</c>, standard <c>overlay</c> compositing is used (alpha ignored).
    /// Only relevant when <see cref="MultiTrack"/> is <c>true</c> and more than one
    /// video track contains clips.
    /// Default: false
    /// </summary>
    public bool AlphaCompositing { get; set; } = false;

    // ── Asset catalog (Phase 49+) ─────────────────────────────────────────────

    /// <summary>
    /// Base URL of the Ben app's shared asset catalog API, e.g. <c>"https://api.averageben.com"</c>.
    /// When set, <see cref="Ben.Video.Editor.Services.SharedCatalogAssetProvider"/> becomes active
    /// and the asset browser shows clipart, callouts, and shapes from the catalog.
    /// Leave null to disable the shared catalog (local and account-library assets still shown).
    /// </summary>
    public string? AssetCatalogUrl { get; set; }

    /// <summary>
    /// Named <see cref="System.Net.Http.HttpClient"/> used for all asset catalog and
    /// watermark API calls. Defaults to <c>"BenVideo.AssetCatalog"</c>.
    /// The host can attach auth handlers by this name:
    /// <code>
    /// builder.Services.AddHttpClient(ServiceCollectionExtensions.AssetCatalogHttpClientName)
    ///                 .AddHttpMessageHandler&lt;YourAuthHandler&gt;();
    /// </code>
    /// </summary>
    public string AssetCatalogHttpClientName { get; set; } = "BenVideo.AssetCatalog";

    // ── Background rendering (item #36 phase C) ───────────────────────────────

    /// <summary>
    /// Enable the background render worker: a second ffmpeg.wasm instance that renders stale
    /// preview regions ahead of time (nearest the playhead first), independently of the main
    /// instance Export/Preview use, so Preview can assemble from already-rendered segments
    /// instead of encoding synchronously when clicked. Opt-in — default off pending a wider
    /// rollout once phase D/E are further along.
    /// Default: false
    /// </summary>
    public bool BackgroundRendering { get; set; } = false;

    /// <summary>
    /// When <see cref="BackgroundRendering"/> is on, pause starting new background-render jobs
    /// while the main ffmpeg instance is actively exporting — the two would otherwise compete
    /// for the same CPU on the user's machine, and Export is what the user is actively waiting on.
    /// An already-started background job still finishes; only new job starts are held.
    /// Default: true
    /// </summary>
    public bool PauseBackgroundRenderDuringExport { get; set; } = true;

    /// <summary>
    /// When <see cref="BackgroundRendering"/> is on, whether the background worker renders a fast
    /// ROUGH pass before its FINE pass for each stale region (item #36 phase D), or goes straight
    /// to FINE. Rough-first makes the whole timeline playable sooner at lower quality, then
    /// sharpens; disabling it trades that early-availability window for fewer total encodes per
    /// region. Applied to <c>BackgroundRenderService.EnableRoughPass</c> (mutable at runtime,
    /// matching Pause/Resume) whenever <see cref="BackgroundRendering"/> is (re)enabled.
    /// Default: true
    /// </summary>
    public bool EnableRoughPass { get; set; } = true;

    /// <summary>
    /// When <see cref="BackgroundRendering"/> is on, the soft cap (in MB) on how many bytes of
    /// background-rendered segments stay resident in the main ffmpeg instance's MEMFS at once
    /// (item #36 design doc §8 / item #38 phase C). Enforced by evicting least-recently-touched
    /// segments back to <c>RenderRegionState.Stale</c> — never the region under the current
    /// playhead, never a region mid-render. Applied to
    /// <c>BackgroundRenderService.SegmentCapBytes</c> (mutable at runtime, matching
    /// <see cref="EnableRoughPass"/>) whenever <see cref="BackgroundRendering"/> is (re)enabled.
    /// Default: 256.
    /// </summary>
    public int BackgroundRenderMemoryCapMb { get; set; } = 256;

    // ── Native sidecar (item #38 phases E-G) ──────────────────────────────────

    /// <summary>
    /// Enable probing for a local native ffmpeg sidecar process (a separate, user-installed
    /// companion app — see DESIGN-item38-long-form-memory.md §5) that can render segments and
    /// exports faster than ffmpeg.wasm's single-threaded core. Opt-in and default off: even when
    /// true, nothing talks to the sidecar until the user pastes its one-time pairing code into
    /// the "Native acceleration" panel — this flag only controls whether the editor probes for
    /// one and shows that panel at all.
    /// Default: false
    /// </summary>
    public bool NativeSidecar { get; set; } = false;

    /// <summary>
    /// Where the sidecar can be downloaded from, so the panel that asks for it can offer it.
    /// </summary>
    /// <remarks>
    /// <para>The panel said "Download and run it" and gave nobody anything to click. The downloads
    /// page existed the whole time, one level down from the standalone editor, and nothing in the
    /// editor linked to it — so the instruction was an instruction to go and find something
    /// (2026-09-05 audit, F17).</para>
    ///
    /// <para>A host setting rather than a constant, because the two hosts reach it differently:
    /// the site is at a site-absolute path, the standalone editor at one relative to its own
    /// <c>&lt;base&gt;</c>. Null hides the link, which is right for a host that ships no sidecar.
    /// </para>
    /// </remarks>
    public string? SidecarDownloadUrl { get; set; }

    /// <summary>
    /// Whether to show the operator tools — the ffmpeg diagnostics chip and the panel behind it
    /// (MEMFS residency, worker state, the raw ffmpeg log).
    /// </summary>
    /// <remarks>
    /// Off by default, deliberately. These are for whoever runs the platform, not for someone
    /// editing their own footage: the panel reports internals, names files and commands, and
    /// offers a worker reset that is meaningless to anyone else. The editor has no notion of
    /// roles — it is a component library — so each host sets this from the identity it already
    /// holds. Defaulting to off means a host that never thinks about it stays quiet rather than
    /// exposing its plumbing.
    /// </remarks>
    public bool ShowDiagnostics { get; set; } = false;
}
