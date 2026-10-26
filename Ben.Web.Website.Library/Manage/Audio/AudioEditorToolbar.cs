using Ben.Data.Common.Enums;

namespace Ben.Web.Website.Library.Manage.Audio;

/// <summary>What the audio editor's toolbar shows: its pressed buttons, counts and what is allowed.</summary>
/// <remarks>The toolbar keeps no state of its own. The editor owns every field, and redraws the bar
/// from a fresh one of these each render.</remarks>
public sealed record AudioToolbarState(
    bool Ready,
    bool Scrub,
    bool Spectrogram,
    bool SpectrogramLabels,
    bool MelScale,
    bool VoiceBand,
    bool Timeline,
    bool DetectSilence,
    bool Scanning,
    EvpSensitivity Sensitivity,
    int MarkerCount,
    int ClipCount,
    double Speed,
    bool CanEdit,
    bool HasSelection,
    string ActiveTab,
    bool InspectorOpen,
    bool CustomScan = false);   // the scan settings were adjusted away from Low, Medium and High

/// <summary>Everything the toolbar can ask the editor to do.</summary>
public enum AudioToolbarAction
{
    // Selection
    ToggleScrub,
    ClearRegions,

    // Analyze
    ToggleSpectrogram,
    Scan,
    ScanLow,
    ScanMedium,
    ScanHigh,
    FineTune,
    Markers,

    // Zoom
    ZoomOut,
    ZoomFit,
    ZoomIn,

    // View ▾
    ToggleTimeline,
    ToggleSpectrogramLabels,
    ToggleMelScale,
    ToggleVoiceBand,
    ToggleDetectSilence,
    DisplaySettings,

    // Listen ▾ (the speeds come through OnSpeed)
    ListenSettings,

    // Edit ▾
    MakeClip,
    ExploreSelection,
    CutSelection,
    SilenceSelection,
    Normalize,
    Reverse,
    Gain,
    Fade,
    ChangeSpeed,
    ChangePitch,

    // Clips
    Clips,
}
