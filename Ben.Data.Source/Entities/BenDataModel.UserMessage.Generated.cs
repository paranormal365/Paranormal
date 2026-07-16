using System;
using System.Collections.Generic;

namespace Ben.Data.Source.Entities
{
    public partial class UserMessage
    {
        public Guid UserMessageTypeId { get; set; }
        public string? MessageSubject { get; set; }
        public string MessageBody { get; set; } = null!;
        public Guid? ParentMessageId { get; set; }
        public DateTime? DateArchived { get; set; }
        public DateTime DateCreated { get; set; }
        public DateTime? DateUpdated { get; set; }
        public Guid CreatedByAppUserId { get; set; }
        public Guid? UpdatedByAppUserId { get; set; }

        public virtual UserMessageType UserMessageType { get; set; } = null!;
        public virtual AppUser CreatedByAppUser { get; set; } = null!;
        public virtual AppUser? UpdatedByAppUser { get; set; }
        public virtual ICollection<UserMessageTo> UserMessageTos { get; set; } = new List<UserMessageTo>();
    }
}
