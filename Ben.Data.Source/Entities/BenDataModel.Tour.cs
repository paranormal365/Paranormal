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

        /// <summary>
        /// The readable part of this tour's public URL — <c>/o/{business}/tours/{UrlName}</c>.
        /// </summary>
        /// <remarks>
        /// Generated from the name when the tour is created and then left alone, the same rule a
        /// public event follows: a slug that chased a renamed tour would break every link already
        /// shared, and sharing the link is the point.
        /// </remarks>
        public string UrlName { get; set; } = null!;

        /// <summary>What a guest is told about it, cleaned like any other public markup.</summary>
        public string? Description { get; set; }

        /// <summary>Where it starts: one of the business's own addresses. Required.</summary>
        public Guid StartOrganizationAddressId { get; set; }

        /// <summary>Typical running time, so a scheduled date can default its end.</summary>
        public int? DurationMinutes { get; set; }

        /// <summary>How many people a date takes by default; a date may say otherwise.</summary>
        public int? DefaultCapacity { get; set; }

        /// <summary>
        /// The zone the tour actually happens in, as an IANA id ("America/Chicago").
        /// </summary>
        /// <remarks>
        /// A guest is told "Saturday at 7:00 PM", and that sentence is only true in one zone. The
        /// site stores times in UTC and the reminder mail used to print raw UTC with a " UTC"
        /// suffix, which is exactly the wrong thing to hand somebody deciding when to leave home.
        /// The zone belongs to the tour rather than the reader because the meeting point does not
        /// move when the reader does.
        /// </remarks>
        public string TimeZoneId { get; set; } = "America/Chicago";

        /// <summary>
        /// Whether guests may leave a rating and a few words after a date they came to.
        /// </summary>
        /// <remarks>On by default (Ben, 2026-09-10) and switchable per tour.</remarks>
        public bool AllowReviews { get; set; } = true;

        /// <summary>Whether new dates may be scheduled and signed up for.</summary>
        /// <remarks>
        /// The pause a retirement is too strong for: a season ending, a street closed for works.
        /// Dates already scheduled stand; a retired tour is the one that stops being counted.
        /// </remarks>
        public bool IsBookable { get; set; } = true;

        /// <summary>
        /// How the business wants to be reached, and how the money is settled with them.
        /// </summary>
        /// <remarks>
        /// Ben, 2026-09-10: "Money collected is to be arranged by the tour company or person."
        /// Nothing is taken through the site, so this free text — a phone number, an address to
        /// pay at, "cash on the night" — is the whole of our part in it, carried into the guest
        /// mail as <c>{{business.contact}}</c>.
        /// </remarks>
        public string? ContactLine { get; set; }

        /// <summary>The subject line of the mail a guest gets. Null uses the built-in wording.</summary>
        public string? MailSubjectTemplate { get; set; }

        /// <summary>
        /// The body of the mail a guest gets, with <c>{{placeholders}}</c>. Null uses the built-in.
        /// </summary>
        /// <remarks>
        /// Written by the business, sanitized like any other authored markup, and rendered for
        /// both the sign-up confirmation and the reminder. A body that leaves out the meeting
        /// point still gets one appended: Ben's rule is that the start address is in the email,
        /// and a guest who cannot tell where to stand has not been told about the tour.
        /// </remarks>
        public string? MailBodyTemplate { get; set; }

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

        /// <summary>Who leads this tour, by default, when a date is scheduled.</summary>
        public virtual ICollection<TourGuide> Guides { get; set; } = new List<TourGuide>();
    }
}
