using System.Text.Json.Serialization;

namespace Ben.Web.Library.Manage.Audio;

// ── Audio Source ──────────────────────────────────────────────────────────────

/// <summary>
/// Determines how the audio data is supplied to WaveSurfer.
/// </summary>
public enum WsAudioSourceType
{
    /// <summary>A direct URL: streaming URL, CDN link, or API endpoint.</summary>
    Url,

    /// <summary>Raw byte array (e.g. fetched from the database via IBenAdminClient). Converted to a base64 data URL.</summary>
    Bytes,

    /// <summary>Already-encoded base64 string with an accompanying MIME type.</summary>
    Base64,
}

/// <summary>
/// Encapsulates the audio source for <see cref="WaveSurferPlayer"/>.
/// Use the static factory methods rather than constructing directly.
/// </summary>
public class WsAudioSource
{
    /// <summary>How the audio is supplied.</summary>
    public WsAudioSourceType Type { get; init; } = WsAudioSourceType.Url;

    /// <summary>Used for <see cref="WsAudioSourceType.Url"/>.</summary>
    public string? Url { get; init; }

    /// <summary>Used for <see cref="WsAudioSourceType.Bytes"/>. Raw audio bytes.</summary>
    public byte[]? Bytes { get; init; }

    /// <summary>Used for <see cref="WsAudioSourceType.Base64"/>. Base64-encoded audio string (no data-URL prefix).</summary>
    public string? Base64 { get; init; }

    /// <summary>MIME type — required for <see cref="WsAudioSourceType.Bytes"/> and <see cref="WsAudioSourceType.Base64"/>.</summary>
    public string ContentType { get; init; } = "audio/mpeg";

    // ── Validation ────────────────────────────────────────────────────────────

    /// <summary>True when the source has enough data to be loaded.</summary>
    public bool IsValid => Type switch
    {
        WsAudioSourceType.Url    => !string.IsNullOrWhiteSpace(Url),
        WsAudioSourceType.Bytes  => Bytes is { Length: > 0 },
        WsAudioSourceType.Base64 => !string.IsNullOrWhiteSpace(Base64) && !string.IsNullOrWhiteSpace(ContentType),
        _                        => false,
    };

    // ── Resolution ────────────────────────────────────────────────────────────

    /// <summary>
    /// Returns the URL or data URL string that WaveSurfer's <c>load(url)</c> accepts.
    /// Returns <c>null</c> when the source is not valid.
    /// </summary>
    public string? ToLoadUrl() => Type switch
    {
        WsAudioSourceType.Url    => IsValid ? Url : null,
        WsAudioSourceType.Bytes  => IsValid ? $"data:{ContentType};base64,{Convert.ToBase64String(Bytes!)}" : null,
        WsAudioSourceType.Base64 => IsValid ? $"data:{ContentType};base64,{Base64}" : null,
        _                        => null,
    };

    // ── Factory Methods ───────────────────────────────────────────────────────

    /// <summary>Create a source from a direct URL (streaming, CDN, or API endpoint).</summary>
    public static WsAudioSource FromUrl(string url) => new()
    {
        Type = WsAudioSourceType.Url,
        Url  = url,
    };

    /// <summary>Create a source from raw bytes (e.g. fetched from the database).</summary>
    public static WsAudioSource FromBytes(byte[] bytes, string contentType = "audio/mpeg") => new()
    {
        Type        = WsAudioSourceType.Bytes,
        Bytes       = bytes,
        ContentType = contentType,
    };

    /// <summary>Create a source from an already-encoded base64 string (without the data-URL prefix).</summary>
    public static WsAudioSource FromBase64(string base64, string contentType = "audio/mpeg") => new()
    {
        Type        = WsAudioSourceType.Base64,
        Base64      = base64,
        ContentType = contentType,
    };

    /// <summary>
    /// Create a source directly from a full data URL (<c>data:audio/mpeg;base64,...</c>).
    /// The data URL is used as-is via <see cref="WsAudioSourceType.Url"/>.
    /// </summary>
    public static WsAudioSource FromDataUrl(string dataUrl) => new()
    {
        Type = WsAudioSourceType.Url,
        Url  = dataUrl,
    };
}

// ── Core Options ──────────────────────────────────────────────────────────────

