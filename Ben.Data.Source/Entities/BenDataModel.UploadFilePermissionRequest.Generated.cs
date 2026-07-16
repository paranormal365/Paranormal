using System;
using Ben.Data.Common.Enums;

namespace Ben.Data.Source.Entities
{
    public partial class UploadFilePermissionRequest
    {
        public Guid UploadFileId { get; set; }
        public Guid? OrganizationId { get; set; }
        public Guid RequestedByAppUserId { get; set; }
        public FilePermissionType PermissionType { get; set; }
        public FilePermissionRequestStatus RequestStatus { get; set; }
        public string? RequestNotes { get; set; }
        public string? ReviewNotes { get; set; }
        public Guid? ReviewedByAppUserId { get; set; }
        public DateTime? DateReviewed { get; set; }
        public DateTime DateCreated { get; set; }
        public DateTime? DateUpdated { get; set; }
        public Guid CreatedByAppUserId { get; set; }
        public Guid? UpdatedByAppUserId { get; set; }

        public virtual UploadFile UploadFile { get; set; } = null!;
        public virtual Organization? Organization { get; set; }
        public virtual AppUser RequestedByAppUser { get; set; } = null!;
        public virtual AppUser? ReviewedByAppUser { get; set; }
        public virtual AppUser CreatedByAppUser { get; set; } = null!;
        public virtual AppUser? UpdatedByAppUser { get; set; }
    }
}
