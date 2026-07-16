namespace Ben.Data.Source.Entities
{
    public partial class UploadFileTypeExtension
    {
        public Guid UploadFileTypeId { get; set; }

        /// <summary>
        /// Extension pattern to match. Supports exact (e.g. ".txt") and
        /// glob-style wildcard suffix (e.g. ".tx*" matches .txa, .txb, .txzzzz).
        /// </summary>
        public string Pattern { get; set; } = null!;

        public DateTime DateCreated { get; set; }
        public Guid CreatedByAppUserId { get; set; }

        public virtual UploadFileType UploadFileType { get; set; } = null!;
        public virtual AppUser CreatedByAppUser { get; set; } = null!;
    }
}
