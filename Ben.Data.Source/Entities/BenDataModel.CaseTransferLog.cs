using Ben.Data.Common.Enums;

namespace Ben.Data.Source.Entities
{
    /// <summary>
    /// Records a proposal to transfer a case from one organization to another.
    /// The receiving org must accept; on acceptance the Case.OrganizationId is updated.
    /// </summary>
    /// <remarks>
    /// Two proposers share this log (item 84): an organization handing a case on, and a
    /// <b>client</b> moving their own paused case to a new group. The client variant carries the
    /// per-category consent — the two-key shape: the client's proposal is one key, the receiving
    /// organization's acceptance the other, and the ORIGINAL group's permission is deliberately
    /// not required, because findings are dual-owned by the group and the client.
    /// </remarks>
    public class CaseTransferLog
    {
        public Guid Id { get; set; }
        public Guid CaseId { get; set; }
        public Guid FromOrganizationId { get; set; }
        public Guid ToOrganizationId { get; set; }
        public Guid ProposedByAppUserId { get; set; }
        public Guid? RespondedByAppUserId { get; set; }
        public CaseTransferStatus Status { get; set; } = CaseTransferStatus.Pending;
        public string? TransferReason { get; set; }
        public string? RejectionReason { get; set; }

        /// <summary>True when the CLIENT proposed this — their paused case, their move.</summary>
        /// <remarks>Explicit rather than inferred from the proposer's memberships: whether the
        /// consent flags below mean anything depends on it, and inference breaks the day someone
        /// is both a member somewhere and a client somewhere else.</remarks>
        public bool ProposedByClient { get; set; }

        /// <summary>
        /// Client's consent: may the receiving group see the history collected so far?
        /// </summary>
        /// <remarks>
        /// Meaningful only when <see cref="ProposedByClient"/>. False at acceptance re-scopes the
        /// pre-move timeline to the client alone — the entries survive, the client keeps them,
        /// the new group starts from the client's own retelling.
        /// </remarks>
        public bool ShareHistory { get; set; } = true;

        /// <summary>
        /// Client's consent: may the receiving group see the original group's investigations?
        /// </summary>
        /// <remarks>
        /// False at acceptance detaches those investigations from the case — they remain the
        /// original group's flat records ("findings remain the original group's"), and simply do
        /// not travel. True leaves them attached: the new group reads them through the case while
        /// the original group keeps them in its own list — dual visibility, matching dual
        /// ownership, with no copy made.
        /// </remarks>
        public bool ShareInvestigations { get; set; } = true;
        public DateTime DateProposed { get; set; }
        public DateTime? DateResponded { get; set; }

        public virtual Case Case { get; set; } = null!;
        public virtual Organization FromOrganization { get; set; } = null!;
        public virtual Organization ToOrganization { get; set; } = null!;
        public virtual AppUser ProposedByAppUser { get; set; } = null!;
        public virtual AppUser? RespondedByAppUser { get; set; }
    }
}
