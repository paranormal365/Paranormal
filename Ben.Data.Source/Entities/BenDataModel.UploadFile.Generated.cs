using System;

namespace Ben.Data.Source.Entities
{
    public partial class UploadFile
    {
        public Guid UploadFileTypeId { get; set; }
        public Guid AppUserId { get; set; }
        public string FileName { get; set; } = null!;
        public string StoredFileName { get; set; } = null!;
        public string ContentType { get; set; } = null!;
        public long FileSize { get; set; }
        public byte[] FileData { get; set; } = null!;
        public string? Description { get; set; }
        public bool IsPublic { get; set; }
        public int SortOrder { get; set; }
        public DateTime DateCreated { get; set; }
        public DateTime? DateUpdated { get; set; }
        public Guid CreatedByAppUserId { get; set; }
        public Guid? UpdatedByAppUserId { get; set; }

        public virtual UploadFileType UploadFileType { get; set; } = null!;
        public virtual AppUser AppUser { get; set; } = null!;
        public virtual AppUser CreatedByAppUser { get; set; } = null!;
        public virtual AppUser? UpdatedByAppUser { get; set; }
        public virtual ICollection<UploadFileOrganizationShare> OrganizationShares { get; set; } = new List<UploadFileOrganizationShare>();
        public virtual ICollection<UploadFilePermissionRequest> PermissionRequests { get; set; } = new List<UploadFilePermissionRequest>();
    }
}