/// <summary>
/// WaveSurfer creation options. Property names are the PascalCase equivalents of the
/// WaveSurfer JS option names (e.g. <c>waveColor</c> → <c>WaveColor</c>).
///
/// Color properties default to <c>null</c> so the JS interop can resolve appropriate
/// values from the current Telerik theme's CSS custom properties
/// (<c>--kendo-color-primary</c> etc.) at runtime. Pass an explicit CSS color string
/// to override.
/// </summary>
public record class WsOptions
{
    // ── Dimensions ────────────────────────────────────────────────────────────

    /// <summary>
    /// Waveform height in pixels, or <c>null</c> to fill the container height automatically.
    /// Matches WaveSurfer JS <c>height</c> option ("auto" when null).
    /// </summary>
    [JsonPropertyName("height")]
    public int? Height { get; set; }

    /// <summary>Width in pixels or CSS string. <c>null</c> = 100% (fillParent).</summary>
    [JsonPropertyName("width")]
    public string? Width { get; set; }

    // ── Colors — null = resolved from Telerik CSS variables at runtime ─────────

    /// <summary>
    /// Color of the unplayed waveform. <c>null</c> = resolved from
    /// <c>--kendo-color-primary</c> CSS variable at runtime.
    /// </summary>
    [JsonPropertyName("waveColor")]
    public string? WaveColor { get; set; }

    /// <summary>
    /// Color of the played (progress) portion. <c>null</c> = resolved from
    /// <c>--kendo-color-primary-emphasis</c> or a darkened primary at runtime.
    /// </summary>
    [JsonPropertyName("progressColor")]
    public string? ProgressColor { get; set; }

    /// <summary>
    /// Color of the playback cursor. <c>null</c> = resolved from
    /// <c>--kendo-body-text</c> CSS variable at runtime.
    /// </summary>
    [JsonPropertyName("cursorColor")]
    public string? CursorColor { get; set; }

    [JsonPropertyName("cursorWidth")]
    public int? CursorWidth { get; set; }

    // ── Bar style ─────────────────────────────────────────────────────────────

    [JsonPropertyName("barWidth")]
    public int? BarWidth { get; set; }

    [JsonPropertyName("barGap")]
    public int? BarGap { get; set; }

    [JsonPropertyName("barRadius")]
    public int? BarRadius { get; set; }

    [JsonPropertyName("barHeight")]
    public double? BarHeight { get; set; }

    /// <summary>"top" | "bottom"</summary>
    [JsonPropertyName("barAlign")]
    public string? BarAlign { get; set; }

    [JsonPropertyName("barMinHeight")]
    public int? BarMinHeight { get; set; }

    // ── Behavior ──────────────────────────────────────────────────────────────

    [JsonPropertyName("minPxPerSec")]
    public double? MinPxPerSec { get; set; }

    [JsonPropertyName("fillParent")]
    public bool? FillParent { get; set; } = true;

    [JsonPropertyName("interact")]
    public bool? Interact { get; set; } = true;

    [JsonPropertyName("dragToSeek")]
    public bool? DragToSeek { get; set; }

    [JsonPropertyName("hideScrollbar")]
    public bool? HideScrollbar { get; set; }

    [JsonPropertyName("audioRate")]
    public double? AudioRate { get; set; }

    [JsonPropertyName("autoScroll")]
    public bool? AutoScroll { get; set; } = true;

    [JsonPropertyName("autoCenter")]
    public bool? AutoCenter { get; set; } = true;

    [JsonPropertyName("normalize")]
    public bool? Normalize { get; set; }

    [JsonPropertyName("sampleRate")]
    public int? SampleRate { get; set; }

    /// <summary>"WebAudio" | "MediaElement" (default).</summary>
    [JsonPropertyName("backend")]
    public string? Backend { get; set; }

    [JsonPropertyName("mediaControls")]
    public bool? MediaControls { get; set; }

    [JsonPropertyName("autoplay")]
    public bool? Autoplay { get; set; }
}

// ── Plugin Options ────────────────────────────────────────────────────────────

public class WsHoverOptions
{
    [JsonPropertyName("lineColor")]
    public string? LineColor { get; set; }

    [JsonPropertyName("lineWidth")]
    public int? LineWidth { get; set; }

    [JsonPropertyName("labelColor")]
    public string? LabelColor { get; set; }

    [JsonPropertyName("labelSize")]
    public int? LabelSize { get; set; }

    [JsonPropertyName("labelBackground")]
    public string? LabelBackground { get; set; }

