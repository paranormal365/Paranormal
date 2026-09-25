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

    // ── Checkout and orders (storefront S4.10) ───────────────────────────────

    /// <summary>Places the order and starts its payment. Exactly one of the answer's parts is set.</summary>
    Task<StoreCheckoutAttempt> PrepareCheckoutAsync(StoreCheckoutRequest request, CancellationToken token = default);

    /// <param name="accessToken">The order's private link token, for a guest; null for the buyer signed in or the browser that placed it.</param>
    Task<ItemResult<StoreOrderStatusView>> GetStoreOrderStatusAsync(Guid orderId, string? accessToken, CancellationToken token = default);

    Task<ItemResult<StoreOrderView>> GetStoreOrderAsync(Guid orderId, string? accessToken, CancellationToken token = default);

    Task<ItemResult<StoreInvoiceRecord>> GetStoreOrderInvoiceAsync(Guid orderId, string? accessToken, CancellationToken token = default);

    /// <summary>The signed-in shopper's questions about the store's items, newest first (store sellers P12).</summary>
    Task<LoadResult<StoreAskedQuestionRecord>> GetMyStoreQuestionsAsync(CancellationToken token = default);

    /// <summary>Asks a question about a product. The refusal says why in words.</summary>
    Task<(StoreAskedQuestionRecord? Result, string? Error)> AskStoreQuestionAsync(Guid productId, string question, CancellationToken token = default);

    /// <summary>What a paid order lets its buyer download (store sellers P11). Empty when nothing, or not theirs.</summary>
    Task<ItemResult<List<StoreOrderDownloadRecord>>> GetStoreOrderDownloadsAsync(Guid orderId, string? accessToken, CancellationToken token = default);

    /// <summary>A written manual from a paid order, to read or print.</summary>
    Task<ItemResult<StoreManualRecord>> GetStoreOrderManualAsync(Guid orderId, Guid fileId, string? accessToken, CancellationToken token = default);

    /// <summary>A written manual for the people who keep it — the store's staff and the product's seller.</summary>
    Task<ItemResult<StoreManualRecord>> GetStoreProductManualAsync(Guid fileId, CancellationToken token = default);

    /// <summary>My Orders: the signed-in person's paid orders, newest first.</summary>
    Task<LoadResult<StoreOrderSummaryView>> GetMyStoreOrdersAsync(int? months = null, CancellationToken token = default);

    /// <summary>"Find my order". The answer is the same whether or not anything matched.</summary>
    Task<bool> LookupStoreOrderAsync(GuestOrderLookupRequest request, CancellationToken token = default);

    /// <summary>Test checkout only: pays the order as Stripe would.</summary>
    Task<bool> SimulateFakePaymentAsync(Guid orderId, CancellationToken token = default);

    /// <summary>Test checkout only: runs the order's reservation out.</summary>
    Task<bool> ExpireReservationForTestAsync(Guid orderId, CancellationToken token = default);

    // ── Favourites and reviews (storefront S6.3) ─────────────────────────────

    /// <summary>A product's approved reviews, a page at a time; <paramref name="sort"/> as StoreReviewSorts.</summary>
    Task<ItemResult<StoreReviewPage>> GetStoreReviewsAsync(string productSlug, string? sort = null, int page = 1, CancellationToken token = default);

    /// <summary>The signed-in shopper's favourites that are on the store today.</summary>
    Task<LoadResult<StoreProductCard>> GetMyStoreFavouritesAsync(CancellationToken token = default);

    Task<ItemResult<StoreFavouriteCount>> GetMyStoreFavouriteCountAsync(CancellationToken token = default);

    Task<(StoreFavouriteCount? Result, string? Error)> AddStoreFavouriteAsync(Guid productId, CancellationToken token = default);

    Task<(StoreFavouriteCount? Result, string? Error)> RemoveStoreFavouriteAsync(Guid productId, CancellationToken token = default);

    /// <summary>Whether the reader has hearted it, may review it, and their own review.</summary>
    Task<ItemResult<StoreProductViewerState>> GetStoreProductViewerStateAsync(Guid productId, CancellationToken token = default);

    Task<(MyStoreReviewRecord? Result, string? Error)> SaveStoreReviewAsync(Guid productId, SubmitStoreReviewRequest request, CancellationToken token = default);

    Task<(bool Deleted, string? Error)> DeleteMyStoreReviewAsync(Guid productId, CancellationToken token = default);

    Task<(StoreHelpfulVoteResult? Result, string? Error)> SetStoreReviewHelpfulAsync(Guid reviewId, bool helpful, CancellationToken token = default);
}

/// <summary>What pressing Continue came to.</summary>
/// <param name="StillProcessingOrderId">An earlier payment on this cart is still going through — the page polls it.</param>
public sealed record StoreCheckoutAttempt(StoreCheckoutPrepared? Prepared, string? Error, Guid? StillProcessingOrderId = null)
{
    public const string CouldNotStart = "The checkout couldn't be started just now. Try again in a moment.";
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
