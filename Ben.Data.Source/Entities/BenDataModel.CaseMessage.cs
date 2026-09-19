using Ben.Data.Common.Enums;
using Ben.Data.Common.Interfaces;

namespace Ben.Data.Source.Entities
{
    /// <summary>A message in the client–org dialogue thread for a specific case.</summary>
    public class CaseMessage : IAuditableEntity
    {
        public Guid Id { get; set; }
        public Guid CaseId { get; set; }
        public Guid AuthorAppUserId { get; set; }
        /// <summary>The message as plain text. Always set; what the iPhone app draws.</summary>
        public string Body { get; set; } = null!;

        /// <summary>
        /// The message as sanitized HTML, when it was written in the website's formatting editor (2026-09-14).
        /// Null for everything written before that and for anything sent as plain text.
        /// </summary>
        public string? BodyHtml { get; set; }
        public CaseMessageSide SenderSide { get; set; }

        /// <summary>True once the client has opened/seen this org message.</summary>
        public bool IsReadByClient { get; set; }

        /// <summary>True once an org member has opened/seen this client message.</summary>
        public bool IsReadByOrg { get; set; }

        public DateTime DateCreated { get; set; }
        public DateTime? DateUpdated { get; set; }
        public Guid CreatedByAppUserId { get; set; }
        public Guid? UpdatedByAppUserId { get; set; }

        public virtual Case Case { get; set; } = null!;
        public virtual AppUser AuthorAppUser { get; set; } = null!;
        public virtual AppUser CreatedByAppUser { get; set; } = null!;
        public virtual AppUser? UpdatedByAppUser { get; set; }
    }
}