    [JsonPropertyName("labelPreferLeft")]
    public bool? LabelPreferLeft { get; set; }
}

public class WsTimelineOptions
{
    [JsonPropertyName("height")]
    public int? Height { get; set; }

    [JsonPropertyName("insertPosition")]
    public string? InsertPosition { get; set; }

    [JsonPropertyName("timeInterval")]
    public double? TimeInterval { get; set; }

    [JsonPropertyName("primaryLabelInterval")]
    public double? PrimaryLabelInterval { get; set; }

    [JsonPropertyName("secondaryLabelInterval")]
    public double? SecondaryLabelInterval { get; set; }

    [JsonPropertyName("primaryLabelSpacing")]
    public int? PrimaryLabelSpacing { get; set; }

    [JsonPropertyName("secondaryLabelSpacing")]
    public int? SecondaryLabelSpacing { get; set; }

    [JsonPropertyName("timeOffset")]
    public double? TimeOffset { get; set; }

    [JsonPropertyName("style")]
    public string? Style { get; set; }

    [JsonPropertyName("secondaryLabelOpacity")]
    public double? SecondaryLabelOpacity { get; set; }
}

public class WsZoomOptions
{
    [JsonPropertyName("scale")]
    public double? Scale { get; set; }

    [JsonPropertyName("maxZoom")]
    public double? MaxZoom { get; set; }

    [JsonPropertyName("deltaThreshold")]
    public int? DeltaThreshold { get; set; }

    [JsonPropertyName("exponentialZooming")]
    public bool? ExponentialZooming { get; set; }

    [JsonPropertyName("iterations")]
    public int? Iterations { get; set; }
}

public class WsMinimapOptions
{
    [JsonPropertyName("overlayColor")]
    public string? OverlayColor { get; set; }

    [JsonPropertyName("insertPosition")]
    public string? InsertPosition { get; set; }

    [JsonPropertyName("height")]
    public int? Height { get; set; }

    [JsonPropertyName("waveColor")]
    public string? WaveColor { get; set; }

    [JsonPropertyName("progressColor")]
    public string? ProgressColor { get; set; }
}

public class WsSpectrogramOptions
{
    [JsonPropertyName("height")]
    public int? Height { get; set; }

    [JsonPropertyName("labels")]
    public bool? Labels { get; set; }

    [JsonPropertyName("labelsBackground")]
    public string? LabelsBackground { get; set; }

    [JsonPropertyName("labelsColor")]
    public string? LabelsColor { get; set; }

    [JsonPropertyName("fftSamples")]
    public int? FftSamples { get; set; }

    [JsonPropertyName("windowFunc")]
    public string? WindowFunc { get; set; }

    [JsonPropertyName("frequencyMin")]
    public double? FrequencyMin { get; set; }

    [JsonPropertyName("frequencyMax")]
    public double? FrequencyMax { get; set; }
}

public class WsEnvelopeOptions
{
    [JsonPropertyName("volume")]
    public double? Volume { get; set; }

    [JsonPropertyName("lineWidth")]
    public string? LineWidth { get; set; }

    [JsonPropertyName("lineColor")]
    public string? LineColor { get; set; }

    [JsonPropertyName("dragLine")]
    public bool? DragLine { get; set; }

    [JsonPropertyName("dragPointSize")]
    public int? DragPointSize { get; set; }

    [JsonPropertyName("dragPointFill")]
    public string? DragPointFill { get; set; }

    [JsonPropertyName("dragPointStroke")]
    public string? DragPointStroke { get; set; }

    [JsonPropertyName("points")]
    public List<WsEnvelopePoint>? Points { get; set; }
}

// ── Plugin Config ─────────────────────────────────────────────────────────────

/// <summary>
/// Aggregated plugin enable flags and per-plugin options passed to the JS init function.
/// </summary>
public class WsPluginConfig
{
    [JsonPropertyName("regions")]
    public bool Regions { get; set; }

    [JsonPropertyName("hover")]
    public bool Hover { get; set; }

    [JsonPropertyName("hoverOptions")]
    public WsHoverOptions? HoverOptions { get; set; }

    [JsonPropertyName("timeline")]
    public bool Timeline { get; set; }

    [JsonPropertyName("timelineOptions")]
    public WsTimelineOptions? TimelineOptions { get; set; }

    [JsonPropertyName("zoom")]
    public bool Zoom { get; set; }

