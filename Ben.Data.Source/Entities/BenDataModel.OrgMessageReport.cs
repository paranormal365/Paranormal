using Ben.Data.Common.Enums;

namespace Ben.Data.Source.Entities
{
    /// <summary>
    /// Somebody telling an administrator that a post should not be there.
    /// </summary>
    /// <remarks>
    /// <para>Reports do not hide anything by themselves, and no number of them does. Hiding is an
    /// administrator's decision, recorded on the post — otherwise a group of people who dislike a
    /// post could remove it between them, which is a moderation system that moderates whoever is
    /// least popular rather than whatever breaks the rules.</para>
    ///
    /// <para>Kept after resolution rather than deleted. A dismissed report is the evidence that
    /// somebody looked, and the pattern across reports — the same author reported repeatedly, or
    /// the same reporter reporting everybody — is exactly what an administrator needs and what
    /// deleting the resolved ones would destroy.</para>
    /// </remarks>
    public partial class OrgMessageReport
    {
        public Guid Id { get; set; }

        public Guid OrgMessageId { get; set; }

        /// <summary>Who reported it.</summary>
        public Guid ReportedByAppUserId { get; set; }

        /// <summary>Why, in the reporter's own words. Optional — a report with no reason is still a signal.</summary>
        public string? Reason { get; set; }

        public FeedReportOutcome Outcome { get; set; } = FeedReportOutcome.Pending;

        /// <summary>When an administrator decided. Null while <see cref="Outcome"/> is Pending.</summary>
        public DateTime? ResolvedUtc { get; set; }

        /// <summary>Which administrator decided.</summary>
        public Guid? ResolvedByAppUserId { get; set; }

        public DateTime DateCreated { get; set; }

        public virtual OrgMessage OrgMessage { get; set; } = null!;
        public virtual AppUser ReportedByAppUser { get; set; } = null!;
        public virtual AppUser? ResolvedByAppUser { get; set; }
    }
}
