namespace Ben.Data.Source.Entities
{
    /// <summary>One message in a support ticket's thread, from either side.</summary>
    public partial class SupportTicketReply
    {
        public Guid SupportTicketId { get; set; }

        public string Body { get; set; } = null!;

        /// <summary>Author's account. Null when an anonymous sender replied through their link.</summary>
        public Guid? AuthorAppUserId { get; set; }

        /// <summary>
        /// True when written by staff. Stored rather than derived from
        /// <see cref="AuthorAppUserId"/> being set, because a signed-in visitor also has one.
        /// </summary>
        public bool IsFromStaff { get; set; }

        /// <summary>
        /// Staff-only note. Never returned by the sender-facing endpoints — the guard for that
        /// lives in the query, not in the UI.
        /// </summary>
        public bool IsInternalNote { get; set; }

        public DateTime DateCreated { get; set; }

        public virtual SupportTicket SupportTicket { get; set; } = null!;
        public virtual AppUser? AuthorAppUser { get; set; }
    }
}
