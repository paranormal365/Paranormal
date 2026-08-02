using Ben.Data.Common.Enums;

namespace Ben.Data.Source.Entities
{
    /// <summary>
    /// A community vote cast by a registered user on a public <see cref="Case"/>.
    /// </summary>
    /// <remarks>
    /// Case votes let the public rate whether a case's paranormal activity is credible.
    /// They differ from <see cref="EvidenceVote"/> records, which target individual
    /// evidence files inside a case timeline.
    /// <br/>
    /// A user may hold at most one vote per case (unique DB index on CaseId + VoterAppUserId);
    /// casting a second vote upserts the existing row.
    /// <br/>
    /// Voter identity is never returned on public responses — the API returns
    /// <see cref="Ben.Service.Models.Entities.CaseVoteSummary"/> aggregate counts only,
    /// with <c>CurrentUserVote</c> included only for authenticated callers.
    /// <br/>
    /// Consumed by: <c>PublicCaseVoteController</c>,
    /// <c>CaseVoteWidget.razor</c>, <c>PublicCaseDiscovery.razor</c> map popup.
    /// </remarks>
    public class CaseVote
    {
        /// <summary>Primary key (Guid, generated on add).</summary>
        public Guid Id { get; set; }

        /// <summary>
        /// FK to the case being rated. Only cases where
        /// <see cref="Case.IsPublic"/> is <c>true</c> and status is
        /// <c>Public</c> or <c>Haunted</c> are voteable.
        /// </summary>
        public Guid CaseId { get; set; }

        /// <summary>
        /// FK to the authenticated user who cast the vote.
        /// Anonymous visitors may view aggregate counts but cannot vote.
        /// </summary>
        public Guid VoterAppUserId { get; set; }

        /// <summary>
        /// Reuses <see cref="EvidenceVoteType"/> for consistency with the
        /// evidence-voting UI: Confirms / Disputes / Inconclusive.
        /// </summary>
        public EvidenceVoteType VoteType { get; set; }

        /// <summary>
        /// Optional supporting comment (max 1 000 chars, enforced by model config).
        /// Stored but not yet surfaced on public endpoints.
        /// </summary>
        public string? Comment { get; set; }

        /// <summary>UTC timestamp; updated each time the user changes their vote.</summary>
        public DateTime DateVoted { get; set; }

        /// <summary>Navigation to the parent case.</summary>
        public virtual Case Case { get; set; } = null!;

        /// <summary>Navigation to the voter's app-user record.</summary>
        public virtual AppUser VoterAppUser { get; set; } = null!;
    }
}

