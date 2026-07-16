using System;

namespace Ben.Data.Source.Entities
{
    public partial class UserMessageTo
    {
        public Guid MessageId { get; set; }
        public Guid ToAppUserId { get; set; }
        public DateTime? DateLastRead { get; set; }
        public int LastReadCount { get; set; }

        public virtual UserMessage UserMessage { get; set; } = null!;
        public virtual AppUser ToAppUser { get; set; } = null!;
    }
}
