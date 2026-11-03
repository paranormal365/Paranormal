using Ben.Data.Common.Interfaces;

namespace Ben.Data.Source.Entities
{
    /// <summary>
    /// A promotion for new groups: the founding groups (Ben, 11/02/2026).
    /// </summary>
    /// <remarks>
    /// <para><b>Ben's rules.</b> The first ten groups that qualify get a free year on the plan their size earns,
    /// and can grow for free all year. A group has a week to gather its members. A member counts only once their
    /// email is confirmed, only if they are new or in no other group, and only for one group. The three most
    /// active groups after their year get a second year free. "Most active" is field sessions, investigations,
    /// events and evidence, per member.</para>
    ///
    /// <para><b>Every number is here, not in code</b>, so Ben can set them on the admin page before opening it.
    /// Nothing shows anywhere while <see cref="IsOpen"/> is false.</para>
    /// </remarks>
    public partial class Promotion : IAuditableEntity
    {
        public Guid Id { get; set; }

        /// <summary>A stable key, "founding-groups".</summary>
        public string Key { get; set; } = string.Empty;

        public string Name { get; set; } = string.Empty;

        /// <summary>Switched on by an administrator. Off: no page, notice, shout-out or claim.</summary>
        public bool IsOpen { get; set; }

        /// <summary>When it was first opened; the claim window counts from here.</summary>
        public DateTime? OpenedUtc { get; set; }

        /// <summary>How long claims stay open after opening, unless the spots fill first.</summary>
        public int ClaimWindowDays { get; set; } = 90;

        public int Spots { get; set; } = 10;
        public int GatherDays { get; set; } = 7;
        public int MinimumMembers { get; set; } = 2;
        public int FreeMonths { get; set; } = 12;
        public int Winners { get; set; } = 3;
        public int WinnerMonths { get; set; } = 12;
        public int GroupOfTheMonthMonths { get; set; } = 1;
        public int ReachedScoreMonths { get; set; } = 3;

        /// <summary>The per-member score over a group's twelve months that earns <see cref="ReachedScoreMonths"/>.</summary>
        public int ReachableScorePerMember { get; set; } = 60;

        public int PointsPerSession { get; set; } = 2;
        public int PointsPerInvestigation { get; set; } = 5;
        public int PointsPerEvent { get; set; } = 10;
        public int PointsPerEvidence { get; set; } = 3;
        public int PointsPerReferral { get; set; } = 10;

        /// <summary>A field session counts only with at least this many readings, so an empty one earns nothing.</summary>
        public int MinimumReadingsPerSession { get; set; } = 10;

        /// <summary>The start of the last calendar month whose group of the month was decided (even with no winner).</summary>
        public DateTime? GroupOfTheMonthDecidedThroughUtc { get; set; }

        /// <summary>When the top groups were decided and their second year given.</summary>
        public DateTime? DecidedUtc { get; set; }

        public DateTime DateCreated { get; set; }
        public DateTime? DateUpdated { get; set; }
        public Guid CreatedByAppUserId { get; set; }
        public Guid? UpdatedByAppUserId { get; set; }

        public virtual ICollection<PromotionEntry> Entries { get; set; } = [];
    }
}
