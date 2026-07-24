namespace Ben.Data.Source.Entities
{
    /// <summary>Links an uploaded file to a specific case timeline entry.</summary>
    public class CaseTimelineEntryFile
    {
        public Guid Id { get; set; }
        public Guid CaseTimelineEntryId { get; set; }
        public Guid UploadFileId { get; set; }
        public DateTime DateCreated { get; set; }
        public Guid CreatedByAppUserId { get; set; }

        public virtual CaseTimelineEntry CaseTimelineEntry { get; set; } = null!;
        public virtual UploadFile UploadFile { get; set; } = null!;
        public virtual AppUser CreatedByAppUser { get; set; } = null!;
    }
}
