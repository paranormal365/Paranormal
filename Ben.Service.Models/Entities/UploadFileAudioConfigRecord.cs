namespace Ben.Service.Models.Entities;

/// <summary>Read DTO for UploadFileAudioConfig. Null color/option properties = use Telerik theme defaults.</summary>
public record UploadFileAudioConfigRecord
{
    public Guid Id { get; init; }
    public Guid UploadFileId { get; init; }

    // Visual — null = resolved from --kendo-color-* CSS variables at runtime
    public string? WaveColor { get; init; }
    public string? ProgressColor { get; init; }
    public string? CursorColor { get; init; }
    public int? CursorWidth { get; init; }
    public int? Height { get; init; }           // null = "auto"

    // Bar style — null = solid waveform
    public int? BarWidth { get; init; }
    public int? BarGap { get; init; }
    public int? BarRadius { get; init; }
    public double? BarHeight { get; init; }
    public string? BarAlign { get; init; }

    // Behaviour
    public bool Normalize { get; init; }
    public bool DragToSeek { get; init; }
    public bool HideScrollbar { get; init; }
    public double? AudioRate { get; init; }

    // Plugin enables
    public bool EnableHover { get; init; }
    public bool EnableTimeline { get; init; }
    public bool EnableZoom { get; init; }
    public bool EnableMinimap { get; init; }
    public bool EnableSpectrogram { get; init; }
    public bool EnableSpectrogramWindowed { get; init; }
    public bool EnableEnvelope { get; init; }
    public bool EnableRegions { get; init; }

    // Plugin options JSON (null = use WaveSurfer built-in defaults)
    public string? HoverOptionsJson { get; init; }
    public string? TimelineOptionsJson { get; init; }
    public string? ZoomOptionsJson { get; init; }
    public string? MinimapOptionsJson { get; init; }
    public string? SpectrogramOptionsJson { get; init; }
    public string? SpectrogramWindowedOptionsJson { get; init; }
    /// <summary>
    /// The listening chain — equaliser, filters, compressor, noise gate — as JSON.
    /// </summary>
    /// <remarks>
    /// What somebody set up to hear a recording more clearly. None of it changes the file, and
    /// null means the component's own defaults.
    /// </remarks>
    public string? EditStateJson { get; init; }
    public string? EnvelopeOptionsJson { get; init; }

    // Component layout
    public string InitialHeight { get; init; } = "200px";
    public string MinHeight { get; init; } = "80px";
    public string MaxHeight { get; init; } = "800px";
    public bool ShowControls { get; init; }
    public double MinZoom { get; init; }
    public double MaxZoom { get; init; }

    // Audit
    public DateTime DateCreated { get; init; }
    public DateTime? DateUpdated { get; init; }
    public Guid CreatedByAppUserId { get; init; }
    public Guid? UpdatedByAppUserId { get; init; }
}

/// <summary>
/// Request body for creating or updating (upsert) an audio config.
/// Omit a property to keep its current DB value when updating (controller handles defaults on create).
/// </summary>
public record UpsertAudioConfigRequest
{
    public string? WaveColor { get; init; }
    public string? ProgressColor { get; init; }
    public string? CursorColor { get; init; }
    public int? CursorWidth { get; init; }
    public int? Height { get; init; }

    public int? BarWidth { get; init; }
    public int? BarGap { get; init; }
    public int? BarRadius { get; init; }
    public double? BarHeight { get; init; }
    public string? BarAlign { get; init; }

    public bool Normalize { get; init; }
    public bool DragToSeek { get; init; }
    public bool HideScrollbar { get; init; }
    public double? AudioRate { get; init; }

    public bool EnableHover { get; init; } = true;
    public bool EnableTimeline { get; init; } = true;
    public bool EnableZoom { get; init; }
    public bool EnableMinimap { get; init; }
    public bool EnableSpectrogram { get; init; }
    public bool EnableSpectrogramWindowed { get; init; }
    public bool EnableEnvelope { get; init; }
    public bool EnableRegions { get; init; }

    public string? HoverOptionsJson { get; init; }
    public string? TimelineOptionsJson { get; init; }
    public string? ZoomOptionsJson { get; init; }
    public string? MinimapOptionsJson { get; init; }
    public string? SpectrogramOptionsJson { get; init; }
    public string? SpectrogramWindowedOptionsJson { get; init; }
    /// <summary>
    /// The listening chain — equaliser, filters, compressor, noise gate — as JSON.
    /// </summary>
    /// <remarks>
    /// What somebody set up to hear a recording more clearly. None of it changes the file, and
    /// null means the component's own defaults.
    /// </remarks>
    public string? EditStateJson { get; init; }
    public string? EnvelopeOptionsJson { get; init; }

    public string InitialHeight { get; init; } = "200px";
    public string MinHeight { get; init; } = "80px";
    public string MaxHeight { get; init; } = "800px";
    public bool ShowControls { get; init; } = true;
    public double MinZoom { get; init; } = 10;
    public double MaxZoom { get; init; } = 1000;
}
