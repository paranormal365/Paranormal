using Ben.Service.Models.Store;
using Ben.Web.Services.WebApi;

namespace Ben.Web.Services;

/// <summary>
/// The store as a shopper sees it (storefront S2). Anybody may call these, signed in or not; a
/// signed-in SuperAdmin's token is what lets a preview see a hidden product.
/// </summary>
/// <remarks>
/// Reads are result types: with the store switched off every one of these answers 404, and a page
/// must say "not found" for that rather than "nothing here".
/// </remarks>
public interface IBenStoreClient
{
    Task<ItemResult<StorePublicInfo>> GetStoreInfoAsync(CancellationToken token = default);

    Task<ItemResult<StoreHomeResponse>> GetStoreHomeAsync(CancellationToken token = default);

    Task<LoadResult<StoreCategoryCard>> GetStoreShelvesAsync(CancellationToken token = default);

    /// <summary>A shelf (<paramref name="categorySlug"/>) or every product, narrowed by <paramref name="query"/>.</summary>
    Task<ItemResult<StoreListingResponse>> GetStoreListingAsync(
        string? categorySlug, StoreListingQuery query, CancellationToken token = default);

    /// <summary>One product's page; <paramref name="preview"/> lets a SuperAdmin see a hidden one.</summary>
    Task<ItemResult<StoreProductDetail>> GetStoreProductPageAsync(string slug, bool preview = false, CancellationToken token = default);

    /// <summary>Counts a look at a product. Fire and forget — the answer is always nothing.</summary>
    Task RecordStoreProductViewAsync(Guid productId, CancellationToken token = default);

    /// <summary>What the search box offers as somebody types (two characters or more).</summary>
    Task<LoadResult<StoreSearchSuggestion>> SuggestStoreProductsAsync(string text, CancellationToken token = default);
}