    [JsonPropertyName("zoomOptions")]
    public WsZoomOptions? ZoomOptions { get; set; }

    [JsonPropertyName("minimap")]
    public bool Minimap { get; set; }

    [JsonPropertyName("minimapOptions")]
    public WsMinimapOptions? MinimapOptions { get; set; }

    [JsonPropertyName("spectrogram")]
    public bool Spectrogram { get; set; }

    [JsonPropertyName("spectrogramOptions")]
    public WsSpectrogramOptions? SpectrogramOptions { get; set; }

    [JsonPropertyName("spectrogramWindowed")]
    public bool SpectrogramWindowed { get; set; }

    [JsonPropertyName("spectrogramWindowedOptions")]
    public WsSpectrogramOptions? SpectrogramWindowedOptions { get; set; }

    [JsonPropertyName("envelope")]
    public bool Envelope { get; set; }

    [JsonPropertyName("envelopeOptions")]
    public WsEnvelopeOptions? EnvelopeOptions { get; set; }
}

// ── DTOs ──────────────────────────────────────────────────────────────────────

public class WsRegionData
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("start")]
    public double Start { get; set; }

    [JsonPropertyName("end")]
    public double End { get; set; }

    [JsonPropertyName("color")]
    public string? Color { get; set; }
}

/// <summary>Parameters for adding a new region. PascalCase of WaveSurfer JS <c>RegionParams</c>.</summary>
public class WsRegionParams
{
    [JsonPropertyName("id")]
    public string? Id { get; set; }

    [JsonPropertyName("start")]
    public double Start { get; set; }

    [JsonPropertyName("end")]
    public double? End { get; set; }

    [JsonPropertyName("color")]
    public string? Color { get; set; }

    [JsonPropertyName("drag")]
    public bool? Drag { get; set; }

    [JsonPropertyName("resize")]
    public bool? Resize { get; set; }

    [JsonPropertyName("content")]
    public string? Content { get; set; }

    [JsonPropertyName("minLength")]
    public double? MinLength { get; set; }

    [JsonPropertyName("maxLength")]
    public double? MaxLength { get; set; }
}

/// <summary>A single volume-control point on the Envelope plugin. PascalCase of <c>EnvelopePoint</c>.</summary>
public class WsEnvelopePoint
{
    [JsonPropertyName("id")]
    public string? Id { get; set; }

    [JsonPropertyName("time")]
    public double Time { get; set; }

    [JsonPropertyName("volume")]
    public double Volume { get; set; }
}

// ── Aggregated Config ─────────────────────────────────────────────────────────

/// <summary>
/// Single configuration object for <see cref="WaveSurferPlayer"/>.
/// Pass this as the <c>Config</c> parameter instead of specifying individual parameters.
///
/// All color properties in <see cref="Options"/> default to <c>null</c>, which the
/// JS interop resolves from the active Telerik theme's CSS custom properties
/// (<c>--kendo-color-primary</c> etc.) at runtime — so the player is automatically
/// themed even without any explicit configuration.
/// </summary>
public class WsConfig
{
    // ── Audio source ──────────────────────────────────────────────────────────
    public WsAudioSource? Source { get; set; }

    // ── WaveSurfer core options ───────────────────────────────────────────────
    public WsOptions Options { get; set; } = new();

    // ── Plugin configuration ──────────────────────────────────────────────────
    public WsPluginConfig Plugins { get; set; } = new();

    // ── Component layout / UI ─────────────────────────────────────────────────

    /// <summary>Starting height of the resizable player wrapper. Any CSS length string.</summary>
    public string InitialHeight { get; set; } = "200px";

    /// <summary>Minimum drag-resize height.</summary>
    public string MinHeight { get; set; } = "80px";

    /// <summary>Maximum drag-resize height.</summary>
    public string MaxHeight { get; set; } = "800px";

    /// <summary>Render the built-in play/pause/stop/volume/zoom/rate controls bar.</summary>
    public bool ShowControls { get; set; } = true;

    /// <summary>Minimum zoom level (minPxPerSec) for the built-in Zoom slider.</summary>
    public double MinZoom { get; set; } = 10;

    /// <summary>Maximum zoom level (minPxPerSec) for the built-in Zoom slider.</summary>
    public double MaxZoom { get; set; } = 1000;

    /// <summary>Additional CSS class applied to the outermost wrapper element.</summary>
    public string? CssClass { get; set; }

