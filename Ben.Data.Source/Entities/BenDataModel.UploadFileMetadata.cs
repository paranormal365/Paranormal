namespace Ben.Data.Source.Entities
{
    /// <summary>
    /// Extracted technical metadata for an uploaded file.
    /// SuperAdmin-only — never included in regular API responses.
    /// </summary>
    public class UploadFileMetadata
    {
        public Guid Id { get; set; }

        /// <summary>1-to-1 with UploadFile.</summary>
        public Guid UploadFileId { get; set; }

        /// <summary>"Audio" | "Image" | "Video" | "Unknown"</summary>
        public string MediaKind { get; set; } = "Unknown";

        // ── Audio ─────────────────────────────────────────────────────────────
        public double? DurationSeconds  { get; set; }
        public int?    SampleRateHz     { get; set; }
        public int?    BitRateKbps      { get; set; }
        public int?    Channels         { get; set; }
        public string? AudioCodec       { get; set; }

        // ── Image / Video ─────────────────────────────────────────────────────
        public int?    WidthPixels      { get; set; }
        public int?    HeightPixels     { get; set; }

        // ── EXIF — common to images and phone/camera videos ───────────────────
        public DateTime? CapturedAtUtc      { get; set; }
        public double?   GpsLatitude        { get; set; }
        public double?   GpsLongitude       { get; set; }
        public double?   GpsAltitudeMeters  { get; set; }
        public string?   CameraManufacturer { get; set; }
        public string?   CameraModel        { get; set; }

        /// <summary>Full raw extraction dump serialised as JSON for future use.</summary>
        public string?   RawMetadataJson    { get; set; }

        public DateTime ExtractedAtUtc { get; set; }

        public virtual UploadFile UploadFile { get; set; } = null!;
    }
}
