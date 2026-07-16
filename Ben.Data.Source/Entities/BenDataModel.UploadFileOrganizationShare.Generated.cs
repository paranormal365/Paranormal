using System;
using Ben.Data.Common.Enums;

namespace Ben.Data.Source.Entities
{
    public partial class UploadFileOrganizationShare
    {
        public Guid UploadFileId { get; set; }
        public Guid OrganizationId { get; set; }
        public Guid SharedByAppUserId { get; set; }
        public FileShareVisibility Visibility { get; set; }
        public bool IsActive { get; set; }
        public Guid? RemovedByAppUserId { get; set; }
        public DateTime? RemovalDate { get; set; }
        public DateTime DateCreated { get; set; }
        public DateTime? DateUpdated { get; set; }
        public Guid CreatedByAppUserId { get; set; }
        public Guid? UpdatedByAppUserId { get; set; }

        public virtual UploadFile UploadFile { get; set; } = null!;
        public virtual Organization Organization { get; set; } = null!;
        public virtual AppUser SharedByAppUser { get; set; } = null!;
        public virtual AppUser? RemovedByAppUser { get; set; }
        public virtual AppUser CreatedByAppUser { get; set; } = null!;
        public virtual AppUser? UpdatedByAppUser { get; set; }
    }
}
