using Ben.Data.Common.Enums;
using Ben.Data.Common.Interfaces;

namespace Ben.Data.Source.Entities
{
    /// <summary>One group's place in a promotion (11/02/2026). One per group per promotion.</summary>
    public partial class PromotionEntry : IAuditableEntity
    {
        public Guid Id { get; set; }
        public Guid PromotionId { get; set; }
        public Guid OrganizationId { get; set; }

        public PromotionEntryState State { get; set; }

        public DateTime ClaimedUtc { get; set; }
        public Guid ClaimedByAppUserId { get; set; }

        /// <summary>When its week to gather members ends.</summary>
        public DateTime GatherEndsUtc { get; set; }

        /// <summary>When it qualified; its own twelve months, and its score, count from here.</summary>
        public DateTime? QualifiedUtc { get; set; }

        /// <summary>When its free year (and its contest) ends.</summary>
        public DateTime? FreeYearEndsUtc { get; set; }

        /// <summary>Counted members when it qualified, and the plan that earned.</summary>
        public int MembersAtQualifying { get; set; }
        public Guid? SubscriptionTierIdEarned { get; set; }

        /// <summary>The group's score per member over its twelve months, frozen when they end.</summary>
        public decimal? FinalScorePerMember { get; set; }
        public decimal? FinalScoreTotal { get; set; }

        /// <summary>Its place among the groups when the promotion was decided.</summary>
        public int? FinalRank { get; set; }

        public string? DisqualifiedReason { get; set; }
        public DateTime? DisqualifiedUtc { get; set; }

        /// <summary>Letters sent, so each goes once: "a day left", and the free year ending a month and a week out.</summary>
        public DateTime? DayLeftSentUtc { get; set; }
        public DateTime? YearEndMonthNoticeSentUtc { get; set; }
        public DateTime? YearEndWeekNoticeSentUtc { get; set; }

        public DateTime DateCreated { get; set; }
        public DateTime? DateUpdated { get; set; }
        public Guid CreatedByAppUserId { get; set; }
        public Guid? UpdatedByAppUserId { get; set; }

        public virtual Promotion Promotion { get; set; } = null!;
        public virtual Organization Organization { get; set; } = null!;
        public virtual SubscriptionTier? SubscriptionTierEarned { get; set; }
        public virtual ICollection<PromotionMember> Members { get; set; } = [];
    }

    /// <summary>
    /// A person counted for one group in a promotion. Unique per person per promotion, so nobody is counted twice
    /// and nobody counted can start a second free group (Ben, 11/02/2026).
    /// </summary>
    public partial class PromotionMember
    {
        public Guid Id { get; set; }
        public Guid PromotionId { get; set; }
        public Guid PromotionEntryId { get; set; }
        public Guid AppUserId { get; set; }
        public DateTime CountedUtc { get; set; }

        public virtual PromotionEntry PromotionEntry { get; set; } = null!;
        public virtual AppUser AppUser { get; set; } = null!;
    }
}
