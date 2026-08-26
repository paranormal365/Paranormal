namespace Ben.Data.Source.Entities
{
    /// <summary>
    /// One member's vote on whether their group should take on a client's request.
    /// </summary>
    /// <remarks>
    /// <para><b>Advisory, deliberately.</b> The vote informs the person who holds the accept
    /// grant; it does not accept anything on its own. Ben's rule (2026-08-26): any group who
    /// accepts first wins — so the decision stays a deliberate act by someone accountable, with
    /// the tally in front of them.</para>
    ///
    /// <para>Keyed by the APPLICATION (<see cref="ClientRequestOrganization"/>), not the request:
    /// several groups may be reviewing the same request at once, and each group's ballot is its
    /// own. Mirrors <see cref="MembershipReviewVote"/>, the existing committee-vote shape.</para>
    /// </remarks>
    public partial class ClientRequestReviewVote
    {
        public Guid Id { get; set; }
        public Guid ClientRequestOrganizationId { get; set; }
        public Guid VoterAppUserId { get; set; }

        /// <summary>True = take the case, false = pass.</summary>
        public bool InFavor { get; set; }
        public string? Comment { get; set; }
        public DateTime DateVoted { get; set; }

        public virtual ClientRequestOrganization ClientRequestOrganization { get; set; } = null!;
        public virtual AppUser VoterAppUser { get; set; } = null!;
    }
}
