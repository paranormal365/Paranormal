namespace Ben.Data.Source.Entities
{
    /// <summary>
    /// Stores per-UploadFile WaveSurfer player configuration.
    /// One row per audio UploadFile; absent row = use WaveSurferPlayer defaults.
    /// All color/option columns are nullable — null means "resolve from Telerik theme at runtime".
    /// </summary>
    public partial class UploadFileAudioConfig
    {
        // ── Foreign key ───────────────────────────────────────────────────────
        public Guid UploadFileId { get; set; }

        // ── Visual — null = resolve from --kendo-color-* CSS vars at runtime ──
        public string? WaveColor { get; set; }
        public string? ProgressColor { get; set; }
        public string? CursorColor { get; set; }
        public int? CursorWidth { get; set; }

        // ── Dimensions — null Height = "auto" (fill container) ───────────────
        public int? Height { get; set; }

        // ── Bar style — null = solid waveform ─────────────────────────────────
        public int? BarWidth { get; set; }
        public int? BarGap { get; set; }
        public int? BarRadius { get; set; }
        public double? BarHeight { get; set; }
        public string? BarAlign { get; set; }   // "top" | "bottom"

        // ── Behaviour ─────────────────────────────────────────────────────────
        public bool Normalize { get; set; }
        public bool DragToSeek { get; set; }
        public bool HideScrollbar { get; set; }
        public double? AudioRate { get; set; }

        // ── Plugin enables ────────────────────────────────────────────────────
        public bool EnableHover { get; set; }
        public bool EnableTimeline { get; set; }
        public bool EnableZoom { get; set; }
        public bool EnableMinimap { get; set; }
        public bool EnableSpectrogram { get; set; }
        public bool EnableSpectrogramWindowed { get; set; }
        public bool EnableEnvelope { get; set; }
        public bool EnableRegions { get; set; }

        // ── Plugin options (JSON, null = use WaveSurfer built-in defaults) ────
        public string? HoverOptionsJson { get; set; }
        public string? TimelineOptionsJson { get; set; }
        public string? ZoomOptionsJson { get; set; }
        public string? MinimapOptionsJson { get; set; }
        public string? SpectrogramOptionsJson { get; set; }
        public string? SpectrogramWindowedOptionsJson { get; set; }
        public string? EnvelopeOptionsJson { get; set; }

        // ── The listening chain (JSON, null = the component's own defaults) ───
        //
        // The equaliser, the high- and low-pass filters, the compressor and the noise gate: what
        // somebody set up to HEAR a recording more clearly. None of it changes the file. It has no
        // column of its own because there are fourteen numbers and they will grow; one JSON column
        // is the shape that does not need a migration every time a filter is added
        // (2026-09-06 audio audit, phase 5b).
        public string? EditStateJson { get; set; }

        // ── Component layout ──────────────────────────────────────────────────
        public string InitialHeight { get; set; } = "200px";
        public string MinHeight { get; set; } = "80px";
        public string MaxHeight { get; set; } = "800px";
        public bool ShowControls { get; set; }
        public double MinZoom { get; set; }
        public double MaxZoom { get; set; }

        // ── Audit ─────────────────────────────────────────────────────────────
        public DateTime DateCreated { get; set; }
        public DateTime? DateUpdated { get; set; }
        public Guid CreatedByAppUserId { get; set; }
        public Guid? UpdatedByAppUserId { get; set; }

        // ── Navigation ────────────────────────────────────────────────────────
        public virtual UploadFile UploadFile { get; set; } = null!;
        public virtual AppUser CreatedByAppUser { get; set; } = null!;
        public virtual AppUser? UpdatedByAppUser { get; set; }
    }
}
