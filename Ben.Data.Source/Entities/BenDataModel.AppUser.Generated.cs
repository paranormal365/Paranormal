using System;
using System.Collections.Generic;

namespace Ben.Data.Source.Entities
{
    public partial class AppUser
    {
        // UserName, Email, PasswordHash, etc. are provided by IdentityUser<Guid>.
        public string? DisplayName { get; set; }

        /// <summary>
        /// This account's <c>@name</c> — unique across the site, lower-cased, chosen when the
        /// account is created.
        /// </summary>
        /// <remarks>
        /// <para>What makes an <c>@mention</c> in the public feed resolve to exactly one person.
        /// Display names are neither unique nor free of spaces, so matching against them meant
        /// notifying the wrong person or nobody — and the answer changing as accounts were added.
        /// </para>
        ///
        /// <para><b>Not editable</b>, by Ben's decision on 2026-08-20: chosen once at creation.
        /// Letting it change later is a possible future and deliberately low priority, and the
        /// reason it is not free is that the handle appears in other people's posts.</para>
        ///
        /// <para>Nullable in the column only so that existing rows could be migrated before being
        /// backfilled; every account has one, and nothing should be written without one. The rules
        /// live in <c>UserHandle</c>.</para>
        /// </remarks>
        public string? Handle { get; set; }

        /// <summary>
        /// This user's half of the two-key rule for showing their private photo to clients of the
        /// orgs they work for. Meaningless on its own — the org must also allow it
        /// (<see cref="Organization.AllowMemberPrivatePhotosToClients"/>). Defaults to false:
        /// consent is something you give, not something you forget to withdraw.
        /// </summary>
        /// <summary>The person's legal first name. Required of every account.</summary>
        /// <remarks>
        /// <para><b>Nullable in the database, required in the UI.</b> The column has to accept null
        /// because 87 accounts already existed when it was added and inventing names for them would
        /// be worse than leaving them blank. Sign-up and the profile both require it going forward,
        /// so the nulls are a finite backlog rather than an ongoing hole.</para>
        ///
        /// <para>Distinct from <see cref="DisplayName"/>, which is what other members see. A person
        /// may go by a nickname on the site and still be Margaret on the paperwork; conflating the
        /// two is how a case report ends up addressed to "Ghosty42".</para>
        /// </remarks>
        public string? FirstName { get; set; }

        /// <summary>The person's legal last name. Required of every account.</summary>
        public string? LastName { get; set; }

        /// <summary>
        /// Optional, and genuinely optional — never required at sign-up or on the profile.
        /// </summary>
        /// <remarks>
        /// Reuses <c>ClientGender</c> rather than adding a second enum meaning the same thing;
        /// <c>NotProvided</c> already exists as its zero value, so "declined to say" is
        /// representable without a null. Null here means the same as NotProvided and both are fine.
        /// </remarks>
        public Ben.Data.Common.Enums.ClientGender? Gender { get; set; }

        /// <summary>
        /// When this person finished (or skipped) first-run onboarding (item 166 W2). Null means
        /// the onboarding wizard has never been offered-and-answered; the gate shows it once.
        /// Existing accounts were stamped by the migration that added the column — they are
        /// already onboard, and a wizard nagging a two-year member would be worse than none.
        /// </summary>
        public DateTime? DateOnboarded { get; set; }

        /// <summary>
        /// Year of birth. Optional, and never the thing an age gate depends on.
        /// </summary>
        /// <remarks>
        /// <para><b>Year, not a full date</b>, matching <c>ClientRequest.BirthYear</c>. A complete
        /// date of birth is an identity-theft and account-recovery credential; a year gives every
        /// age band the site would actually act on and is worth far less to anyone who steals it.
        /// Ben weighed a full date for birthday greetings and chose the year instead.</para>
        ///
        /// <para><b>Optional, so it cannot be the age gate.</b> Anything that must hold for every
        /// account has to come from something every account has — a 13+ attestation at sign-up —
        /// because the people most likely to leave this blank are exactly the ones a gate would be
        /// for.</para>
        ///
        /// <para>Nothing collects it yet.</para>
        /// </remarks>
        public int? BirthYear { get; set; }

        public bool SharePrivatePhotoWithClients { get; set; }

        /// <summary>
        /// When this person closed their account, or null while it is open.
        /// </summary>
        /// <remarks>
        /// <para><b>The row survives the person.</b> Closing an account anonymises it rather than
        /// deleting it: identity, credentials and contact details go, while the cases, evidence,
        /// reports and messages they authored stay where their group left them, attributed to
        /// <c>AccountClosure.FormerMemberName</c>. Deleting the row would take a group's case
        /// history with it — see <c>AccountClosureService</c> for the whole argument, and for why
        /// an organization's owner has to hand it over before they can leave.</para>
        ///
        /// <para>This is the flag everything else keys on: a closed account cannot sign in, cannot
        /// be invited, and does not appear in people-pickers. Nullable because "not closed" is the
        /// normal state and a sentinel date would be a lie about when it happened.</para>
        /// </remarks>
        public DateTime? DateClosed { get; set; }

        public DateTime DateCreated { get; set; }
        public DateTime? DateUpdated { get; set; }

        public virtual ICollection<AppUserPhoto> Photos { get; set; } = new List<AppUserPhoto>();
        public virtual ICollection<UserAddress> UserAddresses { get; set; } = new List<UserAddress>();
        public virtual ICollection<UserEmail> UserEmails { get; set; } = new List<UserEmail>();
        public virtual ICollection<UserPhone> UserPhones { get; set; } = new List<UserPhone>();
        public virtual ICollection<UserLink> UserLinks { get; set; } = new List<UserLink>();
        public virtual ICollection<UserMessage> CreatedMessages { get; set; } = new List<UserMessage>();
        public virtual ICollection<UserNote> CreatedUserNotes { get; set; } = new List<UserNote>();
        public virtual ICollection<UserMessageTo> ReceivedUserMessageTos { get; set; } = new List<UserMessageTo>();
    }
}
