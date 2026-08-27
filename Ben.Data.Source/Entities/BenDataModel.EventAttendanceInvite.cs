using Ben.Data.Common.Interfaces;

namespace Ben.Data.Source.Entities
{
    /// <summary>
    /// Somebody said they are coming to a public event before they had an account here.
    /// </summary>
    /// <remarks>
    /// <para>Ben's requirement: <i>"Someone may give some information, but we need enough to be able
    /// to show them they have elected to attend if not already users of our site."</i></para>
    ///
    /// <para><b>An email typed into a box proves nothing.</b> If anyone could type an address and be
    /// shown where a group is meeting, a hidden location would be theatre. So the address is typed,
    /// a link is sent to it, and only clicking that link confirms anything — the cheapest gate that
    /// actually verifies the person has the address they claimed.</para>
    ///
    /// <para><b>Confirming creates a real, passwordless account.</b> Not a guest record: Ben's stated
    /// purpose for public events is that they introduce a group to new people, and a guest RSVP
    /// leaves nobody behind. This reconciles <i>"they have to be a site user"</i> with <i>"maybe we
    /// allow the temporary with contact info"</i> — they are a user, they simply never had to invent
    /// a password. Setting one later is an upgrade rather than a requirement.</para>
    ///
    /// <para>Modelled on <see cref="CaseClientInvite"/>, which is the same shape pointed at a case:
    /// an address, a single-use token, an expiry, and who it turned into.</para>
    /// </remarks>
    public class EventAttendanceInvite : IAuditableEntity
    {
        public Guid Id { get; set; }
        public Guid OrgCalendarEventId { get; set; }

        /// <summary>Where the confirmation link was sent. Lowercased on save so lookups match.</summary>
        public string Email { get; set; } = string.Empty;

        /// <summary>What they would like to be called. Optional — an email is enough to come along.</summary>
        public string? DisplayName { get; set; }

        /// <summary>Single-use. Cleared on confirmation so a forwarded email cannot be replayed.</summary>
        public string? Token { get; set; }

        public DateTime DateExpires { get; set; }

        /// <summary>When they clicked the link. Null while it is still just a typed address.</summary>
        public DateTime? DateConfirmed { get; set; }

        /// <summary>The account confirming created, or the existing one the address belonged to.</summary>
        public Guid? ConfirmedByAppUserId { get; set; }

        /// <summary>
        /// The organiser who sent this link on somebody's behalf, or null when the guest asked
        /// for it themselves.
        /// </summary>
        /// <remarks>
        /// <para><b>The walk-up problem (Ben, 2026-08-27).</b> A guide standing at the meeting
        /// point with a group who turned up late cannot use the self-service flow on their
        /// behalf, and cannot add them as attendees either, because that path resolves an
        /// existing account by published email and a walk-up has no account at all. So a guide
        /// with the calendar permission sends the link instead, and the guest confirms it on
        /// their own phone — the same machinery, started by somebody accountable.</para>
        ///
        /// <para><b>Why it is recorded rather than inferred.</b> Confirmation re-checks the
        /// sign-up closing time, so a link sent for a late arrival would otherwise be refused at
        /// the moment it is used — the exact failure the late-arrival grace exists to prevent.
        /// A link the organiser sent carries their decision with it and confirms after closing;
        /// one the guest asked for themselves does not. Keeping the organiser's id also means
        /// "who let these thirty people in" has an answer.</para>
        /// </remarks>
        public Guid? InvitedByAppUserId { get; set; }

        public DateTime DateCreated { get; set; }
        public DateTime? DateUpdated { get; set; }
        public Guid CreatedByAppUserId { get; set; }
        public Guid? UpdatedByAppUserId { get; set; }

        public virtual OrgCalendarEvent OrgCalendarEvent { get; set; } = null!;
        public virtual AppUser? ConfirmedByAppUser { get; set; }
        public virtual AppUser? InvitedByAppUser { get; set; }
    }
}