    // ── Factory methods ───────────────────────────────────────────────────────

    /// <summary>Standard config: hover + timeline enabled, theme-derived colors.</summary>
    public static WsConfig Default(WsAudioSource? source = null) => new()
    {
        Source  = source,
        Plugins = new WsPluginConfig { Hover = true, Timeline = true },
    };

    /// <summary>Rich config: hover + timeline + zoom + minimap enabled.</summary>
    public static WsConfig Rich(WsAudioSource? source = null) => new()
    {
        Source  = source,
        Plugins = new WsPluginConfig { Hover = true, Timeline = true, Zoom = true, Minimap = true },
    };

    /// <summary>Compact config: no controls, reduced height.</summary>
    public static WsConfig Compact(WsAudioSource? source = null) => new()
    {
        Source        = source,
        InitialHeight = "100px",
        MinHeight     = "60px",
        ShowControls  = false,
    };
}

// ── DB record → WsConfig extension ───────────────────────────────────────────

/// <summary>
/// Extension methods to convert a persisted <c>UploadFileAudioConfigRecord</c> into
/// a runtime <see cref="WsConfig"/> for the <see cref="WaveSurferPlayer"/> component.
/// </summary>
public static class UploadFileAudioConfigExtensions
{
    /// <summary>
    /// Converts a persisted <c>UploadFileAudioConfigRecord</c> into a <see cref="WsConfig"/>.
    /// Null color/option properties remain null so the JS interop resolves them from the
    /// active Telerik CSS theme at runtime.
    /// </summary>
    /// <param name="record">The record loaded from the API.</param>
    /// <param name="source">Audio source to embed in the config (resolved separately from the file bytes/URL).</param>
    public static WsConfig ToWsConfig(
        this Ben.Service.Models.Entities.UploadFileAudioConfigRecord record,
        WsAudioSource? source = null)
    {
        return new WsConfig
        {
            Source = source,

            Options = new WsOptions
            {
                WaveColor     = record.WaveColor,
                ProgressColor = record.ProgressColor,
                CursorColor   = record.CursorColor,
                CursorWidth   = record.CursorWidth,
                Height        = record.Height,
                BarWidth      = record.BarWidth,
                BarGap        = record.BarGap,
                BarRadius     = record.BarRadius,
                BarHeight     = record.BarHeight,
                BarAlign      = record.BarAlign,
                Normalize     = record.Normalize    ? true : null,
                DragToSeek    = record.DragToSeek   ? true : null,
                HideScrollbar = record.HideScrollbar ? true : null,
                AudioRate     = record.AudioRate,
            },

            Plugins = new WsPluginConfig
            {
                Hover                    = record.EnableHover,
                HoverOptions             = Deserialize<WsHoverOptions>(record.HoverOptionsJson),
                Timeline                 = record.EnableTimeline,
                TimelineOptions          = Deserialize<WsTimelineOptions>(record.TimelineOptionsJson),
                Zoom                     = record.EnableZoom,
                ZoomOptions              = Deserialize<WsZoomOptions>(record.ZoomOptionsJson),
                Minimap                  = record.EnableMinimap,
                MinimapOptions           = Deserialize<WsMinimapOptions>(record.MinimapOptionsJson),
                Spectrogram              = record.EnableSpectrogram,
                SpectrogramOptions       = Deserialize<WsSpectrogramOptions>(record.SpectrogramOptionsJson),
                SpectrogramWindowed      = record.EnableSpectrogramWindowed,
                SpectrogramWindowedOptions = Deserialize<WsSpectrogramOptions>(record.SpectrogramWindowedOptionsJson),
                Envelope                 = record.EnableEnvelope,
                EnvelopeOptions          = Deserialize<WsEnvelopeOptions>(record.EnvelopeOptionsJson),
                Regions                  = record.EnableRegions,
            },

            InitialHeight = record.InitialHeight,
            MinHeight     = record.MinHeight,
            MaxHeight     = record.MaxHeight,
            ShowControls  = record.ShowControls,
            MinZoom       = record.MinZoom,
            MaxZoom       = record.MaxZoom,
        };
    }

    private static T? Deserialize<T>(string? json) where T : class
    {
        if (json is null) return null;
        try { return System.Text.Json.JsonSerializer.Deserialize<T>(json); }
        catch { return null; }
    }
}
