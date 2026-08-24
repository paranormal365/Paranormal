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

        /// <summary>
        /// The file this record's location and capture details were CARRIED FROM, when the file
        /// is a derivative — a clip cut from a recording, an edited copy, a mix (Ben's rule,
        /// 2026-08-24: "if we create clips from an audio file, keep any lat/lon altitude or other
        /// info related to the clip").
        /// </summary>
        /// <remarks>
        /// Null means the values were read off THESE bytes. The distinction is the whole point of
        /// storing it: a clip carries no EXIF of its own — an encoder writes none — so without
        /// this the choice would be to lose the location or to imply the clip was measured at it.
        /// Inherited values are still true about where the recording was made, which is what an
        /// investigator is asking when they ask where a clip came from.
        /// </remarks>
        public Guid? InheritedFromUploadFileId { get; set; }

        public DateTime ExtractedAtUtc { get; set; }

        public virtual UploadFile UploadFile { get; set; } = null!;
    }
}
