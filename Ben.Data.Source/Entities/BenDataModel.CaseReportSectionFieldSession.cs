namespace Ben.Data.Source.Entities
{
    /// <summary>
    /// A field session cited by one section of a case report.
    /// </summary>
    /// <remarks>
    /// <para><b>A reference, not a copy.</b> The session stays where it was uploaded — its
    /// document, its recordings, and its per-file digests all remain the originals. A report that
    /// copied the readings would be a second version of the night that could drift from the one
    /// the instruments actually produced, and the whole point of keeping the device's own
    /// <c>data.json</c> verbatim is that there is exactly one such version.</para>
    ///
    /// <para><b>Why the case manager needs this at all.</b> Everything the phones collected in
    /// the field — magnetic readings, sound levels, positions, rooms, marks, EVP questions,
    /// photographs and recordings — arrives as sessions. Until a section can point at one, that
    /// material is present on the site but absent from the document the client is actually
    /// given.</para>
    /// </remarks>
    public class CaseReportSectionFieldSession
    {
        public Guid Id { get; set; }
        public Guid CaseReportSectionId { get; set; }
        public Guid FieldSessionUploadId { get; set; }

        /// <summary>What the manager wants said about this session in the report — "the cellar
        /// spike at 01:14". Optional; the session's own label is used when this is empty.</summary>
        public string? Caption { get; set; }

        public int SortOrder { get; set; }

        public virtual CaseReportSection Section { get; set; } = null!;
        public virtual FieldSessionUpload FieldSessionUpload { get; set; } = null!;
    }
}
