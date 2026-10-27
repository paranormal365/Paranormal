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
    int ScanPercent,
    EvpSensitivity Sensitivity,
    int MarkerCount,
    int ClipCount,
    double Speed,
    bool CanEdit,
    bool HasSelection,
    string ActiveTab,
    bool InspectorOpen,
    bool CustomScan = false,    // the scan settings were adjusted away from Low, Medium and High
    bool Loop = false,          // phase 7: what is played loops instead of stopping at its end
    bool Minimap = false,
    bool VolumeCurve = false,   // the volume curve is being drawn
    bool LevelLane = false,     // phase 8: the lanes under the waveform, and hearing the original
    bool DetectorLane = false,
    bool CompareOriginal = false);

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

    // Zoom, and playing (phase 7)
    ZoomOut,
    ZoomFit,
    ZoomIn,
    ZoomSelection,
    ToggleLoop,

    // View ▾
    ToggleTimeline,
    ToggleSpectrogramLabels,
    ToggleMelScale,
    ToggleVoiceBand,
    ToggleDetectSilence,
    ToggleMinimap,
    ToggleLevelLane,
    ToggleDetectorLane,
    DisplaySettings,

    // Listen ▾ (the speeds come through OnSpeed)
    ListenSettings,
    ToggleCompare,

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
    DrawVolume,

    // Clips, and the keyboard
    Clips,
    Shortcuts,
}
