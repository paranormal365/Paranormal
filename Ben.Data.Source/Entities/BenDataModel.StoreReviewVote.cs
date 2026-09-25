
namespace Ben.Data.Source.Entities
{
    /// <summary>A "this was helpful" vote on a review (storefront). One per person per review.</summary>
    public class StoreReviewVote
    {
        public Guid Id { get; set; }
        public Guid ReviewId { get; set; }
        public Guid AppUserId { get; set; }
        public DateTime DateCreated { get; set; }

        public virtual StoreReview Review { get; set; } = null!;
        public virtual AppUser AppUser { get; set; } = null!;
    }
}
