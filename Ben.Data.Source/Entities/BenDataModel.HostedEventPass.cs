using Ben.Data.Common.Interfaces;

namespace Ben.Data.Source.Entities
{
    /// <summary>
    /// A confirmed booking's ticket: something the door can scan to know who has just walked in.
    /// </summary>
    /// <remarks>
    /// <para>Ben, 2026-09-12: <i>"Generate a QR code for the confirmation the event organizer can
    /// scan to check them in when they arrive so check in is smoother."</i></para>
    ///
    /// <para><b>A pass belongs to a booking, not to a person.</b> A booking is a party, so one
    /// pass admits the party — the alternative is four people at a door each hunting for their own
    /// code while three of them never had an email address here. The scan answers with the party
    /// size, so the door counts heads against a number rather than against a stack of tickets.</para>
    ///
    /// <para><b>The token is the whole of the secret, and it is opaque.</b> It carries no booking
    /// id, no name and no event: anybody who photographs a printed pass over somebody's shoulder
    /// learns a random string, and a random string is worth nothing away from this event's door.
    /// It is looked up rather than decoded, so a forged one cannot be constructed.</para>
    ///
    /// <para><b>Nothing is ever edited into a pass.</b> A pass is issued, and afterwards it is only
    /// ever revoked — a changed booking gets a NEW pass and the old one is revoked, with
    /// <see cref="ReissuedFromHostedEventPassId"/> saying which it replaced. That is what makes a
    /// revoked pass a fact rather than a gap: a door shown an old code is told it was replaced,
    /// which is a different thing from being told it was never real.</para>
    /// </remarks>
    public partial class HostedEventPass : IAuditableEntity
    {
        public Guid Id { get; set; }
        public Guid HostedEventBookingId { get; set; }

        /// <summary>
        /// The opaque string the QR code carries. Unique across the site.
        /// </summary>
        /// <remarks>
        /// 256 bits of randomness, hex, because guessing one must not be a way through a door.
        /// Unique site-wide rather than per event so that a scan can be answered without the door
        /// first having to say which event it is standing at.
        /// </remarks>
        public string Token { get; set; } = null!;

        public DateTime IssuedUtc { get; set; }

        /// <summary>When it stopped being good, and why. Null while it is live.</summary>
        public DateTime? RevokedUtc { get; set; }

        /// <summary>
        /// In the venue's own words, and shown at the door.
        /// </summary>
        /// <remarks>
        /// Required when revoking. A door told only "not valid" cannot tell a guest whether to go
        /// and find the desk, wait, or go home — and "the booking changed, ask them for the new
        /// one" is a sentence that ends the conversation.
        /// </remarks>
        public string? RevokedReason { get; set; }

        /// <summary>The pass this one replaced, when it was a reissue.</summary>
        public Guid? ReissuedFromHostedEventPassId { get; set; }

        /// <summary>When it was last emailed to the guest. Null when it never has been.</summary>
        public DateTime? EmailedUtc { get; set; }

        /// <summary>
        /// When somebody at the door scanned it and let the party in.
        /// </summary>
        /// <remarks>
        /// <para>Kept on the pass rather than on the booking, so a reissued pass does not inherit
        /// the arrival of the one it replaced — a party that came on Friday and was reissued for
        /// Saturday genuinely has not arrived yet on the new pass.</para>
        ///
        /// <para><b>A second scan is not refused.</b> A door that rejected the same party walking
        /// back in from the car park would be a worse door than one that says "already checked in
        /// at 7:42" and lets the person on the door decide. The first scan is the one kept.</para>
        /// </remarks>
        public DateTime? CheckedInUtc { get; set; }

        /// <summary>Who was on the door.</summary>
        public Guid? CheckedInByAppUserId { get; set; }

        public DateTime DateCreated { get; set; }
        public DateTime? DateUpdated { get; set; }
        public Guid CreatedByAppUserId { get; set; }
        public Guid? UpdatedByAppUserId { get; set; }

        public virtual HostedEventBooking HostedEventBooking { get; set; } = null!;
        public virtual AppUser CreatedByAppUser { get; set; } = null!;
        public virtual AppUser? UpdatedByAppUser { get; set; }
        public virtual AppUser? CheckedInByAppUser { get; set; }
    }
}
