using Ben.Data.Common.Enums;
using Ben.Data.Common.Interfaces;

namespace Ben.Data.Source.Entities
{
    /// <summary>
    /// A group saying "this place is our venue" (item 235 phase 9).
    /// </summary>
    /// <remarks>
    /// <para><b>A place still has no owner.</b> Places are shared by every group that has ever
    /// investigated or run an event at one, and a hotel's building does not stop being where a
    /// paranormal society went in 2019 because the hotel joined. So the venue is a row BESIDE the
    /// place, one per group per place, not a column on it.</para>
    ///
    /// <para><b>Only a verified profile has any power.</b> Anybody may describe a place as their
    /// venue — a society that rents the same hall every October has a perfectly good reason to — but
    /// only a profile whose claim was proved (<see cref="VerifiedUtc"/>) makes other groups ask
    /// before publishing there. Without that, a competitor could write a profile for the Thomas
    /// House and stop every organizer in Ohio at a stroke. At most one profile per place is
    /// verified, enforced by a filtered unique index.</para>
    /// </remarks>
    public class OrganizationVenueProfile : IAuditableEntity
    {
        public Guid Id { get; set; }
        public Guid OrganizationId { get; set; }
        public Guid PlaceId { get; set; }

        /// <summary>The building's story, shown on the venue page and — when granted — on events held there.</summary>
        public string? History { get; set; }

        /// <summary>What a group holding an event here has to know: curfews, candles, parking.</summary>
        public string? HouseRules { get; set; }

        /// <summary>How many may stay overnight, when that is a limit the venue wants stated.</summary>
        public int? MaxOvernightGuests { get; set; }

        /// <summary>Whether the venue page is visible to anybody who is not in the group.</summary>
        public bool IsPublished { get; set; }

        /// <summary>
        /// When this group was accepted as the venue at this place. Null until a claim is proved.
        /// </summary>
        public DateTime? VerifiedUtc { get; set; }

        public DateTime DateCreated { get; set; }
        public DateTime? DateUpdated { get; set; }
        public Guid CreatedByAppUserId { get; set; }
        public Guid? UpdatedByAppUserId { get; set; }

        public virtual Organization Organization { get; set; } = null!;
        public virtual Place Place { get; set; } = null!;
        public virtual AppUser CreatedByAppUser { get; set; } = null!;
        public virtual AppUser? UpdatedByAppUser { get; set; }
    }

    /// <summary>
    /// One group asking a verified venue whether it may hold one event there (item 235 phase 9).
    /// </summary>
    /// <remarks>
    /// <para><b>Always about an event.</b> The organizer asks from the event's own page, so the
    /// venue sees exactly what is proposed — the name, the nights, how many are expected — and not
    /// an abstract "may we use your building in the autumn". A yes to something unspecified is the
    /// yes that gets disputed.</para>
    ///
    /// <para>The dates are copied at the moment of asking. If the organizer adds a night afterwards
    /// the grant does not stretch to cover it, and the readiness list says so.</para>
    /// </remarks>
    public class VenueHostingRequest : IAuditableEntity
    {
        public Guid Id { get; set; }
        public Guid HostedEventId { get; set; }

        /// <summary>The group asking — the event's own.</summary>
        public Guid RequestingOrganizationId { get; set; }

        /// <summary>The group being asked — the verified venue at the event's place.</summary>
        public Guid VenueOrganizationId { get; set; }

        public DateTime FromDate { get; set; }
        public DateTime ToDate { get; set; }

        /// <summary>What the organizer wanted the venue to know.</summary>
        public string? Message { get; set; }

        public VenueHostingRequestStatus Status { get; set; } = VenueHostingRequestStatus.Pending;
        public DateTime? DecidedUtc { get; set; }
        public Guid? DecidedByAppUserId { get; set; }

        /// <summary>The venue's reason for a no, which the organizer is shown word for word.</summary>
        public string? DecisionNote { get; set; }

        /// <summary>The grant a yes created.</summary>
        public Guid? OrganizationVenueGrantId { get; set; }

        public DateTime DateCreated { get; set; }
        public DateTime? DateUpdated { get; set; }
        public Guid CreatedByAppUserId { get; set; }
        public Guid? UpdatedByAppUserId { get; set; }

        public virtual HostedEvent HostedEvent { get; set; } = null!;
        public virtual Organization RequestingOrganization { get; set; } = null!;
        public virtual Organization VenueOrganization { get; set; } = null!;
        public virtual OrganizationVenueGrant? OrganizationVenueGrant { get; set; }
        public virtual AppUser? DecidedByAppUser { get; set; }
        public virtual AppUser CreatedByAppUser { get; set; } = null!;
        public virtual AppUser? UpdatedByAppUser { get; set; }
    }

    /// <summary>
    /// A venue's yes: one group may hold events at this place between these dates (item 235 phase 9).
    /// </summary>
    /// <remarks>
    /// <para><b>The site's first grant from one group to another.</b> Everything else a group can
    /// see belongs to it or was shared with a person; this is one group lending another the right to
    /// publish at its address, and some of what goes with the address.</para>
    ///
    /// <para><b>Withdrawn, never deleted.</b> A revoked grant is the record of why an event that
    /// guests had booked stopped happening, and deleting it would leave a cancelled weekend with no
    /// account of who called it off. <see cref="RevokedReason"/> is shown to the organizer and to
    /// every guest who is told.</para>
    /// </remarks>
    public class OrganizationVenueGrant : IAuditableEntity
    {
        public Guid Id { get; set; }
        public Guid VenueOrganizationId { get; set; }
        public Guid GranteeOrganizationId { get; set; }
        public Guid PlaceId { get; set; }

        /// <summary>The first date covered, inclusive, as a calendar date at the venue.</summary>
        public DateTime ValidFrom { get; set; }

        /// <summary>The last date covered, inclusive.</summary>
        public DateTime ValidTo { get; set; }

        /// <summary>The event's plan may place the rooms the venue has described.</summary>
        public bool AllowRooms { get; set; }

        /// <summary>The event's page may tell the building's story from the venue's profile.</summary>
        public bool AllowHistory { get; set; }

        /// <summary>
        /// The venue's own people may see who is coming and run the door, without deciding anything.
        /// </summary>
        public bool AllowStaff { get; set; }

        public DateTime? RevokedUtc { get; set; }
        public Guid? RevokedByAppUserId { get; set; }
        public string? RevokedReason { get; set; }

        public DateTime DateCreated { get; set; }
        public DateTime? DateUpdated { get; set; }
        public Guid CreatedByAppUserId { get; set; }
        public Guid? UpdatedByAppUserId { get; set; }

        public virtual Organization VenueOrganization { get; set; } = null!;
        public virtual Organization GranteeOrganization { get; set; } = null!;
        public virtual Place Place { get; set; } = null!;
        public virtual AppUser? RevokedByAppUser { get; set; }
        public virtual AppUser CreatedByAppUser { get; set; } = null!;
        public virtual AppUser? UpdatedByAppUser { get; set; }
    }
}
