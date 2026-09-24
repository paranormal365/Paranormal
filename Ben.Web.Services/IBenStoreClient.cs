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

    // ── The cart (storefront S3.4) ───────────────────────────────────────────
    // The browser's cart travels as X-Ben-Cart, added by WebApiClient to every store call; the
    // account's, as the ordinary bearer. Each change answers with the whole cart.

    Task<ItemResult<StoreCartView>> GetCartAsync(CancellationToken token = default);

    Task<ItemResult<StoreCartCount>> GetCartCountAsync(CancellationToken token = default);

    Task<StoreCartChange> AddToCartAsync(AddToCartRequest request, CancellationToken token = default);

    /// <summary>0 removes the line.</summary>
    Task<StoreCartChange> SetCartQuantityAsync(Guid variantId, int quantity, CancellationToken token = default);

    Task<StoreCartChange> RemoveFromCartAsync(Guid variantId, CancellationToken token = default);

    Task<StoreCartChange> ApplyCartCouponAsync(string code, CancellationToken token = default);

    Task<StoreCartChange> RemoveCartCouponAsync(CancellationToken token = default);
}

/// <summary>What a change to the cart came back with.</summary>
/// <param name="Cart">The cart after the change — also after a capped one (409), which still changed it. Null when refused.</param>
/// <param name="Message">
/// What to tell the buyer: the refusal ("That item is no longer available."), or what happened
/// instead of what was asked ("Only 3 left."). Null when it went exactly as asked.
/// </param>
public sealed record StoreCartChange(StoreCartView? Cart, string? Message)
{
    public const string CouldNotUpdate = "Your cart couldn't be updated. Try again.";

    /// <summary>A 200, a 409 carrying the cart, or a refusal in the server's words.</summary>
    public static StoreCartChange From((StoreCartView? Result, string? Error, StoreCartView? Conflict) answer)
        => answer.Result is { } ok ? new(ok, null)
         : answer.Conflict is { } capped ? new(capped, capped.Notice ?? CouldNotUpdate)
         : new(null, answer.Error ?? CouldNotUpdate);
}
