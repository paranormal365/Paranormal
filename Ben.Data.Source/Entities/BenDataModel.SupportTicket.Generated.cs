using Ben.Data.Common.Enums;

namespace Ben.Data.Source.Entities
{
    /// <summary>
    /// A message from a visitor asking for help or trying to reach staff.
    /// </summary>
    /// <remarks>
    /// <para>Deliberately not an <c>IAuditableEntity</c>: most tickets arrive from people with no
    /// account, so there is no <c>CreatedByAppUserId</c> to record. <see cref="AppUserId"/> is set
    /// only when the sender happened to be signed in.</para>
    ///
    /// <para>The ticket — not an email — is the record. Mail may be sent as a notification on top,
    /// and is allowed to fail; the conversation lives here either way, which is what lets the whole
    /// feature work before SMTP is configured.</para>
    /// </remarks>
    public partial class SupportTicket
    {
        /// <summary>Human-readable handle, e.g. <c>SUP-9F3A1C0B</c>. Shown to the sender and staff.</summary>
        public string Reference { get; set; } = null!;

        /// <summary>
        /// Secret that lets an anonymous sender return to their own ticket.
        /// </summary>
        /// <remarks>
        /// This is the answer to "how does staff reply reach someone with no account?" — they get a
        /// link containing this value. It is the only credential on the thread, so it is generated
        /// randomly and never displayed anywhere except to the sender who created it.
        /// </remarks>
        public Guid AccessToken { get; set; }

        public string FromName { get; set; } = null!;
        public string FromEmail { get; set; } = null!;

        public SupportTicketTopic Topic { get; set; }
        public string Subject { get; set; } = null!;
        public string Body { get; set; } = null!;

        public SupportTicketStatus Status { get; set; }

        /// <summary>The sender's account, when they were signed in. Null for anonymous senders.</summary>
        public Guid? AppUserId { get; set; }

        /// <summary>Staff member who picked this up.</summary>
        public Guid? AssignedToAppUserId { get; set; }

        /// <summary>
        /// Salted SHA-256 of the sender's IP, for rate limiting only.
        /// </summary>
        /// <remarks>
        /// Hashed rather than stored raw: rate limiting only ever needs to know whether two
        /// submissions came from the same place, which equality on the hash answers just as well.
        /// Keeping the address itself would be collecting personal data the feature never reads.
        /// </remarks>
        public string? SourceIpHash { get; set; }

        public DateTime DateCreated { get; set; }
        public DateTime? DateUpdated { get; set; }
        public DateTime? DateClosed { get; set; }

        public virtual AppUser? AppUser { get; set; }
        public virtual AppUser? AssignedToAppUser { get; set; }
        public virtual ICollection<SupportTicketReply> Replies { get; set; } = new List<SupportTicketReply>();
    }
}
