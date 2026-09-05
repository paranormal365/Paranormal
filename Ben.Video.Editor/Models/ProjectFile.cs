using System.Text.Json.Serialization;
using Ben.Video.Editor.Models.Assets;

namespace Ben.Video.Editor.Models;

/// <summary>
/// The root DTO written to / read from a <c>.benvideo</c> project file.
/// </summary>
public sealed class ProjectFile
{
    /// <summary>
    /// The format this editor writes.
    /// </summary>
    /// <remarks>
    /// Version 2 added the media bin. A file's own version is what
    /// <see cref="Ben.Video.Editor.Services.ProjectFileMigrations"/> reads to decide what it needs,
    /// and what tells a reader that a file came from a newer editor than itself — which used to
    /// open silently and half-work.
    /// </remarks>
    public const int CurrentSchemaVersion = 2;

    /// <summary>Format version — bump when breaking changes are made to this schema.</summary>
    public int SchemaVersion { get; set; } = CurrentSchemaVersion;

    /// <summary>UTC timestamp when the project was first created.</summary>
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>UTC timestamp of the most recent save.</summary>
    public DateTime SavedAt { get; set; } = DateTime.UtcNow;

    /// <summary>Human-readable project name (defaults to the filename without extension).</summary>
    public string ProjectName { get; set; } = "Untitled Project";

    /// <summary>
    /// Snapshot of the feature flags active when the project was saved.
    /// Restored on open so the editor re-enables the same optional panels.
    /// </summary>
    public ProjectOptionsSnapshot Options { get; set; } = new();

    /// <summary>All timeline tracks (video and audio) in their saved order.</summary>
    public List<ProjectTrack> Tracks { get; set; } = [];

    /// <summary>Named cue points on the timeline ruler.</summary>
    public List<TimelineMarker>    Markers      { get; set; } = [];
    public List<ProjectMotionPath> MotionPaths  { get; set; } = [];

    /// <summary>
    /// The media brought into this project, whether or not any of it is on the timeline.
    /// </summary>
    /// <remarks>
    /// Absent from files written before the media bin existed, which is why it defaults to empty
    /// rather than being required: an older project simply has nothing unplaced, and opening it
    /// fills the bin from what is on its timeline.
    /// </remarks>
    public ProjectMediaBin Bin { get; set; } = new();
}

/// <summary>The media bin's contents, by kind.</summary>
/// <remarks>
/// Three lists rather than one polymorphic one: System.Text.Json needs a discriminator to round-trip
/// a mixed list, and the rest of this file already keeps clips apart by kind for the same reason.
/// </remarks>
public sealed class ProjectMediaBin
{
    public List<ProjectVideoClip> VideoClips { get; set; } = [];
    public List<ProjectAudioClip> AudioClips { get; set; } = [];
    public List<ProjectImageClip> ImageClips { get; set; } = [];

    /// <summary>True when there is nothing to restore — an older file, or an empty project.</summary>
    public bool IsEmpty => VideoClips.Count == 0 && AudioClips.Count == 0 && ImageClips.Count == 0;
}

/// <summary>
/// Persisted subset of <see cref="VideoEditorOptions"/> — only flags that affect
/// timeline structure or rendering are restored; host-level config (URLs, etc.) is not.
/// </summary>
public sealed class ProjectOptionsSnapshot
{
    public bool MultiTrack   { get; set; }
    public bool AudioTracks  { get; set; }
    public bool Transitions  { get; set; }
    public bool TextOverlays { get; set; }
    public bool VideoEffects { get; set; }
    public bool Markers      { get; set; } = true;
    public bool InlineTrimming { get; set; }
}

/// <summary>
/// Serialized form of a <see cref="TimelineTrack"/>.
/// Items are stored as concrete typed DTOs so STJ can round-trip them without
/// the polymorphism overhead of <see cref="TrackItem"/>.
/// </summary>
public sealed class ProjectTrack
{
    public Guid      Id       { get; set; }
    public string    Label    { get; set; } = string.Empty;
    public TrackType Type     { get; set; }
    public int       Order    { get; set; }
    public bool      IsMuted  { get; set; }
    public bool      IsLocked { get; set; }

