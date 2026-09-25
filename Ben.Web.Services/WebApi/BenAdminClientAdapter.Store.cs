using Ben.Service.Models.Store;

namespace Ben.Web.Services.WebApi;

/// <summary>The public store slice — see <see cref="IBenStoreClient"/> for the contract.</summary>
/// <remarks>Every route is a literal so <c>EveryApiRouteHasACallerTests</c> can see who calls it.</remarks>
public sealed partial class BenAdminClientAdapter
{
    public Task<ItemResult<StorePublicInfo>> GetStoreInfoAsync(CancellationToken token = default)
        => _api.GetItemAsync<StorePublicInfo>("/api/public/store/info", token);

    public Task<ItemResult<StoreHomeResponse>> GetStoreHomeAsync(CancellationToken token = default)
        => _api.GetItemAsync<StoreHomeResponse>("/api/public/store/home", token);

    public Task<LoadResult<StoreCategoryCard>> GetStoreShelvesAsync(CancellationToken token = default)
        => _api.GetListAsync<StoreCategoryCard>("/api/public/store/categories", token);

    public Task<ItemResult<StoreListingResponse>> GetStoreListingAsync(
        string? categorySlug, StoreListingQuery query, CancellationToken token = default)
        => _api.GetItemAsync<StoreListingResponse>(
            (string.IsNullOrEmpty(categorySlug)
                ? "/api/public/store/products"
                : $"/api/public/store/categories/{Uri.EscapeDataString(categorySlug)}")
            + StoreListingQueryString.From(query), token);

    public Task<ItemResult<StoreProductDetail>> GetStoreProductPageAsync(string slug, bool preview = false, CancellationToken token = default)
        => _api.GetItemAsync<StoreProductDetail>(
            $"/api/public/store/products/{Uri.EscapeDataString(slug)}" + (preview ? "?preview=1" : ""), token);

    public async Task RecordStoreProductViewAsync(Guid productId, CancellationToken token = default)
    {
        try { await _api.PostAnonymousVoidAsync($"/api/public/store/products/{productId}/viewed", new { }, token); }
        catch (HttpRequestException) { }   // a look not counted is not worth a failure
    }

    public Task<LoadResult<StoreSearchSuggestion>> SuggestStoreProductsAsync(string text, CancellationToken token = default)
        => _api.GetListAsync<StoreSearchSuggestion>($"/api/public/store/search-suggest?q={Uri.EscapeDataString(text.Trim())}", token);

    // ── The cart (S3.4) ──────────────────────────────────────────────────────

    public Task<ItemResult<StoreCartView>> GetCartAsync(CancellationToken token = default)
        => _api.GetItemAsync<StoreCartView>("/api/store/cart", token);

    public Task<ItemResult<StoreCartCount>> GetCartCountAsync(CancellationToken token = default)
        => _api.GetItemAsync<StoreCartCount>("/api/store/cart/count", token);

    public async Task<StoreCartChange> AddToCartAsync(AddToCartRequest request, CancellationToken token = default)
        => StoreCartChange.From(await _api.SendExpectingConflictAsync<AddToCartRequest, StoreCartView, StoreCartView>(
            HttpMethod.Post, "/api/store/cart/items", request, token));

    public async Task<StoreCartChange> SetCartQuantityAsync(Guid variantId, int quantity, CancellationToken token = default)
        => StoreCartChange.From(await _api.SendExpectingConflictAsync<SetCartQuantityRequest, StoreCartView, StoreCartView>(
            HttpMethod.Put, $"/api/store/cart/items/{variantId}", new SetCartQuantityRequest(quantity), token));

    public async Task<StoreCartChange> RemoveFromCartAsync(Guid variantId, CancellationToken token = default)
    {
        var (cart, error, _) = await _api.SendWithStatusAsync<object, StoreCartView>(
            HttpMethod.Delete, $"/api/store/cart/items/{variantId}", null, token);
        return StoreCartChange.From((cart, error, null));
    }

    public async Task<StoreCartChange> ApplyCartCouponAsync(string code, CancellationToken token = default)
        => StoreCartChange.From(await _api.SendExpectingConflictAsync<ApplyCartCouponRequest, StoreCartView, StoreCartView>(
            HttpMethod.Post, "/api/store/cart/coupon", new ApplyCartCouponRequest(code), token));

