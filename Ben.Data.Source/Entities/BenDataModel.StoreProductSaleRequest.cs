using Ben.Data.Common.Enums;

namespace Ben.Data.Source.Entities
{
    /// <summary>
    /// A seller asking the store to put one of their items on sale, with what they want to be paid
    /// for each unit (store sellers, backlog 251, P3).
    /// </summary>
    /// <remarks>
    /// <para>Ben, 09/24/2026: an admin approves an item going on sale and sets its price; the seller
    /// proposes their asking price in the request. The store's selling price is its own — the
    /// asking price is what the seller earns on top of cost, fixed onto the item when approved.</para>
    ///
    /// <para>Kept after it is decided, so the item's page can say why a request was declined and
    /// the queue has a record. One open request per item (a filtered unique index).</para>
    /// </remarks>
    public class StoreProductSaleRequest
    {
        public const int MaxNoteLength = 1000;

        public Guid Id { get; set; }
        public Guid ProductId { get; set; }
        public Guid SellerAppUserId { get; set; }
        public decimal SellerAskingPrice { get; set; }
        public string? SellerNote { get; set; }
        public StoreSaleRequestStatus Status { get; set; }
        public DateTime RequestedUtc { get; set; }
        public Guid? DecidedByAppUserId { get; set; }
        public DateTime? DecidedUtc { get; set; }
        public string? DecisionNote { get; set; }

        public virtual StoreProduct Product { get; set; } = null!;
        public virtual AppUser SellerAppUser { get; set; } = null!;
        public virtual AppUser? DecidedByAppUser { get; set; }
    }
}
