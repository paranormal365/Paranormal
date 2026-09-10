using Ben.Data.Common.Interfaces;

namespace Ben.Data.Source.Entities
{
    /// <summary>
    /// A tour a business sells: the product, not a date on which it runs (item 233).
    /// </summary>
    /// <remarks>
    /// <para><b>Ben, 2026-09-10:</b> "The $29 per month is for a single tour no matter how many
    /// times scheduled. If they have a tour on one street and need another tour for another
    /// street, that is a different tour." So the tour is the thing the business plan counts, and a
    /// scheduled date is an <see cref="OrgCalendarEvent"/> that names one.</para>
    ///
    /// <para><b>What makes two tours different.</b> Ben fixed the first half: the start location,
    /// which is required. The second half is the <see cref="Name"/>, unique within the business
    /// without regard to case. Two tours may leave from the same corner as long as they are called
    /// different things; the same name from the same corner is the same tour, scheduled again.
    /// The name rather than the route, because a route is a description a business will not
    /// always write and the site cannot compare, while a name is what the business already calls
    /// the thing on its own leaflet.</para>
    ///
    /// <para><b>Retiring, not deleting.</b> A tour that has run has dates and sign-ups behind it.
    /// Retiring stops new dates and drops it from the next renewal's count; nothing that happened
    /// is touched.</para>
    /// </remarks>
    public partial class Tour : IAuditableEntity
    {
        public Guid Id { get; set; }
        public Guid OrganizationId { get; set; }

        /// <summary>What the business calls it. Required; unique within the business.</summary>
        public string Name { get; set; } = null!;

        /// <summary>What a guest is told about it, cleaned like any other public markup.</summary>
        public string? Description { get; set; }

        /// <summary>Where it starts: one of the business's own addresses. Required.</summary>
        public Guid StartOrganizationAddressId { get; set; }

        /// <summary>Typical running time, so a scheduled date can default its end.</summary>
        public int? DurationMinutes { get; set; }

        /// <summary>How many people a date takes by default; a date may say otherwise.</summary>
        public int? DefaultCapacity { get; set; }

        /// <summary>Set when the business stops running it. Retired tours take no new dates.</summary>
        public DateTime? RetiredAtUtc { get; set; }

        public bool IsActive => RetiredAtUtc is null;

        public DateTime DateCreated { get; set; }
        public DateTime? DateUpdated { get; set; }
        public Guid CreatedByAppUserId { get; set; }
        public Guid? UpdatedByAppUserId { get; set; }

        public virtual Organization Organization { get; set; } = null!;
        public virtual OrganizationAddress StartOrganizationAddress { get; set; } = null!;
        public virtual AppUser CreatedByAppUser { get; set; } = null!;
        public virtual AppUser? UpdatedByAppUser { get; set; }
        public virtual ICollection<OrgCalendarEvent> Dates { get; set; } = new List<OrgCalendarEvent>();
    }
}
