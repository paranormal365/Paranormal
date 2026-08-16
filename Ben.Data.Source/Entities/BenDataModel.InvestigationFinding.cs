namespace Ben.Data.Source.Entities
{
    /// <summary>
    /// What one person who was there says happened.
    /// </summary>
    /// <remarks>
    /// <para>Deliberately not the same thing as <c>Investigation.Notes</c>, which is one field for
    /// the whole visit and can only be changed by whoever manages it. Four people in a building at
    /// night see four different nights, and flattening that into a single account — written by
    /// whoever happens to hold the edit right — loses the disagreements, which are often the most
    /// useful part of the record.</para>
    ///
    /// <para><b>Only the attendee writes their own.</b> There is no override here, unlike arrival,
    /// where a manager may record that somebody turned up. Attendance is an observable fact about a
    /// person; an account of what they experienced is not, and a record saying "Sarah saw a shadow"
    /// that Sarah did not write would be worse than no record. Somebody who was not there has
    /// nothing to file.</para>
    ///
    /// <para>One per person per investigation, enforced by a unique index, and editable
    /// afterwards: these get written the next morning, and re-read and corrected for days.</para>
    /// </remarks>
    public class InvestigationFinding
    {
        public Guid Id { get; set; }
        public Guid InvestigationId { get; set; }

        /// <summary>The person whose account this is. Always the author — see the type remarks.</summary>
        public Guid AppUserId { get; set; }

        /// <summary>Their account of the visit.</summary>
        public string Narrative { get; set; } = string.Empty;

        /// <summary>
        /// When they last said it, as opposed to when the row appeared.
        /// </summary>
        /// <remarks>
        /// Kept beside <see cref="DateCreated"/> because a first draft on the night and a revision
        /// a week later are different claims, and a reader comparing accounts wants to know which
        /// they are looking at.
        /// </remarks>
        public DateTime? DateUpdated { get; set; }

        public DateTime DateCreated { get; set; }
        public Guid CreatedByAppUserId { get; set; }

        public virtual Investigation Investigation { get; set; } = null!;
        public virtual AppUser AppUser { get; set; } = null!;
        public virtual AppUser CreatedByAppUser { get; set; } = null!;
    }
}