    public async Task<StoreCartChange> RemoveCartCouponAsync(CancellationToken token = default)
    {
        var (cart, error, _) = await _api.SendWithStatusAsync<object, StoreCartView>(
            HttpMethod.Delete, "/api/store/cart/coupon", null, token);
        return StoreCartChange.From((cart, error, null));
    }

    // ── Checkout and orders (S4.10) ──────────────────────────────────────────

    public async Task<StoreCheckoutAttempt> PrepareCheckoutAsync(StoreCheckoutRequest request, CancellationToken token = default)
    {
        var (prepared, error, refusal) = await _api.SendExpectingConflictAsync<StoreCheckoutRequest, StoreCheckoutPrepared, StoreCheckoutRefusal>(
            HttpMethod.Post, "/api/store/checkout/payment-intent", request, token);
        return prepared is not null ? new StoreCheckoutAttempt(prepared, null)
             : refusal is not null ? new StoreCheckoutAttempt(null, refusal.Sentence, refusal.OrderId)
             : new StoreCheckoutAttempt(null, error ?? StoreCheckoutAttempt.CouldNotStart);
    }

    private static string WithToken(string path, string? accessToken)
        => string.IsNullOrEmpty(accessToken) ? path : $"{path}?t={Uri.EscapeDataString(accessToken)}";

    public Task<ItemResult<StoreOrderStatusView>> GetStoreOrderStatusAsync(Guid orderId, string? accessToken, CancellationToken token = default)
        => OrderDoorAsync<StoreOrderStatusView>(WithToken($"/api/store/orders/{orderId}/status", accessToken), token);

    public Task<ItemResult<StoreOrderView>> GetStoreOrderAsync(Guid orderId, string? accessToken, CancellationToken token = default)
        => OrderDoorAsync<StoreOrderView>(WithToken($"/api/store/orders/{orderId}", accessToken), token);

    public Task<ItemResult<StoreInvoiceRecord>> GetStoreOrderInvoiceAsync(Guid orderId, string? accessToken, CancellationToken token = default)
        => OrderDoorAsync<StoreInvoiceRecord>(WithToken($"/api/store/orders/{orderId}/invoice", accessToken), token);

    public Task<ItemResult<List<StoreOrderDownloadRecord>>> GetStoreOrderDownloadsAsync(Guid orderId, string? accessToken, CancellationToken token = default)
        => OrderDoorAsync<List<StoreOrderDownloadRecord>>(WithToken($"/api/store/orders/{orderId}/downloads", accessToken), token);

    public Task<ItemResult<StoreManualRecord>> GetStoreOrderManualAsync(Guid orderId, Guid fileId, string? accessToken, CancellationToken token = default)
        => OrderDoorAsync<StoreManualRecord>(WithToken($"/api/store/orders/{orderId}/downloads/{fileId}/manual", accessToken), token);

    public Task<ItemResult<StoreManualRecord>> GetStoreProductManualAsync(Guid fileId, CancellationToken token = default)
        => OrderDoorAsync<StoreManualRecord>($"/api/store/product-files/{fileId}/manual", token);

    /// <summary>
    /// An order door's answer: the record, NOTHING on 404, or a failure for anything else.
    /// </summary>
    /// <remarks>
    /// The order doors answer 404 on purpose for "not yours" (never 403), and the pages need to
    /// tell that apart from "the server could not answer": the first is "there's no order here"
    /// or a sign-in, the second a Retry. The shared item reader calls every 404 a failure, which
    /// drew "The server answered 404 (Not Found)." with a Retry that could never work — found by
    /// StoreOrderTests.A_stranger_sees_no_order (S4.13).
    /// </remarks>
    private async Task<ItemResult<T>> OrderDoorAsync<T>(string path, CancellationToken token)
    {
        try
        {
            var (result, error, status) = await _api.SendWithStatusAsync<object, T>(HttpMethod.Get, path, null, token);
            return status is >= 200 and < 300 ? ItemResult<T>.Ok(result)
                 : status == 404 ? ItemResult<T>.Ok(default)
                 : ItemResult<T>.Failure(error);
        }
        catch (HttpRequestException)
        {
            return ItemResult<T>.Failure();
        }
    }

