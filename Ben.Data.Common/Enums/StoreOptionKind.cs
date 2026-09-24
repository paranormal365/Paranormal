namespace Ben.Data.Common.Enums;

/// <summary>How a product option's values are chosen on the product page (storefront).</summary>
public enum StoreOptionKind
{
    /// <summary>Text pills — "Standard", "Large".</summary>
    Pill = 0,

    /// <summary>Round colour swatches, from each value's hex colour.</summary>
    Swatch = 1,
}
