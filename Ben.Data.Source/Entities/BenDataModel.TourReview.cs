using Ben.Data.Common.Interfaces;

namespace Ben.Data.Source.Entities
{
    /// <summary>
    /// What somebody who took a tour thought of it (item 233).
    /// </summary>
    /// <remarks>
    /// <para>Ben chose, 2026-09-10, that reviews are offered per tour and on by default. Only a
    /// person who was <b>accepted on a date of that tour which has already finished</b> may leave
    /// one — a rating from somebody who never turned up is not a review of anything, and the rule
    /// is the same one that already gates submitting evidence to an event.</para>
    ///
    /// <para><b>One per guest per tour</b>, editable, because a guest who walks the same tour
    /// three times has one opinion of it, not three. The date it is attached to is the one they
    /// came to when they first wrote it, kept so a reader can see how recent the visit was.</para>
    ///
    /// <para><b>The business may hide a review, never edit it.</b> Editing somebody's words while
    /// keeping their name on them is the one thing a review system must not allow; hiding is
    /// visible as an absence and reversible.</para>
    /// </remarks>
    public partial class TourReview : IAuditableEntity
    {
        public Guid Id { get; set; }
        public Guid TourId { get; set; }

        /// <summary>The date they came to when they wrote it.</summary>
        public Guid OrgCalendarEventId { get; set; }

        public Guid AppUserId { get; set; }

        /// <summary>One to five.</summary>
        public int Stars { get; set; }

        /// <summary>A few words, optional — a rating alone is a review.</summary>
        public string? Comment { get; set; }

        /// <summary>Set when the business hides it. The words are never changed.</summary>
        public DateTime? HiddenAtUtc { get; set; }
        public Guid? HiddenByAppUserId { get; set; }

        public DateTime DateCreated { get; set; }
        public DateTime? DateUpdated { get; set; }
        public Guid CreatedByAppUserId { get; set; }
        public Guid? UpdatedByAppUserId { get; set; }

        public virtual Tour Tour { get; set; } = null!;
        public virtual OrgCalendarEvent OrgCalendarEvent { get; set; } = null!;
        public virtual AppUser AppUser { get; set; } = null!;
        public virtual AppUser CreatedByAppUser { get; set; } = null!;
        public virtual AppUser? UpdatedByAppUser { get; set; }
    }
}
