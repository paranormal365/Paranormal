using Ben.Data.Common.Enums;
using Ben.Data.Common.Interfaces;

namespace Ben.Data.Source.Entities
{
    /// <summary>
    /// A buyer's review of a product, moderated before anyone else sees it (storefront).
    /// </summary>
    /// <remarks>
    /// Only somebody with a paid, not fully refunded order containing the product may write one;
    /// <see cref="OrderId"/> records which. One review per person per product; editing it sends it
    /// back to moderation. Kept, as "A former member", when the author's account is deleted.
    /// </remarks>
    public class StoreReview : IAuditableEntity
    {
        public Guid Id { get; set; }
        public Guid ProductId { get; set; }
        public Guid AuthorAppUserId { get; set; }
        public Guid OrderId { get; set; }
        public int Rating { get; set; }
        public string Title { get; set; } = string.Empty;
        public string Body { get; set; } = string.Empty;
        public StoreReviewStatus Status { get; set; }
        public string? RejectionReason { get; set; }
        public int HelpfulCount { get; set; }
        public Guid? ModeratedByAppUserId { get; set; }
        public DateTime? ModeratedUtc { get; set; }
        public string? AdminReply { get; set; }
        public Guid? AdminReplyByAppUserId { get; set; }
        public DateTime? AdminRepliedUtc { get; set; }

        public virtual StoreProduct Product { get; set; } = null!;
        public virtual AppUser AuthorAppUser { get; set; } = null!;
        public virtual StoreOrder Order { get; set; } = null!;
        public virtual AppUser? ModeratedByAppUser { get; set; }
        public virtual AppUser? AdminReplyByAppUser { get; set; }

        public DateTime DateCreated { get; set; }
        public DateTime? DateUpdated { get; set; }
        public Guid CreatedByAppUserId { get; set; }
        public Guid? UpdatedByAppUserId { get; set; }

        public virtual AppUser CreatedByAppUser { get; set; } = null!;
        public virtual AppUser? UpdatedByAppUser { get; set; }
    }
}
