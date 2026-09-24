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
        => _api.GetItemAsync<StoreOrderStatusView>(WithToken($"/api/store/orders/{orderId}/status", accessToken), token);

    public Task<ItemResult<StoreOrderView>> GetStoreOrderAsync(Guid orderId, string? accessToken, CancellationToken token = default)
        => _api.GetItemAsync<StoreOrderView>(WithToken($"/api/store/orders/{orderId}", accessToken), token);

    public Task<ItemResult<StoreInvoiceRecord>> GetStoreOrderInvoiceAsync(Guid orderId, string? accessToken, CancellationToken token = default)
        => _api.GetItemAsync<StoreInvoiceRecord>(WithToken($"/api/store/orders/{orderId}/invoice", accessToken), token);

    public Task<LoadResult<StoreOrderSummaryView>> GetMyStoreOrdersAsync(int? months = null, CancellationToken token = default)
        => _api.GetListAsync<StoreOrderSummaryView>("/api/me/store/orders" + (months is > 0 ? $"?months={months}" : ""), token);

    public Task<bool> LookupStoreOrderAsync(GuestOrderLookupRequest request, CancellationToken token = default)
        => _api.PostAnonymousVoidAsync("/api/store/orders/lookup", request, token);

    public async Task<bool> SimulateFakePaymentAsync(Guid orderId, CancellationToken token = default)
        => (await _api.SendWithStatusAsync<object, object>(HttpMethod.Post, $"/api/store/checkout/dev/simulate-payment/{orderId}", new { }, token)).Status is >= 200 and < 300;

    public async Task<bool> ExpireReservationForTestAsync(Guid orderId, CancellationToken token = default)
        => (await _api.SendWithStatusAsync<object, object>(HttpMethod.Post, $"/api/store/checkout/dev/expire-reservation/{orderId}", new { }, token)).Status is >= 200 and < 300;
}
