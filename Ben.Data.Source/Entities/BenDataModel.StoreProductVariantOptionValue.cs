
namespace Ben.Data.Source.Entities
{
    /// <summary>Which option value a variant carries (storefront). One row per option.</summary>
    public class StoreProductVariantOptionValue
    {
        public Guid Id { get; set; }
        public Guid VariantId { get; set; }
        public Guid OptionValueId { get; set; }
        public DateTime DateCreated { get; set; }

        public virtual StoreProductVariant Variant { get; set; } = null!;
        public virtual StoreProductOptionValue OptionValue { get; set; } = null!;
    }
}
