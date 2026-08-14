using System;
using System.Collections.Generic;

namespace Ben.Data.Source.Entities
{
    public partial class AppUser
    {
        // UserName, Email, PasswordHash, etc. are provided by IdentityUser<Guid>.
        public string? DisplayName { get; set; }
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
