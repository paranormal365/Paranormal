namespace Ben.Data.Source.Entities
{
    /// <summary>An uploaded file included as evidence in a report section.</summary>
    public class CaseReportSectionFile
    {
        public Guid Id { get; set; }
        public Guid CaseReportSectionId { get; set; }
        public Guid UploadFileId { get; set; }
        public string? Caption { get; set; }
        public int SortOrder { get; set; }

        public virtual CaseReportSection Section { get; set; } = null!;
        public virtual UploadFile UploadFile { get; set; } = null!;
    }
}
