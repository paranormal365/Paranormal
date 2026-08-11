using Ben.Data.Common.Interfaces;

namespace Ben.Data.Source.Entities
{
    /// <summary>
    /// A single comment posted on an UploadFile (item #6 phase 2). Posting requires the author to
    /// be the file's owner, or to match at least one audience the owner has enabled via the
    /// <c>Allow*Comments</c> toggles on <see cref="UploadFile"/> AND actually have that audience's
    /// access to the file at all (see <c>FileAudienceAccess</c>) — toggles turn on discussion for an
    /// audience that already has visibility, they are not a second, independent grant.
    /// </summary>
    /// <remarks>
    /// Free-form user content, not an access grant — hard-deleted (matching
    /// <see cref="UploadFileRegionNote"/>), unlike <see cref="UploadFileShare"/> which soft-deletes
    /// because it needs a "who revoked it and when" trail.
    /// </remarks>
    public class UploadFileComment : IAuditableEntity
    {
        public Guid Id { get; set; }
        public Guid UploadFileId { get; set; }
        public Guid AuthorAppUserId { get; set; }
        public string Text { get; set; } = null!;

        // ── Audience snapshot, frozen at post time ──────────────────────────
        // Multiple bools (not one enum) because an author can match more than one audience at
        // once (e.g. an investigation-team member who is also the case's client) — same reasoning
        // as EvidenceVote.IsVoterCaseOrgMember/IsVoterCaseClient. Never recomputed on edit.
        public bool IsOwner { get; set; }
        public bool IsInvestigationTeamMember { get; set; }
        public bool IsClient { get; set; }
        public bool IsOrganizationMember { get; set; }
        public bool IsPublicCommenter { get; set; }

        public DateTime DateCreated { get; set; }
        public DateTime? DateUpdated { get; set; }
        public Guid CreatedByAppUserId { get; set; }
        public Guid? UpdatedByAppUserId { get; set; }

        public virtual UploadFile UploadFile { get; set; } = null!;
        public virtual AppUser AuthorAppUser { get; set; } = null!;
        public virtual AppUser CreatedByAppUser { get; set; } = null!;
        public virtual AppUser? UpdatedByAppUser { get; set; }
    }
}
