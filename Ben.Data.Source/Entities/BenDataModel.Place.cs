using Ben.Data.Common.Enums;
using Ben.Data.Common.Interfaces;

namespace Ben.Data.Source.Entities
{
    /// <summary>
    /// A physical location that cases and investigations point at.
    /// </summary>
    /// <remarks>
    /// <para>A place is not a case. A case carries a client, a request, an owning organization and
    /// a privacy model; a visit to a landmark carries none of those. Modelling the landmark as a
    /// case without a client would put an implicit "and not one of those" into every existing case
    /// query in the codebase, and the first one anybody forgot would leak.</para>
    ///
    /// <para>Every address field is nullable, unlike <see cref="Case"/>'s. A famous bridge may have
    /// a name and a pair of coordinates and no street address at all, and demanding a ZIP for it
    /// would only produce invented ones.</para>
    ///
    /// <para>The same place is expected to accumulate visits from several organizations over years.
    /// That is the point — it is what makes "N investigations by M groups since Y" answerable — and
    /// it is also why deduplication matters (P8) and why nothing here assumes one owner.</para>
    /// </remarks>
    public partial class Place : IAuditableEntity
    {
        public Guid Id { get; set; }

        /// <summary>
        /// What people call it — "The Bell Witch Cave". Null for an ordinary address that nobody
        /// has named.
        /// </summary>
        public string? Name { get; set; }

        public string? StreetAddress1 { get; set; }
        public string? StreetAddress2 { get; set; }
        public string? City { get; set; }
        public string? State { get; set; }
        public string? ZipCode { get; set; }
        public string? Country { get; set; }

        public decimal? Latitude { get; set; }
        public decimal? Longitude { get; set; }

        /// <summary>
        /// Why this place has no coordinates, or null when it has them.
        /// </summary>
        /// <remarks>
        /// Recorded rather than left as a silent pair of nulls, for the same reason as on
        /// <see cref="Investigation"/>: a missing dot on a map is otherwise indistinguishable from
        /// a place nobody has visited, and somebody has to be able to see that the address simply
        /// could not be found and go fix it.
        /// </remarks>
        public string? GeocodeNote { get; set; }

        /// <summary>When the coordinates were last resolved.</summary>
        public DateTime? DateGeocoded { get; set; }

        /// <summary>
        /// Decides the default sharing scope for investigations here. See
        /// <see cref="PlaceKind"/> for why the two values differ in the safe direction.
        /// </summary>
        public PlaceKind Kind { get; set; } = PlaceKind.PrivateResidence;

        /// <summary>
        /// Reserved for a future curation step over user-created public places. **Nothing reads it
        /// yet.**
        /// </summary>
        /// <remarks>
        /// Scaffolded deliberately and documented as unused, because this codebase already has a
        /// cautionary example: <c>ExperienceCategory</c> carries IsApproved, ProposedBy and
        /// ApprovedBy columns while every write path hardcodes approval, so no one can propose
        /// anything and there is no queue. A flag that looks like a workflow but is not one is
        /// worse than no flag. Whether curation happens at all is still an open question on this
        /// branch — until it is answered, this column stays inert and says so.
        /// </remarks>
        public bool IsApproved { get; set; }

        public DateTime DateCreated { get; set; }
        public DateTime? DateUpdated { get; set; }
        public Guid CreatedByAppUserId { get; set; }
        public Guid? UpdatedByAppUserId { get; set; }

        public virtual AppUser CreatedByAppUser { get; set; } = null!;
        public virtual AppUser? UpdatedByAppUser { get; set; }

        public virtual ICollection<Case> Cases { get; set; } = new List<Case>();
        public virtual ICollection<Investigation> Investigations { get; set; } = new List<Investigation>();
    }
}
