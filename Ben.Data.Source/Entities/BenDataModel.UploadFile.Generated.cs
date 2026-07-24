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

        /// <summary>
        /// Relative path within the configured file-storage root (e.g. "users/{userId}/{storedFileName}").
        /// Null only on legacy rows that have not yet been migrated from the FileData column.
        /// </summary>
        public string? StoragePath { get; set; }

        /// <summary>
        /// Legacy binary blob.  Null for all new uploads; populated only on rows not yet
        /// migrated by FileMigrationService.  Will be dropped in a future migration.
        /// </summary>
        public byte[]? FileData { get; set; }
        public string? Description { get; set; }
        public bool IsPublic { get; set; }
        public int SortOrder { get; set; }
        public DateTime DateCreated { get; set; }
        public DateTime? DateUpdated { get; set; }
        public Guid CreatedByAppUserId { get; set; }
        public Guid? UpdatedByAppUserId { get; set; }

        /// <summary>When this file was clipped from another file, the ID of the source file.</summary>
        public Guid? ParentFileId { get; set; }

        /// <summary>Start time (seconds) within the parent file that this clip begins at.</summary>
        public double? RegionStart { get; set; }

        /// <summary>End time (seconds) within the parent file that this clip ends at.</summary>
        public double? RegionEnd { get; set; }

        public virtual UploadFileType UploadFileType { get; set; } = null!;
        public virtual AppUser AppUser { get; set; } = null!;
        public virtual AppUser CreatedByAppUser { get; set; } = null!;
        public virtual AppUser? UpdatedByAppUser { get; set; }
        public virtual UploadFile? ParentFile { get; set; }
        public virtual ICollection<UploadFile> ChildClips { get; set; } = new List<UploadFile>();
        public virtual ICollection<UploadFileOrganizationShare> OrganizationShares { get; set; } = new List<UploadFileOrganizationShare>();
        public virtual ICollection<UploadFilePermissionRequest> PermissionRequests { get; set; } = new List<UploadFilePermissionRequest>();
        public virtual UploadFileAudioConfig? AudioConfig { get; set; }
        public virtual ICollection<UploadFileRegionNote> RegionNotes { get; set; } = new List<UploadFileRegionNote>();
        public virtual ICollection<UploadFileVote> Votes { get; set; } = new List<UploadFileVote>();
    }
}
