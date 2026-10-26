using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace Ben.Web.Website.Library.Manage.Audio;

/// <summary>
/// How the editor was left for a recording, beyond the spectrogram and the listening chain: zoom,
/// speed, the voice band, the drag mode, silence detection, the open tab, and the spectrogram's
/// Display-tab dials.
/// </summary>
/// <remarks>
/// <para>Phase 3 of the 10/26/2026 plan. The 09/06 audit found every one of these reset on each open,
/// and phase 2 added more (the tabs, the WaveSurfer 8 dials).</para>
///
/// <para><b>It rides in the same EditStateJson as the listening chain, under "workspace".</b> No
/// migration, and a row written before this existed simply has no "workspace" and gets the
/// defaults. <see cref="AudioListeningChain.FromJson"/> ignores the key, and
/// <see cref="Compose"/> puts it back beside the chain on every save. The chain's ToJson builds a
/// fresh object, so anything written beside it any other way would be lost on the next save.</para>
///
/// <para>Read field by field and range-checked like the chain: a stored NaN zoom or speed would leave
/// the waveform blank or silent with nothing to say why.</para>
/// </remarks>
public sealed record AudioWorkspace
{
    /// <summary>Pixels per second; 0 means fit the whole recording.</summary>
    public double ZoomPxPerSec { get; init; }
    public double Speed { get; init; } = 1.0;
    public bool VoiceBand { get; init; }
    public bool DragScrub { get; init; }
    public bool DetectSilence { get; init; }
    public bool InspectorOpen { get; init; } = true;
    public string InspectorTab { get; init; } = "markers";
    public SpectrogramAnalysis Analysis { get; init; } = SpectrogramAnalysis.Default;

    /// <summary>The whole-recording strip under the waveform (phase 7).</summary>
    public bool Minimap { get; init; }

    /// <summary>The spectrogram's height in px, 100–480 (phase 7).</summary>
    public int SpectrogramHeight { get; init; } = 160;

    /// <summary>The level and detector lanes under the waveform (phase 8).</summary>
    public bool LevelLane { get; init; }
    public bool DetectorLane { get; init; }

    public static AudioWorkspace Default { get; } = new();

    /// <summary>The tabs the editor has; anything else stored falls back to the first.</summary>
    public static readonly string[] Tabs = ["markers", "clips", "listen", "display", "edit"];

    private static readonly string[] Scales = ["linear", "logarithmic", "mel", "bark", "erb"];
    private static readonly string[] Windows =
        ["hann", "hamming", "blackman", "bartlett", "cosine", "gauss", "lanczoz", "rectangular", "triangular"];

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    /// <summary>Reads the "workspace" part of an EditStateJson. Anything missing or unreadable is the default.</summary>
    public static AudioWorkspace FromEditState(string? editStateJson)
    {
        if (string.IsNullOrWhiteSpace(editStateJson)) return Default;

        Stored? stored;
        try
        {
            var node = JsonNode.Parse(editStateJson)?["workspace"];
            stored = node?.Deserialize<Stored>(Json);
        }
        catch (Exception e) when (e is JsonException or InvalidOperationException) { return Default; }

        if (stored is null) return Default;

        var a = SpectrogramAnalysis.Default;
        return new AudioWorkspace
        {
            ZoomPxPerSec  = Clamp(stored.ZoomPxPerSec, Default.ZoomPxPerSec, 0, 2000),
            Speed         = Clamp(stored.Speed, Default.Speed, 0.25, 3),
            VoiceBand     = stored.VoiceBand ?? Default.VoiceBand,
            DragScrub     = stored.DragScrub ?? Default.DragScrub,
            DetectSilence = stored.DetectSilence ?? Default.DetectSilence,
            InspectorOpen = stored.InspectorOpen ?? Default.InspectorOpen,
            InspectorTab  = stored.InspectorTab is { } t && Tabs.Contains(t) ? t : Default.InspectorTab,
            Minimap       = stored.Minimap ?? Default.Minimap,
            SpectrogramHeight = (int)Clamp(stored.SpectrogramHeight, Default.SpectrogramHeight, 100, 480),
            LevelLane     = stored.LevelLane ?? Default.LevelLane,
            DetectorLane  = stored.DetectorLane ?? Default.DetectorLane,
            Analysis = new SpectrogramAnalysis(
                Scale:         stored.Scale is { } s && Scales.Contains(s) ? s : a.Scale,
                FrequencyMin:  Clamp(stored.FrequencyMin, a.FrequencyMin, 0, 24_000),
                FrequencyMax:  Clamp(stored.FrequencyMax, a.FrequencyMax, 0, 24_000),
                WindowFunc:    stored.WindowFunc is { } w && Windows.Contains(w) ? w : a.WindowFunc,
                GainDb:        Clamp(stored.GainDb, a.GainDb, 0, 60),
                RangeDb:       Clamp(stored.RangeDb, a.RangeDb, 1, 120),
                PreEmphasis:   Clamp(stored.PreEmphasis, a.PreEmphasis, 0, 12),
                SplitChannels: stored.SplitChannels ?? a.SplitChannels),
        };
    }

    /// <summary>The listening chain's JSON with this workspace beside it, as EditStateJson.</summary>
    public string Compose(string chainJson)
    {
        JsonObject root;
        try { root = JsonNode.Parse(chainJson) as JsonObject ?? new JsonObject(); }
        catch (JsonException) { root = new JsonObject(); }

        root["workspace"] = JsonSerializer.SerializeToNode(new Stored
        {
            ZoomPxPerSec  = ZoomPxPerSec,
            Speed         = Speed,
            VoiceBand     = VoiceBand,
            DragScrub     = DragScrub,
            DetectSilence = DetectSilence,
            InspectorOpen = InspectorOpen,
            InspectorTab  = InspectorTab,
            Minimap       = Minimap,
            SpectrogramHeight = SpectrogramHeight,
            LevelLane     = LevelLane,
            DetectorLane  = DetectorLane,
            Scale         = Analysis.Scale,
            FrequencyMin  = Analysis.FrequencyMin,
            FrequencyMax  = Analysis.FrequencyMax,
            WindowFunc    = Analysis.WindowFunc,
            GainDb        = Analysis.GainDb,
            RangeDb       = Analysis.RangeDb,
            PreEmphasis   = Analysis.PreEmphasis,
            SplitChannels = Analysis.SplitChannels,
        }, Json);

        return root.ToJsonString(Json);
    }

    private static double Clamp(double? value, double fallback, double min, double max)
        => value is { } v && double.IsFinite(v) ? Math.Clamp(v, min, max) : fallback;

    /// <summary>The wire shape: every field nullable, so "absent" and "zero" stay different.</summary>
    private sealed record Stored
    {
        public double? ZoomPxPerSec { get; init; }
        public double? Speed { get; init; }
        public bool? VoiceBand { get; init; }
        public bool? DragScrub { get; init; }
        public bool? DetectSilence { get; init; }
        public bool? InspectorOpen { get; init; }
        public string? InspectorTab { get; init; }
        public bool? Minimap { get; init; }
        public double? SpectrogramHeight { get; init; }
        public bool? LevelLane { get; init; }
        public bool? DetectorLane { get; init; }
        public string? Scale { get; init; }
        public double? FrequencyMin { get; init; }
        public double? FrequencyMax { get; init; }
        public string? WindowFunc { get; init; }
        public double? GainDb { get; init; }
        public double? RangeDb { get; init; }
        public double? PreEmphasis { get; init; }
        public bool? SplitChannels { get; init; }
    }
}
