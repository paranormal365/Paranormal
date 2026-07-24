using Ben.Data.Common.Interfaces;

namespace Ben.Data.Source.Entities
{
    /// <summary>
    /// Immutable audit record created whenever an <see cref="OrganizationFile"/> is deleted.
    /// Intentionally denormalized (no FKs to any other tables) so the log survives even if
    /// the organization, file type, or deleting user is later removed — same pattern as AuditLog.
    /// </summary>
    public partial class OrganizationFileDeleteLog : IIDStd
    {
        public Guid Id { get; set; }

        // ── Organization snapshot ──────────────────────────────────────────────
        public Guid OrganizationId { get; set; }
        public string OrganizationName { get; set; } = null!;

        // ── File snapshot ─────────────────────────────────────────────────────
        /// <summary>The <see cref="OrganizationFile.Id"/> that was deleted.</summary>
        public Guid OriginalFileId { get; set; }
        public string FileName { get; set; } = null!;
        public string ContentType { get; set; } = null!;
        public long FileSize { get; set; }
        /// <summary>Storage path that was removed from disk (for reference).</summary>
        public string? StoragePath { get; set; }
        /// <summary>Source user file ID if the deleted file was a shared copy.</summary>
        public Guid? SourceUploadFileId { get; set; }
        public bool WasPublic { get; set; }
        /// <summary>Who had approved the file for public access, or null if it was never published.</summary>
        public Guid? WasPublishedByAppUserId { get; set; }
        public string? WasPublishedByDisplayName { get; set; }
        public DateTime? WasDatePublished { get; set; }

        // ── Deletion audit ─────────────────────────────────────────────────────
        /// <summary>ID of the user who performed the deletion (stored as data; no FK).</summary>
        public Guid DeletedByAppUserId { get; set; }
        /// <summary>Display name of the deleting user at the moment of deletion.</summary>
        public string DeletedByDisplayName { get; set; } = null!;
        public DateTime DateDeleted { get; set; }
    }
}
