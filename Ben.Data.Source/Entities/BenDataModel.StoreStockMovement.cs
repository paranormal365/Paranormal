using Ben.Data.Common.Enums;

namespace Ben.Data.Source.Entities
{
    /// <summary>
    /// Why a variant's stock on hand changed, and what it came to (storefront).
    /// </summary>
    /// <remarks>
    /// One append-only ledger written by the stock helper and the admin adjust screens, so "why is it
    /// 3?" always has an answer.
    /// </remarks>
    public class StoreStockMovement
    {
        public Guid Id { get; set; }
        public Guid VariantId { get; set; }
        public int Delta { get; set; }
        public int QuantityAfter { get; set; }
        public StoreStockReason Reason { get; set; }
        public string? Note { get; set; }
        public Guid? OrderId { get; set; }
        public Guid? RefundId { get; set; }
        public Guid? ActorAppUserId { get; set; }
        public DateTime OccurredUtc { get; set; }

        public virtual StoreProductVariant Variant { get; set; } = null!;
        public virtual StoreOrder? Order { get; set; }
        public virtual StoreRefund? Refund { get; set; }
        public virtual AppUser? ActorAppUser { get; set; }
    }
}