    public List<ProjectVideoClip>    VideoClips    { get; set; } = [];
    public List<ProjectAudioClip>    AudioClips    { get; set; } = [];
    public List<ProjectImageClip>    ImageClips    { get; set; } = [];
    public List<ProjectCalloutClip>  CalloutClips  { get; set; } = [];
    public List<ProjectClipArtClip>  ClipArtClips  { get; set; } = [];
    public List<ProjectTransition>   Transitions   { get; set; } = [];
    public List<ProjectTextOverlay>  TextOverlays  { get; set; } = [];
}

/// <summary>Serialized <see cref="VideoClip"/>.</summary>
public sealed class ProjectVideoClip
{
    public Guid   Id               { get; set; }

    /// <summary>The media-bin entry this was placed from, when it was.</summary>
    public Guid?  SourceBinId      { get; set; }
    public string Name             { get; set; } = string.Empty;
    public double TimelinePosition { get; set; }
    public double Duration         { get; set; }
    public int    Order            { get; set; }

    // Trim / speed
    public double StartTrim        { get; set; }
    public double EndTrim          { get; set; }
    public double Speed            { get; set; } = 1.0;

    // Dimensions
    public int    Width            { get; set; }
    public int    Height           { get; set; }

    // Audio
    public double Volume           { get; set; } = 1.0;
    public List<VolumeKeyframe> VolumeAutomation { get; set; } = [];

    /// <summary>
    /// Whether this clip's own sound is silenced, and whether it has any.
    /// </summary>
    /// <remarks>
    /// Neither was saved, so "Separate Audio" — which mutes the clip and puts its sound on its own
    /// track — came back with the picture unmuted and the audio track playing as well, doubling
    /// every word (2026-09-05 audit, audio-7).
    /// </remarks>
    public bool   MuteAudio        { get; set; }
    public bool   HasAudio         { get; set; } = true;

    /// <summary>The clip this one is tied to, so moving one moves the other.</summary>
    public Guid?  LinkedClipId     { get; set; }

    // Effects
    public ClipEffects Effects     { get; set; } = new();
    public List<ProjectAppliedEffect> AppliedEffects { get; set; } = [];

    /// <summary>
    /// Set to <c>true</c> on load; cleared to <c>false</c> once the user re-links
    /// the source file via ClipBrowser and the file is written to ffmpeg MEMFS.
    /// </summary>
    public bool IsMediaMissing     { get; set; } = true;

    /// <summary>
    /// The original file name (without path) used as a hint when the user re-links.
    /// </summary>
    public string? OriginalFileName { get; set; }
    public string? OpfsExt          { get; set; }

    /// <summary>
    /// Where the media came from, so it can be fetched again on another machine.
    /// </summary>
    /// <remarks>
    /// The three together are what makes a project portable: the id says which server file, and
    /// the size and hash say whether what came back is the same file (2026-09-05 audit, F14). The
    /// hash is null above the size ceiling — see <c>MediaFingerprint</c>.
    /// </remarks>
    public Guid?   SourceFileId      { get; set; }
    public long?   SourceFileSize    { get; set; }
    public string? SourceContentHash { get; set; }
}

/// <summary>Serialized <see cref="AudioClip"/>.</summary>
public sealed class ProjectAudioClip
{
    public Guid   Id               { get; set; }

    /// <summary>The media-bin entry this was placed from, when it was.</summary>
    public Guid?  SourceBinId      { get; set; }
    public string Name             { get; set; } = string.Empty;
    public double TimelinePosition { get; set; }
    public double Duration         { get; set; }
    public int    Order            { get; set; }

    public double StartTrim        { get; set; }
    public double EndTrim          { get; set; }
    public double Volume           { get; set; } = 1.0;
    public double FadeInSeconds    { get; set; }
    public double FadeOutSeconds   { get; set; }
    public List<VolumeKeyframe> VolumeAutomation { get; set; } = [];
    public double LeftVolume       { get; set; } = 1.0;
    public double RightVolume      { get; set; } = 1.0;

