using System.Text.Json;
using System.Text.Json.Serialization;

namespace Ben.Video.Editor.Services;

/// <summary>
/// Scoped service that owns the editor's layout: the height of the timeline, the width and state
/// of the side panel, and which of its tabs is showing.
/// </summary>
/// <remarks>
/// <para>Components subscribe to <see cref="OnChanged"/> to re-render when layout mutates, and
/// <see cref="VideoEditor"/> writes the sizes onto <c>.bv-editor</c> as CSS custom properties.</para>
///
/// <para><b>The preview has no height of its own any more.</b> It used to, and the timeline sat
/// beside it with <c>height: 100%</c>, which in a column flexbox gives the timeline a flex-basis of
/// the entire editor: the two then shrank in proportion to those bases, so the preview lost about a
/// third of whatever height it was given and ended up a 38-pixel strip under 700 pixels of empty
/// timeline. The preview now takes the space that is left, which is also the arrangement every
/// video editor uses — the timeline is the thing with a size, and the picture gets the rest
/// (2026-09-05 audit, F4).</para>
/// </remarks>
public sealed class LayoutService
{
    // ── sizes (CSS-custom-property values written onto .bv-editor) ───────────

    /// <summary>Width of the Media &amp; Properties panel in pixels.</summary>
    public int PanelWidth { get; private set; } = DefaultPanelWidth;

    /// <summary>Height of the timeline in pixels. The preview takes what is left.</summary>
    public int TimelineHeight { get; private set; } = DefaultTimelineHeight;

    /// <summary>True once a person has dragged the timeline's seam themselves.</summary>
    /// <remarks>
    /// After that, <see cref="AutoFitTimeline"/> stops second-guessing them. Growing the timeline
    /// as tracks are added is a courtesy; overriding a deliberate drag is not.
    /// </remarks>
    public bool TimelineHeightUserSet { get; private set; }

    /// <summary>Whether the side panel is collapsed to its edge.</summary>
    public bool PanelCollapsed { get; private set; }

    /// <summary>Which side-panel tab is showing — "media", "assets" or "props".</summary>
    public string PanelTab { get; private set; } = "media";

    // ── constraints ──────────────────────────────────────────────────────────
    public const int PanelMinWidth  = 280;
    public const int PanelMaxWidth  = 560;
    public const int DefaultPanelWidth = 340;

    public const int TimelineMinHeight = 120;
    public const int TimelineMaxHeight = 600;
    public const int DefaultTimelineHeight = 260;

    // ── change notification ───────────────────────────────────────────────────
    public event Action? OnChanged;

    // ── resize ────────────────────────────────────────────────────────────────

    public void SetPanelWidth(int px)
    {
        PanelWidth = Math.Clamp(px, PanelMinWidth, PanelMaxWidth);
        Notify();
    }

    /// <summary>A deliberate drag of the timeline's seam.</summary>
    public void SetTimelineHeight(int px)
    {
        TimelineHeight        = Math.Clamp(px, TimelineMinHeight, TimelineMaxHeight);
        TimelineHeightUserSet = true;
        Notify();
    }

    /// <summary>
    /// Grows or shrinks the timeline to suit the tracks it now holds, unless the person has
    /// already sized it themselves.
    /// </summary>
    public void AutoFitTimeline(int preferredPx)
    {
        if (TimelineHeightUserSet) return;

        var next = Math.Clamp(preferredPx, TimelineMinHeight, TimelineMaxHeight);
        if (next == TimelineHeight) return;

        TimelineHeight = next;
        Notify();
    }

    public void TogglePanel()
    {
        PanelCollapsed = !PanelCollapsed;
        Notify();
    }

    public void SetPanelCollapsed(bool collapsed)
    {
        if (PanelCollapsed == collapsed) return;
        PanelCollapsed = collapsed;
        Notify();
    }

    public void SetPanelTab(string tabId)
    {
        if (string.IsNullOrWhiteSpace(tabId) || PanelTab == tabId) return;
        PanelTab = tabId;
        Notify();
    }

    // ── persistence ───────────────────────────────────────────────────────────

    /// <summary>The localStorage key the editor stores <see cref="Export"/> under.</summary>
    public const string StorageKey = "bv-layout";

    /// <summary>
    /// The whole layout as one small serialisable value.
    /// </summary>
    /// <remarks>
    /// A record rather than fields on the service so the round trip is testable without a browser:
    /// the JSON is what a stale or hand-edited entry looks like, and <see cref="Apply"/> has to
    /// survive it.
    /// </remarks>
    public sealed record LayoutSnapshot
    {
        [JsonPropertyName("panelWidth")]     public int?    PanelWidth     { get; init; }
        [JsonPropertyName("timelineHeight")] public int?    TimelineHeight { get; init; }
        [JsonPropertyName("timelineUserSet")]public bool?   TimelineUserSet{ get; init; }
        [JsonPropertyName("panelCollapsed")] public bool?   PanelCollapsed { get; init; }
        [JsonPropertyName("panelTab")]       public string? PanelTab       { get; init; }
    }

    private static readonly JsonSerializerOptions SnapshotJson = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public LayoutSnapshot Export() => new()
    {
        PanelWidth      = PanelWidth,
        TimelineHeight  = TimelineHeight,
        TimelineUserSet = TimelineHeightUserSet,
        PanelCollapsed  = PanelCollapsed,
        PanelTab        = PanelTab,
    };

    /// <summary>
    /// Restores a snapshot, clamping everything it carries.
    /// </summary>
    /// <remarks>
    /// Every field is optional and every value is clamped, because this comes from the browser's
    /// own storage: an older build wrote fewer fields, and a hand-edited entry can say anything. A
    /// bad value must give a usable editor, never a zero-height timeline.
    /// </remarks>
    public void Apply(LayoutSnapshot? snapshot)
    {
        if (snapshot is null) return;

        if (snapshot.PanelWidth is { } w)
            PanelWidth = Math.Clamp(w, PanelMinWidth, PanelMaxWidth);

        if (snapshot.TimelineHeight is { } h)
            TimelineHeight = Math.Clamp(h, TimelineMinHeight, TimelineMaxHeight);

        TimelineHeightUserSet = snapshot.TimelineUserSet ?? false;
        PanelCollapsed        = snapshot.PanelCollapsed ?? false;

        if (!string.IsNullOrWhiteSpace(snapshot.PanelTab))
            PanelTab = snapshot.PanelTab;

        Notify();
    }

    public string Serialise() => JsonSerializer.Serialize(Export(), SnapshotJson);

    /// <summary>Reads a stored snapshot. Anything unparseable is treated as no preference.</summary>
    public static LayoutSnapshot? Deserialise(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;

        try { return JsonSerializer.Deserialize<LayoutSnapshot>(json, SnapshotJson); }
        catch (JsonException) { return null; }
    }

    private void Notify() => OnChanged?.Invoke();
}
