namespace Ben.Data.Source.Entities
{
    /// <summary>Links an uploaded file to a client request.</summary>
    public partial class ClientRequestFile
    {
        public Guid ClientRequestId { get; set; }
        public Guid UploadFileId { get; set; }
        public DateTime DateCreated { get; set; }
        public Guid CreatedByAppUserId { get; set; }
        public DateTime? DateUpdated { get; set; }
        public Guid? UpdatedByAppUserId { get; set; }

        public virtual ClientRequest ClientRequest { get; set; } = null!;
        public virtual UploadFile UploadFile { get; set; } = null!;
        public virtual AppUser CreatedByAppUser { get; set; } = null!;
        public virtual AppUser? UpdatedByAppUser { get; set; }
    }
}