    /// <summary>The picture this sound belongs with. The other half of the link.</summary>
    public Guid?  LinkedClipId     { get; set; }

    public bool   IsMediaMissing   { get; set; } = true;
    public string? OriginalFileName { get; set; }
    public string? OpfsExt          { get; set; }

    /// <summary>
    /// Where the media came from, so it can be fetched again on another machine.
    /// </summary>
    /// <remarks>
    /// The three together are what makes a project portable: the id says which server file, and
    /// the size and hash say whether what came back is the same file (2026-09-05 audit, F14). The
    /// hash is null above the size ceiling — see <c>MediaFingerprint</c>.
    /// </remarks>
    public Guid?   SourceFileId      { get; set; }
    public long?   SourceFileSize    { get; set; }
    public string? SourceContentHash { get; set; }
}

/// <summary>Serialized <see cref="Transition"/>.</summary>
public sealed class ProjectTransition
{
    public Guid             Id               { get; set; }
    public string           Name             { get; set; } = string.Empty;
    public double           TimelinePosition { get; set; }
    public double           Duration         { get; set; }
    public int              Order            { get; set; }
    public TransitionStyle  Style            { get; set; }
    public Guid             FromClipId       { get; set; }
    public Guid             ToClipId         { get; set; }
}

/// <summary>Serialized <see cref="TextRun"/> (item #16).</summary>
public sealed class ProjectTextRun
{
    public string  Text        { get; set; } = string.Empty;
    public bool    Bold        { get; set; }
    public bool    Underline   { get; set; }
    public bool    Subscript   { get; set; }
    public bool    Superscript { get; set; }
    public string? Color       { get; set; }
}

/// <summary>Serialized <see cref="TextOverlay"/>.</summary>
public sealed class ProjectTextOverlay
{
    public Guid   Id               { get; set; }
    public string Name             { get; set; } = string.Empty;
    public double TimelinePosition { get; set; }
    public double Duration         { get; set; }
    public int    Order            { get; set; }
    public int    LayerIndex       { get; set; }

    public string Text             { get; set; } = string.Empty;
    public string FontFamily       { get; set; } = "Arial";
    public int    FontSize         { get; set; } = 36;
    public string FontColor        { get; set; } = "#ffffff";
    // FontBold/FontUnderline (item #16 phase 111) and Runs (item #16 phase 115) — found missing
    // from this DTO entirely during phase 115: FontBold/FontUnderline were shipped in phase 111
    // but never wired into serialization, so they were silently lost on every project save/reload.
    public bool    FontBold         { get; set; }
    public bool    FontUnderline    { get; set; }
    public List<ProjectTextRun>? Runs { get; set; }
    public bool   HasBackground    { get; set; }
    public string BackgroundColor  { get; set; } = "#000000";
    public double BackgroundOpacity { get; set; } = 0.5;
    public string HorizontalAlign  { get; set; } = "center";
    public string VerticalAlign    { get; set; } = "bottom";
    public int    OffsetX          { get; set; }
    public int    OffsetY          { get; set; }
    public double? OverrideX       { get; set; }
    public double? OverrideY       { get; set; }
    public double FadeInSeconds    { get; set; }
    public double FadeOutSeconds   { get; set; }
    public double Opacity          { get; set; } = 1.0;

    /// <summary>
    /// The widest the title may draw, as a fraction of the canvas. Null means no limit.
    /// </summary>
    public double? MaxWidth        { get; set; }

    // Shadow (packed ARGB double via ColorHelper)
    public double ShadowColor      { get; set; }
    public double ShadowOffsetX    { get; set; } = 3.0;
    public double ShadowOffsetY    { get; set; } = 3.0;
    public double ShadowBlur       { get; set; } = 4.0;
}

/// <summary>Serialized <see cref="ImageClip"/>.</summary>
public sealed class ProjectImageClip
{
    public Guid   Id               { get; set; }

