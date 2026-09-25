
namespace Ben.Data.Source.Entities
{
    /// <summary>One line of a cart: a variant and how many (storefront). 1–100 per line.</summary>
    public class StoreCartItem
    {
        public Guid Id { get; set; }
        public Guid CartId { get; set; }
        public Guid VariantId { get; set; }
        public int Quantity { get; set; }
        public DateTime DateCreated { get; set; }
        public DateTime? DateUpdated { get; set; }

        public virtual StoreCart Cart { get; set; } = null!;
        public virtual StoreProductVariant Variant { get; set; } = null!;
    }
}