    public Task<LoadResult<StoreOrderSummaryView>> GetMyStoreOrdersAsync(int? months = null, CancellationToken token = default)
        => _api.GetListAsync<StoreOrderSummaryView>("/api/me/store/orders" + (months is > 0 ? $"?months={months}" : ""), token);

    public Task<bool> LookupStoreOrderAsync(GuestOrderLookupRequest request, CancellationToken token = default)
        => _api.PostAnonymousVoidAsync("/api/store/orders/lookup", request, token);

    public async Task<bool> SimulateFakePaymentAsync(Guid orderId, CancellationToken token = default)
        => (await _api.SendWithStatusAsync<object, object>(HttpMethod.Post, $"/api/store/checkout/dev/simulate-payment/{orderId}", new { }, token)).Status is >= 200 and < 300;

    public async Task<bool> ExpireReservationForTestAsync(Guid orderId, CancellationToken token = default)
        => (await _api.SendWithStatusAsync<object, object>(HttpMethod.Post, $"/api/store/checkout/dev/expire-reservation/{orderId}", new { }, token)).Status is >= 200 and < 300;

    // ── Favourites and reviews (S6.3) ────────────────────────────────────────

    public Task<ItemResult<StoreReviewPage>> GetStoreReviewsAsync(string productSlug, string? sort = null, int page = 1, CancellationToken token = default)
        => _api.GetItemAsync<StoreReviewPage>(
            $"/api/public/store/products/{Uri.EscapeDataString(productSlug)}/reviews?sort={Uri.EscapeDataString(sort ?? StoreReviewSorts.Popular)}&page={page}", token);

    public Task<LoadResult<StoreProductCard>> GetMyStoreFavouritesAsync(CancellationToken token = default)
        => _api.GetListAsync<StoreProductCard>("/api/me/store/engagement/favourites", token);

    public Task<ItemResult<StoreFavouriteCount>> GetMyStoreFavouriteCountAsync(CancellationToken token = default)
        => _api.GetItemAsync<StoreFavouriteCount>("/api/me/store/engagement/favourites/count", token);

    public Task<(StoreFavouriteCount? Result, string? Error)> AddStoreFavouriteAsync(Guid productId, CancellationToken token = default)
        => _api.SendExpectingReasonAsync<object, StoreFavouriteCount>(HttpMethod.Put, $"/api/me/store/engagement/favourites/{productId}", new { }, token);

    public Task<(StoreFavouriteCount? Result, string? Error)> RemoveStoreFavouriteAsync(Guid productId, CancellationToken token = default)
        => _api.SendExpectingReasonAsync<object, StoreFavouriteCount>(HttpMethod.Delete, $"/api/me/store/engagement/favourites/{productId}", new { }, token);

    public Task<ItemResult<StoreProductViewerState>> GetStoreProductViewerStateAsync(Guid productId, CancellationToken token = default)
        => _api.GetItemAsync<StoreProductViewerState>($"/api/me/store/engagement/products/{productId}/state", token);

    public Task<(MyStoreReviewRecord? Result, string? Error)> SaveStoreReviewAsync(Guid productId, SubmitStoreReviewRequest request, CancellationToken token = default)
        => _api.SendExpectingReasonAsync<SubmitStoreReviewRequest, MyStoreReviewRecord>(HttpMethod.Put, $"/api/me/store/engagement/products/{productId}/review", request, token);

    public async Task<(bool Deleted, string? Error)> DeleteMyStoreReviewAsync(Guid productId, CancellationToken token = default)
    {
        var (_, error, status) = await _api.SendWithStatusAsync<object, object>(HttpMethod.Delete, $"/api/me/store/engagement/products/{productId}/review", null, token);
        return status is >= 200 and < 300 ? (true, null) : (false, error ?? "The review couldn't be removed.");
    }

    public Task<(StoreHelpfulVoteResult? Result, string? Error)> SetStoreReviewHelpfulAsync(Guid reviewId, bool helpful, CancellationToken token = default)
        => _api.SendExpectingReasonAsync<object, StoreHelpfulVoteResult>(helpful ? HttpMethod.Post : HttpMethod.Delete,
            $"/api/me/store/engagement/reviews/{reviewId}/helpful", new { }, token);
}
