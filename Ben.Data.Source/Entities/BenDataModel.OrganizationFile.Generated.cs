using System;

namespace Ben.Data.Source.Entities
{
    public partial class OrganizationFile
    {
        /// <summary>The organization that owns this file.</summary>
        public Guid OrganizationId { get; set; }

        public Guid UploadFileTypeId { get; set; }
        public string FileName { get; set; } = null!;
        public string StoredFileName { get; set; } = null!;
        public string ContentType { get; set; } = null!;
        public long FileSize { get; set; }

        /// <summary>Relative path within the storage root (e.g. "orgs/{orgId}/{storedFileName}").</summary>
        public string? StoragePath { get; set; }

        /// <summary>Legacy binary blob — null for all new files.</summary>
        public byte[]? FileData { get; set; }

        public string? Description { get; set; }
        /// <summary>True once a member with OrganizationFiles-Update permission has approved this file for public viewing.</summary>
        public bool IsPublic { get; set; }
        public int SortOrder { get; set; }

        /// <summary>User who approved this file for public access. Null until explicitly published.</summary>
        public Guid? PublishedByAppUserId { get; set; }

        /// <summary>UTC timestamp of when this file was first approved for public access.</summary>
        public DateTime? DatePublished { get; set; }

        /// <summary>
        /// When this file was copied from a user's <see cref="UploadFile"/>, the source file ID.
        /// The original user file is never modified; this is an independent copy.
        /// </summary>
        public Guid? SourceUploadFileId { get; set; }

        public DateTime DateCreated { get; set; }
        public DateTime? DateUpdated { get; set; }
        public Guid CreatedByAppUserId { get; set; }
        public Guid? UpdatedByAppUserId { get; set; }

        public virtual Organization Organization { get; set; } = null!;
        public virtual UploadFileType UploadFileType { get; set; } = null!;

        /// <summary>The user's file this was copied from, or null if uploaded directly to the org.</summary>
        public virtual UploadFile? SourceUploadFile { get; set; }

        public virtual AppUser CreatedByAppUser { get; set; } = null!;
        public virtual AppUser? UpdatedByAppUser { get; set; }
        public virtual AppUser? PublishedByAppUser { get; set; }
    }
}
