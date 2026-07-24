namespace Ben.Data.Source.Entities
{
    /// <summary>
    /// Defines an organization's operating area using a radius from a center point.
    /// The center coordinates are PRIVATE — never exposed publicly to protect home addresses.
    /// Only <see cref="DisplayLabel"/> and <see cref="RadiusMiles"/> are shown to the public.
    /// One-to-one with <see cref="Organization"/>.
    /// </summary>
    public partial class OrganizationAreaOfOperation
    {
        public Guid OrganizationId { get; set; }

        /// <summary>Radius of the operating area in miles from the center point.</summary>
        public decimal RadiusMiles { get; set; }

        /// <summary>
        /// Latitude of the center of the operating area.
        /// PRIVATE — never returned by public-facing API endpoints.
        /// Should be a city/town center, NOT a personal address.
        /// </summary>
        public decimal CenterLatitude { get; set; }

        /// <summary>
        /// Longitude of the center of the operating area.
        /// PRIVATE — never returned by public-facing API endpoints.
        /// Should be a city/town center, NOT a personal address.
        /// </summary>
        public decimal CenterLongitude { get; set; }

        /// <summary>
        /// Human-readable label shown to the public describing the operating area
        /// (e.g. "Within 30 miles of Nashville, TN").
        /// </summary>
        public string? DisplayLabel { get; set; }

        public DateTime DateCreated { get; set; }
        public DateTime? DateUpdated { get; set; }
        public Guid CreatedByAppUserId { get; set; }
        public Guid? UpdatedByAppUserId { get; set; }

        public virtual Organization Organization { get; set; } = null!;
        public virtual AppUser CreatedByAppUser { get; set; } = null!;
        public virtual AppUser? UpdatedByAppUser { get; set; }
    }
}