    /// <summary>The media-bin entry this was placed from, when it was.</summary>
    public Guid?  SourceBinId      { get; set; }
    public string Name             { get; set; } = string.Empty;
    public double TimelinePosition { get; set; }
    public double Duration         { get; set; }
    public int    Order            { get; set; }
    public int    Width            { get; set; }
    public int    Height           { get; set; }
    public ClipEffects Effects     { get; set; } = new();
    public List<ProjectAppliedEffect> AppliedEffects { get; set; } = [];
    public bool   IsMediaMissing   { get; set; } = true;
    public string? OriginalFileName { get; set; }
    public string? OpfsExt          { get; set; }

    /// <summary>
    /// Where the media came from, so it can be fetched again on another machine.
    /// </summary>
    /// <remarks>
    /// The three together are what makes a project portable: the id says which server file, and
    /// the size and hash say whether what came back is the same file (2026-09-05 audit, F14). The
    /// hash is null above the size ceiling — see <c>MediaFingerprint</c>.
    /// </remarks>
    public Guid?   SourceFileId      { get; set; }
    public long?   SourceFileSize    { get; set; }
    public string? SourceContentHash { get; set; }
}

/// <summary>Serialized <see cref="AppliedEffect"/></summary> — effect id + parameter snapshot.</summary>
public sealed class ProjectAppliedEffect
{
    public string EffectId                          { get; set; } = string.Empty;
    public Dictionary<string, double> Parameters   { get; set; } = [];
}
/// <summary>Serialized <see cref="MotionKeyframe"/>.</summary>
public sealed class ProjectKeyframe
{
    public double  Time       { get; set; }
    public double  X          { get; set; } = 0.5;
    public double  Y          { get; set; } = 0.5;
    public double  Scale      { get; set; } = 1.0;

    /// <summary>
    /// Per-axis scale, and rotation. Null means "whatever <see cref="Scale"/> says", which is how
    /// a keyframe written before these existed reads.
    /// </summary>
    /// <remarks>
    /// These were on the keyframe and not in this DTO, so stretching a layer on one axis or
    /// rotating it looked right until the project was saved and opened again, at which point the
    /// animation came back uniform and upright (2026-09-05 audit, motion-1).
    /// </remarks>
    public double? ScaleX     { get; set; }
    public double? ScaleY     { get; set; }
    public double? Rotation   { get; set; }

    public double  Alpha      { get; set; } = 1.0;
    public string  Easing     { get; set; } = "Linear";
    public double? HandleOutX { get; set; }
    public double? HandleOutY { get; set; }
    public double? HandleInX  { get; set; }
    public double? HandleInY  { get; set; }

    // Callout appearance (packed ARGB doubles via ColorHelper)
    public double  FillColor   { get; set; }
    public double  StrokeColor { get; set; }
    public Dictionary<string, double> ControlPointValues { get; set; } = [];

    // Shadow (Callout + TextOverlay layers)
    public double  ShadowColor   { get; set; }
    public double  ShadowOffsetX { get; set; } = 3.0;
    public double  ShadowOffsetY { get; set; } = 3.0;
    public double  ShadowBlur    { get; set; } = 4.0;
}

/// <summary>Serialized <see cref="MotionPath"/>.</summary>
public sealed class ProjectMotionPath
{
    public Guid                  Id        { get; set; } = Guid.NewGuid();
    public Guid                  LayerId   { get; set; }
    public string                LayerType { get; set; } = string.Empty;
    public List<ProjectKeyframe> Keyframes { get; set; } = [];
}/// <summary>Serialized <see cref="CalloutClip"/>.</summary>
public sealed class ProjectCalloutClip
{
    public Guid     Id             { get; set; }
    public string   Name           { get; set; } = string.Empty;
    public double   TimelinePosition { get; set; }
    public double   Duration       { get; set; }
    public int      Order          { get; set; }
    public int      LayerIndex     { get; set; }

    // Shape
    public ShapeType Shape         { get; set; } = ShapeType.Rectangle;

