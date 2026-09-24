using Ben.Data.Common.Enums;

namespace Ben.Data.Source.Entities
{
    /// <summary>One line of an order's timeline (storefront). Append-only.</summary>
    public class StoreOrderEvent
    {
        public Guid Id { get; set; }
        public Guid OrderId { get; set; }
        public StoreOrderEventKind Kind { get; set; }
        public StoreOrderStatus? FromStatus { get; set; }
        public StoreOrderStatus? ToStatus { get; set; }

        /// <summary>The admin who did it; null for the buyer, Stripe or a job.</summary>
        public Guid? ActorAppUserId { get; set; }

        public string? Note { get; set; }
        public decimal? Amount { get; set; }
        public DateTime OccurredUtc { get; set; }

        public virtual StoreOrder Order { get; set; } = null!;
        public virtual AppUser? ActorAppUser { get; set; }
    }
}
