using Ben.Data.Common.Enums;

namespace Ben.Data.Source.Entities
{
    /// <summary>
    /// One package of an order: everything one seller — or the site — ships (store sellers, backlog
    /// 251, P5).
    /// </summary>
    /// <remarks>
    /// <para>Ben, 09/24/2026: "Each seller contributes their own status of shipped … They may have a
    /// service and tracking number and the other will have another service and tracking number or no
    /// tracking number provided." Each package carries its own status, carrier and tracking; the
    /// order's status is rolled up from them.</para>
    ///
    /// <para><b>Money, frozen at checkout.</b> <see cref="ShippingAmount"/> is what the buyer paid to
    /// ship this package — the flat rate, or 0 when it shipped free — and <see cref="ShippingTaxAmount"/>
    /// its share of the order's tax on shipping. <see cref="SellerShippingCredit"/> is what the
    /// seller is credited for the label they buy: the flat rate even when the buyer's shipping was
    /// free (Ben), and 0 for the site's own stock.</para>
    ///
    /// <para><see cref="SellerName"/> is a snapshot, like the order's product names: the buyer's
    /// "Ships from" does not change when a seller renames themselves, and closing their account
    /// anonymises it.</para>
    /// </remarks>
    public class StoreOrderParcel
    {
        public Guid Id { get; set; }
        public Guid OrderId { get; set; }

        /// <summary>1, 2, … within the order: the site's own stock first, then sellers by id.</summary>
        public int Number { get; set; }

        /// <summary>The seller who ships it; null for the site's own stock.</summary>
        public Guid? SellerAppUserId { get; set; }
        public string? SellerName { get; set; }

        public StoreParcelStatus Status { get; set; }
        public decimal ShippingAmount { get; set; }
        public decimal ShippingTaxAmount { get; set; }
        public decimal ShippingRefunded { get; set; }
        public decimal SellerShippingCredit { get; set; }

        public string? Carrier { get; set; }
        public string? TrackingNumber { get; set; }
        public string? TrackingUrl { get; set; }
        public DateTime? PackedUtc { get; set; }
        public DateTime? ShippedUtc { get; set; }
        public DateTime? DeliveredUtc { get; set; }
        public DateTime? CancelledUtc { get; set; }

        public DateTime DateCreated { get; set; }
        public DateTime? DateUpdated { get; set; }

        public virtual StoreOrder Order { get; set; } = null!;
        public virtual AppUser? SellerAppUser { get; set; }
        public virtual ICollection<StoreOrderItem> Items { get; set; } = [];
    }
}
