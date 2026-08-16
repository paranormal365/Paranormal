using System.Text.Json.Serialization;
using Ben.Video.Editor.Models.Assets;

namespace Ben.Video.Editor.Models;

/// <summary>
/// The root DTO written to / read from a <c>.benvideo</c> project file.
/// </summary>
public sealed class ProjectFile
{
    /// <summary>Format version — bump when breaking changes are made to this schema.</summary>
    public int SchemaVersion { get; set; } = 1;

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
}

/// <summary>Serialized <see cref="AudioClip"/>.</summary>
public sealed class ProjectAudioClip
{
    public Guid   Id               { get; set; }
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

    public bool   IsMediaMissing   { get; set; } = true;
    public string? OriginalFileName { get; set; }
    public string? OpfsExt          { get; set; }
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