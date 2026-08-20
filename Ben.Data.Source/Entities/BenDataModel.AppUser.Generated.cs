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
        public bool SharePrivatePhotoWithClients { get; set; }
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
