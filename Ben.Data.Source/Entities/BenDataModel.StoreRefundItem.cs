
namespace Ben.Data.Source.Entities
{
    /// <summary>Which order lines a refund covers, and how many (storefront).</summary>
    public class StoreRefundItem
    {
        public Guid Id { get; set; }
        public Guid RefundId { get; set; }
        public Guid OrderItemId { get; set; }
        public int Quantity { get; set; }
        public decimal Amount { get; set; }

        public virtual StoreRefund Refund { get; set; } = null!;
        public virtual StoreOrderItem OrderItem { get; set; } = null!;
    }
}
