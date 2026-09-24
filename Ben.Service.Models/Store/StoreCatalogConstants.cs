namespace Ben.Service.Models.Store;

/// <summary>
/// One price filter on the listing page — "Under $25", "$25 – $50" (storefront).
/// </summary>
/// <param name="Key">What the query string carries (<c>?price=25-50</c>).</param>
/// <param name="Min">Inclusive lower bound, or null for no floor.</param>
/// <param name="Max">Exclusive upper bound, or null for no ceiling.</param>
public sealed record StorePriceBand(string Key, string Label, decimal? Min, decimal? Max)
{
    public bool Contains(decimal price) => (Min is null || price >= Min) && (Max is null || price < Max);
}

/// <summary>
/// The fixed vocabulary of the store's listing, sorting and limits, shared by the API that
/// enforces them and the pages that offer them (storefront).
/// </summary>
/// <remarks>
/// One definition, so a sort the page offers is always a sort the API understands and a limit the
/// page shows ("up to 100") is the limit the API refuses at.
/// </remarks>
public static class StoreCatalogConstants
{
    /// <summary>Products per listing page — Smarty's four-by-four grid.</summary>
    public const int StorePageSize = 16;

    public static readonly IReadOnlyList<StorePriceBand> PriceBands =
    [
        new("under-25", "Under $25", null, 25m),
        new("25-50", "$25 – $50", 25m, 50m),
        new("50-100", "$50 – $100", 50m, 100m),
        new("100-250", "$100 – $250", 100m, 250m),
        new("250-500", "$250 – $500", 250m, 500m),
        new("500-plus", "$500 and up", 500m, null),
    ];

    public static StorePriceBand? BandFor(string? key)
        => PriceBands.FirstOrDefault(b => string.Equals(b.Key, key, StringComparison.OrdinalIgnoreCase));

    /// <summary>Listing sorts. The first is the default.</summary>
    public static class Sorts
    {
        public const string Popular = "popular";
        public const string Newest = "newest";
        public const string Rating = "rating";
        public const string PriceAscending = "price-asc";
        public const string PriceDescending = "price-desc";

        public static readonly IReadOnlyList<(string Key, string Label)> All =
        [
            (Popular, "Most popular"), (Newest, "Newest"), (Rating, "Top rated"),
            (PriceAscending, "Price: low to high"), (PriceDescending, "Price: high to low"),
        ];

        /// <summary>The sort to use for a requested key; anything unknown is the default.</summary>
        public static string Normalize(string? key)
            => All.Any(s => s.Key == key) ? key! : Popular;
    }

    /// <summary>Review sorts on a product page. The first is the default.</summary>
    public static class ReviewSorts
    {
        public const string Popular = "popular";
        public const string Newest = "newest";
        public const string Highest = "highest";
        public const string Lowest = "lowest";
        public const string Helpful = "helpful";

        public static readonly IReadOnlyList<(string Key, string Label)> All =
        [
            (Popular, "Most popular"), (Newest, "Newest"), (Highest, "Highest rating"),
            (Lowest, "Lowest rating"), (Helpful, "Most helpful"),
        ];

        public static string Normalize(string? key)
            => All.Any(s => s.Key == key) ? key! : Popular;
    }
}

/// <summary>The catalogue's limits (storefront).</summary>
public static class StoreCatalogRules
{
    public const int MaxImagesPerProduct = 12;
    public const int MaxOptionsPerProduct = 3;
    public const int MaxValuesPerOption = 24;
}

/// <summary>The cart's limits (storefront).</summary>
public static class StoreCartRules
{
    /// <summary>The most of one thing a cart line may hold — the CHECK on <c>StoreCartItems.Quantity</c>.</summary>
    public const int MaxQuantityPerLine = 100;

    public const int MaxDistinctLines = 50;

    /// <summary>Length of the guest cart token: 32 random bytes, base64url without padding.</summary>
    public const int TokenLength = 43;
}

/// <summary>The checkout's limits (storefront).</summary>
public static class StoreCheckoutRules
{
    /// <summary>Open (PendingPayment) checkouts one visitor address or email may hold at once.</summary>
    public const int MaxOpenCheckoutsPerCaller = 3;
}
