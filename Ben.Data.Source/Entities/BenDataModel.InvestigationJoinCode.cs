using System;
using Ben.Data.Common.Interfaces;

namespace Ben.Data.Source.Entities
{
    /// <summary>
    /// The code staff hold up so a guest's phone can join tonight's work (item 248).
    /// </summary>
    /// <remarks>
    /// <para>Ben, 2026-09-20: <i>"generate a qr code for the employees to allow someone to scan
    /// with their phone which would let the person scanning it to have credentials to use their
    /// phone for investigation as well… something we let them generate and print or generate and
    /// let others scan off their tablet, computer or phone."</i></para>
    ///
    /// <para><b>Not a ticket and not a letter.</b> Everything else the site has for bringing
    /// somebody in assumes it knows them first — an invitation to an address, a sign-up, a
    /// membership. The people this is for are standing in front of a guide in the dark, and
    /// anything needing a typed address, a confirmation letter and a password is not going to
    /// happen at the gate of a cemetery at 9pm.</para>
    ///
    /// <para><b>One code, many people, a credential each.</b> The sheet is held up to a crowd, so
    /// the code itself is shared by design — what is not shared is what a scan produces. Each
    /// redemption mints its own <see cref="InvestigationGuestPass"/>, which is what lets a guide
    /// revoke one person without reprinting the sheet, and what makes "who is holding one tonight"
    /// a question with an answer.</para>
    /// </remarks>
    public partial class InvestigationJoinCode : IAuditableEntity
    {
        public Guid Id { get; set; }

        /// <summary>The night this admits somebody to. Nothing else.</summary>
        public Guid InvestigationId { get; set; }
        public Investigation? Investigation { get; set; }

        /// <summary>Whose investigation it is, so a guide's permission can be checked without a join.</summary>
        public Guid OrganizationId { get; set; }

        /// <summary>The secret behind the QR, long enough that guessing is not a way in.</summary>
        public string Token { get; set; } = null!;

        /// <summary>
        /// The short code printed beside the QR, for somebody to type.
        /// </summary>
        /// <remarks>
        /// <b>Because Apple has no deferred deep linking.</b> Somebody who scans the QR without the
        /// app gets Safari and an App Store link; after installing, the app has no idea what they
        /// scanned. Nothing clever fixes that without a third party, so the sheet carries a code a
        /// person can read out and type — which is the only thing that actually works for a fresh
        /// install, and costs a few characters of print.
        /// <para>Short and unambiguous: no letters that look like digits, because it is read off a
        /// printed sheet in the dark.</para>
        /// </remarks>
        public string TypedCode { get; set; } = null!;

        /// <summary>
        /// When it stops working.
        /// </summary>
        /// <remarks>
        /// A code scanned at 7pm must not still open anything in March. The night's own window is
        /// the bound, and a code with no end is not offered.
        /// </remarks>
        public DateTime ExpiresUtc { get; set; }

        /// <summary>When a guide took it out of use, and who.</summary>
        public DateTime? RevokedUtc { get; set; }
        public Guid? RevokedByAppUserId { get; set; }

        public DateTime DateCreated { get; set; }
        public DateTime? DateUpdated { get; set; }
        public Guid CreatedByAppUserId { get; set; }
        public Guid? UpdatedByAppUserId { get; set; }
    }

    /// <summary>
    /// One person's credential for tonight, minted when they scanned the code (item 248).
    /// </summary>
    /// <remarks>
    /// <para><b>What it permits, and nothing more</b> (Ben, 2026-09-21): contribute photos, audio
    /// and readings to THIS investigation, and see their own. Not the case, not the client, not the
    /// address, not other people's evidence, and nothing that outlives the night. A walk-up guest
    /// is not somebody the group vetted, and a public investigation can still sit at a named
    /// place.</para>
    ///
    /// <para>Separate from the code so that revoking one person does not mean reprinting the
    /// sheet, and so that a guide can see who is working tonight.</para>
    /// </remarks>
    public partial class InvestigationGuestPass : IAuditableEntity
    {
        public Guid Id { get; set; }

        public Guid InvestigationJoinCodeId { get; set; }
        public InvestigationJoinCode? InvestigationJoinCode { get; set; }

        /// <summary>The investigation, copied from the code so a check needs no join.</summary>
        public Guid InvestigationId { get; set; }

        /// <summary>Who holds it. An account minted at redemption when they had none.</summary>
        public Guid AppUserId { get; set; }
        public AppUser? AppUser { get; set; }

        /// <summary>What they called themselves, so a guide sees a name rather than an id.</summary>
        public string? DisplayName { get; set; }

        public DateTime IssuedUtc { get; set; }

        /// <summary>When a guide took this one person's credential away, and who did it.</summary>
        public DateTime? RevokedUtc { get; set; }
        public Guid? RevokedByAppUserId { get; set; }

        public DateTime DateCreated { get; set; }
        public DateTime? DateUpdated { get; set; }
        public Guid CreatedByAppUserId { get; set; }
        public Guid? UpdatedByAppUserId { get; set; }
    }
}
