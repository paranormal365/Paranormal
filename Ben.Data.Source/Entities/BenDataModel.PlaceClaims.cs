using Ben.Data.Common.Enums;
using Ben.Data.Common.Interfaces;

namespace Ben.Data.Source.Entities
{
    /// <summary>
    /// A way to reach a place: its website, a phone number, an email address (item 235 phase 9).
    /// </summary>
    /// <remarks>
    /// <para><b>Public or private.</b> Ben, 2026-09-13: "Phone public and private maybe for
    /// scheduling". A public detail is the venue's own front door — its website, its front-desk
    /// number — and anybody may see it on the place page. A private one is a group's own note — the
    /// events coordinator's mobile — and only that group sees it.</para>
    ///
    /// <para><b>Provisional until the venue is confirmed.</b> Before anybody has proved they run a
    /// place, any group may record its public details, and the page says who added each. Once a
    /// venue is confirmed, the public details are the venue's to keep: it confirms or removes what
    /// others added, and others add private notes only (<see cref="ConfirmedByVenueUtc"/>).</para>
    ///
    /// <para><b>Why this is also how a venue proves itself.</b> A public email address somebody
    /// OTHER than the claimant recorded, some days before the claim, is the address the world
    /// associates with the building. A code sent there reaches whoever really reads the venue's
    /// mail — which is the proof — while one the claimant typed in themselves proves nothing.</para>
    /// </remarks>
    public class PlaceContact : IAuditableEntity
    {
        public Guid Id { get; set; }
        public Guid PlaceId { get; set; }
        public PlaceContactKind Kind { get; set; }
        public string Value { get; set; } = null!;

        /// <summary>"Front desk", "Events coordinator", "Mrs Cole".</summary>
        public string? Label { get; set; }

        public bool IsPublic { get; set; }

        /// <summary>The group that recorded it. Null for the site's own staff.</summary>
        public Guid? OrganizationId { get; set; }

        /// <summary>When the confirmed venue said this public detail is right.</summary>
        public DateTime? ConfirmedByVenueUtc { get; set; }

        public DateTime DateCreated { get; set; }
        public DateTime? DateUpdated { get; set; }
        public Guid CreatedByAppUserId { get; set; }
        public Guid? UpdatedByAppUserId { get; set; }

        public virtual Place Place { get; set; } = null!;
        public virtual Organization? Organization { get; set; }
        public virtual AppUser CreatedByAppUser { get; set; } = null!;
        public virtual AppUser? UpdatedByAppUser { get; set; }
    }

    /// <summary>
    /// A group saying "we run this place", and the proof (item 235 phase 9).
    /// </summary>
    /// <remarks>
    /// <para><b>A claim is on the PLACE, never on an event.</b> It decides who must say yes to future
    /// events at the address and gives the venue a page. Nothing already booked moves, nothing is
    /// cancelled, and no past guest list changes hands: a claim that could seize an organizer's
    /// weekend would be a way to steal one.</para>
    ///
    /// <para><b>Two ways to prove it.</b> A code to a public email address that somebody else recorded
    /// for the place, then a week in which the groups who know the place may object; or, failing
    /// that, what the claimant has — a licence, a listing — reviewed by a person. An objection always
    /// sends it to a person. Never an automatic transfer on the claimant's word: the commonest false
    /// claim is a competitor, and the second commonest is a former manager.</para>
    /// </remarks>
    public class VenuePlaceClaim : IAuditableEntity
    {
        public Guid Id { get; set; }
        public Guid PlaceId { get; set; }

        /// <summary>The group claiming to be the venue.</summary>
        public Guid OrganizationId { get; set; }

        /// <summary>The person who made the claim for it.</summary>
        public Guid ClaimantAppUserId { get; set; }

        public VenueClaimantRole ClaimantRole { get; set; }

        /// <summary>What the claimant offers as proof, in their words — for the person reviewing it.</summary>
        public string? Evidence { get; set; }

        public VenueClaimState State { get; set; } = VenueClaimState.Pending;

        /// <summary>The public contact a code was sent to, when proving it that way.</summary>
        public Guid? PlaceContactId { get; set; }

        /// <summary>SHA-256 of the code. The code itself is never stored.</summary>
        public string? CodeHash { get; set; }
        public DateTime? CodeSentUtc { get; set; }
        public int CodeAttempts { get; set; }

        /// <summary>When the code was entered correctly.</summary>
        public DateTime? ProvedUtc { get; set; }

        /// <summary>When a proved claim takes effect, if nobody objects before then.</summary>
        public DateTime? ObjectionsCloseUtc { get; set; }

        public DateTime? ObjectedUtc { get; set; }
        public Guid? ObjectedByAppUserId { get; set; }
        public Guid? ObjectingOrganizationId { get; set; }
        public string? ObjectionText { get; set; }

        public DateTime? DecidedUtc { get; set; }
        public Guid? DecidedByAppUserId { get; set; }

        /// <summary>The reviewer's reason, shown to the claimant.</summary>
        public string? DecisionNote { get; set; }

        public DateTime DateCreated { get; set; }
        public DateTime? DateUpdated { get; set; }
        public Guid CreatedByAppUserId { get; set; }
        public Guid? UpdatedByAppUserId { get; set; }

        public virtual Place Place { get; set; } = null!;
        public virtual Organization Organization { get; set; } = null!;
        public virtual AppUser ClaimantAppUser { get; set; } = null!;
        public virtual PlaceContact? PlaceContact { get; set; }
        public virtual AppUser? ObjectedByAppUser { get; set; }
        public virtual Organization? ObjectingOrganization { get; set; }
        public virtual AppUser? DecidedByAppUser { get; set; }
        public virtual AppUser CreatedByAppUser { get; set; } = null!;
        public virtual AppUser? UpdatedByAppUser { get; set; }
    }
}
