namespace Ben.Data.Source.Entities
{
    /// <summary>
    /// Per-viewer audit record — tracks every unique user who viewed the message.
    /// Composite primary key on (OrgMessageId, ViewerAppUserId).
    /// </summary>
    public class OrgMessageView
    {
        public Guid OrgMessageId { get; set; }
        public Guid ViewerAppUserId { get; set; }
        public DateTime DateViewed { get; set; }

        public virtual OrgMessage OrgMessage { get; set; } = null!;
        public virtual AppUser ViewerAppUser { get; set; } = null!;
    }
}