    // Geometry (canvas fractions)
    public double   X              { get; set; } = 0.1;
    public double   Y              { get; set; } = 0.1;
    public double   Width          { get; set; } = 0.2;
    public double   Height         { get; set; } = 0.15;
    public double   Rotation       { get; set; }

    // Appearance (packed ARGB doubles via ColorHelper)
    public double   FillColor      { get; set; }
    public double   StrokeColor    { get; set; }
    public double   StrokeWidth    { get; set; } = 2.0;
    public double   Opacity        { get; set; } = 1.0;

    // Shadow
    public double   ShadowColor    { get; set; }
    public double   ShadowOffsetX  { get; set; } = 3.0;
    public double   ShadowOffsetY  { get; set; } = 3.0;
    public double   ShadowBlur     { get; set; } = 4.0;

    // Text (optional label)
    public string?  Text           { get; set; }
    public string   FontFamily     { get; set; } = "Arial";
    public int      FontSize       { get; set; } = 28;
    public double   FontColor      { get; set; }
    // FontBold/FontUnderline (item #16 phase 111) and Runs (item #16 phase 115) — same
    // found-missing-from-this-DTO gap as ProjectTextOverlay above; fixed in the same pass.
    public bool     FontBold       { get; set; }
    public bool     FontUnderline  { get; set; }
    public List<ProjectTextRun>? Runs { get; set; }

    /// <summary>
    /// How the label sits inside the shape: its alignment, whether it wraps, whether it has a
    /// shadow of its own, and how far it stands off the edge.
    /// </summary>
    /// <remarks>
    /// None of it was saved. A callout laid out carefully came back centred, unwrapped and with
    /// default padding — the shape survived and everything about the words in it did not
    /// (2026-09-05 audit, callouts-1).
    /// </remarks>
    public TextHorizontalAlign TextAlign         { get; set; } = TextHorizontalAlign.Center;
    public TextVerticalAlign   TextVerticalAlign { get; set; } = TextVerticalAlign.Middle;
    public bool                TextWrap          { get; set; }
    public bool                TextShadow        { get; set; }
    public double              TextPadding       { get; set; } = 8.0;

    // Fade
    public double   FadeInSeconds  { get; set; }
    public double   FadeOutSeconds { get; set; }

    // Custom asset
    public string?  OpfsAssetName  { get; set; }
    public bool     AssetMissing   { get; set; }

    // OPFS reference
    public string?  OpfsExt        { get; set; }

    // SVG control points (Arrow/Line Bezier, Star radii, Rectangle corner radius)
    public Dictionary<string, double> ControlPointValues { get; set; } = [];
}

/// <summary>Serialized <see cref="ClipArtClip"/>.</summary>
public sealed class ProjectClipArtClip
{
    public Guid     Id               { get; set; }
    public string   Name             { get; set; } = string.Empty;
    public double   TimelinePosition { get; set; }
    public double   Duration         { get; set; }
    public int      Order            { get; set; }
    public int      LayerIndex       { get; set; }

    // Asset identity
    public string       AssetId     { get; set; } = string.Empty;
    public AssetSource  AssetSource { get; set; }
    public VideoAssetFormat AssetFormat { get; set; }

    // Geometry
    public double X        { get; set; } = 0.1;
    public double Y        { get; set; } = 0.1;
    public double Width    { get; set; } = 0.2;
    public double Height   { get; set; } = -1.0;
    public double Rotation { get; set; }
    public double Opacity  { get; set; } = 1.0;
    public double? TintColor { get; set; }

    // Control-point state
    public Dictionary<string, double> ControlPointValues { get; set; } = [];
    public Dictionary<string, string> ControlPointColors { get; set; } = [];

    // Settings snapshot
    public bool SettingsAllowRecolor       { get; set; }
    public bool SettingsAllowResize        { get; set; }
    public bool SettingsAllowOpacity       { get; set; }
    public bool SettingsAllowRotation      { get; set; }
    public bool SettingsAllowEffects       { get; set; }
    public bool SettingsAllowEasing        { get; set; }
    public bool SettingsAllowMotion        { get; set; }
    public bool SettingsAllowControlPoints { get; set; }
}