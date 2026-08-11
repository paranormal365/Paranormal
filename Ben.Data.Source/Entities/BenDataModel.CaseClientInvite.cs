using Ben.Data.Common.Interfaces;

namespace Ben.Data.Source.Entities
{
    /// <summary>
    /// An email invite from a case's primary client, inviting someone with no account yet to
    /// register and be linked to the case as a co-client (item #4's remaining piece —
    /// <see cref="CaseClientAccess"/> already covers people who already have an account).
    /// </summary>
    /// <remarks>
    /// Status is derived from the three nullable fields below rather than a companion enum that
    /// could disagree with them — pending means <see cref="DateAccepted"/> and
    /// <see cref="DateRevoked"/> are both null and <see cref="DateExpires"/> is still in the
    /// future. <see cref="Token"/> is stored raw (not hashed) so the primary client's "Copy Link"
    /// action works after creation, not just at send time — copy-link is a primary delivery path
    /// here, not a fallback for a broken email server.
    /// </remarks>
    public class CaseClientInvite : IAuditableEntity
    {
        public Guid Id { get; set; }
        public Guid CaseId { get; set; }

        public string Email { get; set; } = null!;

        /// <summary>Opaque, unguessable token embedded in the invite link. Unique.</summary>
        public string Token { get; set; } = null!;

        public DateTime DateExpires { get; set; }
        public DateTime? DateAccepted { get; set; }
        public DateTime? DateRevoked { get; set; }

        /// <summary>The AppUser who accepted — set once, at acceptance.</summary>
        public Guid? AcceptedByAppUserId { get; set; }

        public DateTime DateCreated { get; set; }
        public DateTime? DateUpdated { get; set; }
        public Guid CreatedByAppUserId { get; set; }
        public Guid? UpdatedByAppUserId { get; set; }

        public virtual Case Case { get; set; } = null!;
        public virtual AppUser? AcceptedByAppUser { get; set; }
        public virtual AppUser CreatedByAppUser { get; set; } = null!;
        public virtual AppUser? UpdatedByAppUser { get; set; }
    }
}
