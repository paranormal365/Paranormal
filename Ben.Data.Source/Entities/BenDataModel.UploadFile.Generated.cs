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

        /// <summary>Fabric.js JSON snapshot — set when the file was saved from the image editor so it can be re-opened.</summary>
        public string? EditStateJson { get; set; }

        /// <summary>True when this file is an edited copy produced by the image editor; ParentFileId points to the original.</summary>
        public bool IsEditedVersion { get; set; }

        // ── Comment settings (item #6 phase 2) ──────────────────────────────
        // Owner-controlled, independently toggleable per audience. Posting requires BOTH the
        // corresponding toggle here AND an active Phase-1 share/link granting that audience
        // visibility of the file at all — see FileAudienceAccess.
        public bool AllowInvestigationTeamComments { get; set; }
        public bool AllowClientComments { get; set; }
        public bool AllowOrganizationComments { get; set; }
        public bool AllowPublicComments { get; set; }

        /// <summary>
        /// When this file is an independent byte-copy made for a case's Files tab (copy-on-attach,
        /// item #6 phase 2), the source file it was copied from. Deliberately separate from
        /// <see cref="ParentFileId"/> (clip/edit-version lineage) — <c>UploadFileController.GetChildClips</c>
        /// queries ParentFileId with no type filter, so conflating the two would surface case copies
        /// as selectable "clips" of the source file in that endpoint.
        /// </summary>
        public Guid? CaseCopyOfUploadFileId { get; set; }

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
        public virtual ICollection<AudioMarker> AudioMarkers { get; set; } = new List<AudioMarker>();
        public virtual ICollection<CaseFile> CaseFiles { get; set; } = new List<CaseFile>();
        public virtual ICollection<UploadFileShare> Shares { get; set; } = new List<UploadFileShare>();
        public virtual ICollection<UploadFileComment> Comments { get; set; } = new List<UploadFileComment>();
        public virtual UploadFile? CaseCopySourceFile { get; set; }
        public virtual ICollection<UploadFile> CaseCopies { get; set; } = new List<UploadFile>();
    }
}
