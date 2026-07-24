namespace Ben.Data.Source.Entities
{
    /// <summary>Tracks which users were sent a message and whether they have read it.</summary>
    public class OrgMessageRecipient
    {
        public Guid Id { get; set; }
        public Guid OrgMessageId { get; set; }
        public Guid RecipientAppUserId { get; set; }

        /// <summary>When the recipient opened/read the message. Null = unread.</summary>
        public DateTime? DateRead { get; set; }

        public DateTime DateCreated { get; set; }

        public virtual OrgMessage OrgMessage { get; set; } = null!;
        public virtual AppUser RecipientAppUser { get; set; } = null!;
    }
}
