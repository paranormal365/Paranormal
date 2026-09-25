using Ben.Data.Common.Enums;
using Ben.Service.Models.Store;

namespace Ben.Web.Services.WebApi;

/// <summary>The store back-office slice — see <see cref="IBenStoreAdminClient"/> for the contract.</summary>
/// <remarks>Every route is a literal so <c>EveryApiRouteHasACallerTests</c> can see who calls it.</remarks>
public sealed partial class BenAdminClientAdapter
{
    // ── categories ───────────────────────────────────────────────────────────

    public Task<LoadResult<StoreCategoryAdminRecord>> GetStoreCategoriesAsync(CancellationToken token = default)
        => _api.GetListAsync<StoreCategoryAdminRecord>("/api/admin/store/categories", token);

    public Task<(StoreCategoryAdminRecord? Result, string? Error)> CreateStoreCategoryAsync(
        SaveStoreCategoryRequest request, CancellationToken token = default)
        => _api.SendExpectingReasonAsync<SaveStoreCategoryRequest, StoreCategoryAdminRecord>(
               HttpMethod.Post, "/api/admin/store/categories", request, token);

    public Task<(StoreCategorySaveResult? Result, string? Error)> SaveStoreCategoryAsync(
        Guid categoryId, SaveStoreCategoryRequest request, CancellationToken token = default)
        => _api.SendExpectingReasonAsync<SaveStoreCategoryRequest, StoreCategorySaveResult>(
               HttpMethod.Put, $"/api/admin/store/categories/{categoryId}", request, token);

    public Task<(List<StoreCategoryAdminRecord>? Result, string? Error)> ReorderStoreCategoriesAsync(
        ReorderRequest request, CancellationToken token = default)
        => _api.SendExpectingReasonAsync<ReorderRequest, List<StoreCategoryAdminRecord>>(
               HttpMethod.Put, "/api/admin/store/categories/reorder", request, token);

    public Task<(StoreCategoryAdminRecord? Result, string? Error)> SetStoreCategoryImageAsync(
        Guid categoryId, MultipartFormDataContent content, CancellationToken token = default)
        => _api.PostMultipartExpectingReasonAsync<StoreCategoryAdminRecord>(
               $"/api/admin/store/categories/{categoryId}/image", content, token);

    public Task<(StoreCategoryAdminRecord? Result, string? Error)> RemoveStoreCategoryImageAsync(
        Guid categoryId, CancellationToken token = default)
        => _api.SendExpectingReasonAsync<object, StoreCategoryAdminRecord>(
               HttpMethod.Delete, $"/api/admin/store/categories/{categoryId}/image", new { }, token);

    public Task<(bool Deleted, string? Error)> DeleteStoreCategoryAsync(Guid categoryId, CancellationToken token = default)
        => _api.DeleteExpectingReasonAsync($"/api/admin/store/categories/{categoryId}", token);

    // ── products ─────────────────────────────────────────────────────────────

    public Task<LoadResult<StoreProductListAdminRecord>> GetStoreProductsAsync(
        string? search = null, Guid? categoryId = null, bool? active = null, CancellationToken token = default)
    {
        var query = new List<string>();
        if (!string.IsNullOrWhiteSpace(search)) query.Add($"q={Uri.EscapeDataString(search.Trim())}");
        if (categoryId is { } c) query.Add($"categoryId={c}");
        if (active is { } a) query.Add($"active={(a ? "true" : "false")}");
        return _api.GetListAsync<StoreProductListAdminRecord>(
            "/api/admin/store/products" + (query.Count == 0 ? "" : "?" + string.Join('&', query)), token);
    }

    public Task<ItemResult<StoreProductAdminRecord>> GetStoreProductAsync(Guid productId, CancellationToken token = default)
        => _api.GetItemAsync<StoreProductAdminRecord>($"/api/admin/store/products/{productId}", token);

    public Task<LoadResult<StoreSellerRecord>> GetStoreSellersAsync(CancellationToken token = default)
        => _api.GetListAsync<StoreSellerRecord>("/api/admin/store/products/sellers", token);

    public Task<(StoreProductAdminRecord? Result, string? Error)> CreateStoreProductAsync(
        CreateStoreProductRequest request, CancellationToken token = default)
        => _api.SendExpectingReasonAsync<CreateStoreProductRequest, StoreProductAdminRecord>(
               HttpMethod.Post, "/api/admin/store/products", request, token);

    public Task<(StoreProductAdminRecord? Result, string? Error)> SaveStoreProductAsync(
        Guid productId, SaveStoreProductRequest request, CancellationToken token = default)
        => _api.SendExpectingReasonAsync<SaveStoreProductRequest, StoreProductAdminRecord>(
               HttpMethod.Put, $"/api/admin/store/products/{productId}", request, token);

    public Task<(StoreProductAdminRecord? Result, string? Error)> ActivateStoreProductAsync(Guid productId, CancellationToken token = default)
        => _api.SendExpectingReasonAsync<object, StoreProductAdminRecord>(
               HttpMethod.Post, $"/api/admin/store/products/{productId}/activate", new { }, token);

    public Task<(StoreProductAdminRecord? Result, string? Error)> DeactivateStoreProductAsync(Guid productId, CancellationToken token = default)
        => _api.SendExpectingReasonAsync<object, StoreProductAdminRecord>(
               HttpMethod.Post, $"/api/admin/store/products/{productId}/deactivate", new { }, token);

    public Task<(StoreProductAdminRecord? Result, string? Error)> DuplicateStoreProductAsync(Guid productId, CancellationToken token = default)
        => _api.SendExpectingReasonAsync<object, StoreProductAdminRecord>(
               HttpMethod.Post, $"/api/admin/store/products/{productId}/duplicate", new { }, token);

    public Task<(bool Deleted, string? Error)> DeleteStoreProductAsync(Guid productId, CancellationToken token = default)
        => _api.DeleteExpectingReasonAsync($"/api/admin/store/products/{productId}", token);

    public Task<(StoreProductAdminRecord? Result, string? Error)> SaveStoreProductOptionsAsync(
        Guid productId, SaveStoreOptionsRequest request, CancellationToken token = default)
        => _api.SendExpectingReasonAsync<SaveStoreOptionsRequest, StoreProductAdminRecord>(
               HttpMethod.Put, $"/api/admin/store/products/{productId}/options", request, token);

    public Task<(StoreVariantsGenerated? Result, string? Error)> GenerateStoreVariantsAsync(Guid productId, CancellationToken token = default)
        => _api.SendExpectingReasonAsync<object, StoreVariantsGenerated>(
               HttpMethod.Post, $"/api/admin/store/products/{productId}/variants/generate", new { }, token);

    public Task<(StoreProductAdminRecord? Result, string? Error)> CreateStoreVariantAsync(
        Guid productId, SaveStoreVariantRequest request, CancellationToken token = default)
        => _api.SendExpectingReasonAsync<SaveStoreVariantRequest, StoreProductAdminRecord>(
               HttpMethod.Post, $"/api/admin/store/products/{productId}/variants", request, token);

    public Task<(StoreProductAdminRecord? Result, string? Error)> SaveStoreVariantAsync(
        Guid productId, Guid variantId, SaveStoreVariantRequest request, CancellationToken token = default)
        => _api.SendExpectingReasonAsync<SaveStoreVariantRequest, StoreProductAdminRecord>(
               HttpMethod.Put, $"/api/admin/store/products/{productId}/variants/{variantId}", request, token);

    public Task<(StoreProductAdminRecord? Result, string? Error)> DeleteStoreVariantAsync(
        Guid productId, Guid variantId, CancellationToken token = default)
        => _api.SendExpectingReasonAsync<object, StoreProductAdminRecord>(
               HttpMethod.Delete, $"/api/admin/store/products/{productId}/variants/{variantId}", new { }, token);

    public Task<(StoreProductAdminRecord? Result, string? Error)> AdjustStoreVariantStockAsync(
        Guid productId, Guid variantId, AdjustStockRequest request, CancellationToken token = default)
        => _api.SendExpectingReasonAsync<AdjustStockRequest, StoreProductAdminRecord>(
               HttpMethod.Post, $"/api/admin/store/products/{productId}/variants/{variantId}/stock", request, token);

    public Task<LoadResult<StoreStockMovementRecord>> GetStoreVariantStockLogAsync(
        Guid productId, Guid variantId, CancellationToken token = default)
        => _api.GetListAsync<StoreStockMovementRecord>($"/api/admin/store/products/{productId}/variants/{variantId}/stock", token);

    public Task<LoadResult<StoreProductChangeRecord>> GetStoreProductHistoryAsync(Guid productId, CancellationToken token = default)
        => _api.GetListAsync<StoreProductChangeRecord>($"/api/admin/store/products/{productId}/history", token);

    // ── parts and cost (store sellers P4) ────────────────────────────────────

    public Task<ItemResult<StoreProductEconomicsRecord>> GetStoreProductEconomicsAsync(Guid productId, CancellationToken token = default)
        => _api.GetItemAsync<StoreProductEconomicsRecord>($"/api/admin/store/products/{productId}/economics", token);

    public Task<ItemResult<StorePartsRecord>> GetStoreProductPartsAsync(Guid productId, CancellationToken token = default)
        => _api.GetItemAsync<StorePartsRecord>($"/api/admin/store/products/{productId}/parts", token);

    public Task<(StorePartsRecord? Result, string? Error)> SaveStoreProductPartsAsync(
        Guid productId, SaveStorePartsRequest request, CancellationToken token = default)
        => _api.SendExpectingReasonAsync<SaveStorePartsRequest, StorePartsRecord>(
               HttpMethod.Put, $"/api/admin/store/products/{productId}/parts", request, token);

    public Task<(StorePartsRecord? Result, string? Error)> SetStorePartPictureAsync(
        Guid productId, Guid partId, MultipartFormDataContent content, CancellationToken token = default)
        => _api.PostMultipartExpectingReasonAsync<StorePartsRecord>($"/api/admin/store/products/{productId}/parts/{partId}/picture", content, token);

    public Task<(StorePartsRecord? Result, string? Error)> RemoveStorePartPictureAsync(Guid productId, Guid partId, CancellationToken token = default)
        => _api.SendExpectingReasonAsync<object, StorePartsRecord>(
               HttpMethod.Delete, $"/api/admin/store/products/{productId}/parts/{partId}/picture", new { }, token);

    // ── sellers' earnings and payouts (store sellers P10) ────────────────────

    public Task<LoadResult<StoreSellerBalanceRecord>> GetStoreSellerBalancesAsync(CancellationToken token = default)
        => _api.GetListAsync<StoreSellerBalanceRecord>("/api/admin/store/sellers", token);

    public Task<ItemResult<StoreSellerDetailRecord>> GetStoreSellerAsync(Guid sellerId, CancellationToken token = default)
        => _api.GetItemAsync<StoreSellerDetailRecord>($"/api/admin/store/sellers/{sellerId}", token);

    public Task<(StoreSellerDetailRecord? Result, string? Error)> RecordStoreSellerPaymentAsync(Guid sellerId, RecordSellerPaymentRequest request, CancellationToken token = default)
        => _api.SendExpectingReasonAsync<RecordSellerPaymentRequest, StoreSellerDetailRecord>(
               HttpMethod.Post, $"/api/admin/store/sellers/{sellerId}/payments", request, token);

    public Task<(StoreSellerDetailRecord? Result, string? Error)> VoidStoreSellerPaymentAsync(Guid sellerId, Guid payoutId, VoidSellerPaymentRequest request, CancellationToken token = default)
        => _api.SendExpectingReasonAsync<VoidSellerPaymentRequest, StoreSellerDetailRecord>(
               HttpMethod.Post, $"/api/admin/store/sellers/{sellerId}/payments/{payoutId}/void", request, token);

    public Task<(StoreSellerDetailRecord? Result, string? Error)> AdjustStoreSellerAsync(Guid sellerId, SellerAdjustmentRequest request, CancellationToken token = default)
        => _api.SendExpectingReasonAsync<SellerAdjustmentRequest, StoreSellerDetailRecord>(
               HttpMethod.Post, $"/api/admin/store/sellers/{sellerId}/adjustments", request, token);

    public async Task<(byte[] Data, string FileName)?> DownloadStoreSellerCsvAsync(Guid sellerId, CancellationToken token = default)
    {
        var result = await _api.GetBytesAsync($"/api/admin/store/sellers/{sellerId}/export.csv", "seller-earnings.csv", token);
        return result is { } r ? (r.Data, r.FileName) : null;
    }

    // ── sale requests (store sellers P3) ─────────────────────────────────────

    public Task<LoadResult<StoreSaleRequestRecord>> GetStoreSaleRequestsAsync(bool decided = false, CancellationToken token = default)
        => _api.GetListAsync<StoreSaleRequestRecord>("/api/admin/store/sale-requests" + (decided ? "?decided=true" : ""), token);

    public Task<(StoreSaleRequestRecord? Result, string? Error)> ApproveStoreSaleRequestAsync(Guid requestId, CancellationToken token = default)
        => _api.SendExpectingReasonAsync<object, StoreSaleRequestRecord>(
               HttpMethod.Post, $"/api/admin/store/sale-requests/{requestId}/approve", new { }, token);

    public Task<(StoreSaleRequestRecord? Result, string? Error)> DeclineStoreSaleRequestAsync(
        Guid requestId, DeclineSaleRequest request, CancellationToken token = default)
        => _api.SendExpectingReasonAsync<DeclineSaleRequest, StoreSaleRequestRecord>(
               HttpMethod.Post, $"/api/admin/store/sale-requests/{requestId}/decline", request, token);

    public Task<(StoreProductAdminRecord? Result, string? Error)> AddStoreProductImageAsync(
        Guid productId, MultipartFormDataContent content, CancellationToken token = default)
        => _api.PostMultipartExpectingReasonAsync<StoreProductAdminRecord>($"/api/admin/store/products/{productId}/images", content, token);

    public Task<(StoreProductAdminRecord? Result, string? Error)> ReorderStoreProductImagesAsync(
        Guid productId, ReorderRequest request, CancellationToken token = default)
        => _api.SendExpectingReasonAsync<ReorderRequest, StoreProductAdminRecord>(
               HttpMethod.Put, $"/api/admin/store/products/{productId}/images/reorder", request, token);

    public Task<(StoreProductAdminRecord? Result, string? Error)> SaveStoreProductImageAsync(
        Guid productId, Guid imageId, UpdateStoreImageRequest request, CancellationToken token = default)
        => _api.SendExpectingReasonAsync<UpdateStoreImageRequest, StoreProductAdminRecord>(
               HttpMethod.Put, $"/api/admin/store/products/{productId}/images/{imageId}", request, token);

    public Task<(StoreProductAdminRecord? Result, string? Error)> DeleteStoreProductImageAsync(
        Guid productId, Guid imageId, CancellationToken token = default)
        => _api.SendExpectingReasonAsync<object, StoreProductAdminRecord>(
               HttpMethod.Delete, $"/api/admin/store/products/{productId}/images/{imageId}", new { }, token);

    // ── stock ────────────────────────────────────────────────────────────────

    public Task<LoadResult<StoreStockRow>> GetStoreStockAsync(string? search = null, bool lowOnly = false, CancellationToken token = default)
    {
        var query = new List<string>();
        if (!string.IsNullOrWhiteSpace(search)) query.Add($"q={Uri.EscapeDataString(search.Trim())}");
        if (lowOnly) query.Add("low=true");
        return _api.GetListAsync<StoreStockRow>("/api/admin/store/stock" + (query.Count == 0 ? "" : "?" + string.Join('&', query)), token);
    }

    public Task<(StoreStockAdjusted? Result, string? Error)> AdjustStoreStockAsync(
        BulkAdjustStockRequest request, CancellationToken token = default)
        => _api.SendExpectingReasonAsync<BulkAdjustStockRequest, StoreStockAdjusted>(
               HttpMethod.Post, "/api/admin/store/stock/adjust", request, token);

    public Task<(StoreStockAdjusted? Result, string? Error)> ImportStoreStockAsync(
        MultipartFormDataContent content, CancellationToken token = default)
        => _api.PostMultipartExpectingReasonAsync<StoreStockAdjusted>("/api/admin/store/stock/import", content, token);

    public async Task<(byte[] Data, string FileName)?> DownloadStoreStockCsvAsync(CancellationToken token = default)
    {
        var result = await _api.GetBytesAsync("/api/admin/store/stock/export.csv", "store-stock.csv", token);
        return result is { } r ? (r.Data, r.FileName) : null;
    }

    // ── discount codes ───────────────────────────────────────────────────────

    public Task<LoadResult<StoreCouponAdminRecord>> GetStoreCouponsAsync(CancellationToken token = default)
        => _api.GetListAsync<StoreCouponAdminRecord>("/api/admin/store/coupons", token);

    public Task<(StoreCouponAdminRecord? Result, string? Error)> CreateStoreCouponAsync(
        SaveStoreCouponRequest request, CancellationToken token = default)
        => _api.SendExpectingReasonAsync<SaveStoreCouponRequest, StoreCouponAdminRecord>(
               HttpMethod.Post, "/api/admin/store/coupons", request, token);

    public Task<(StoreCouponAdminRecord? Result, string? Error)> SaveStoreCouponAsync(
        Guid couponId, SaveStoreCouponRequest request, CancellationToken token = default)
        => _api.SendExpectingReasonAsync<SaveStoreCouponRequest, StoreCouponAdminRecord>(
               HttpMethod.Put, $"/api/admin/store/coupons/{couponId}", request, token);

    public Task<(bool Deleted, string? Error)> DeleteStoreCouponAsync(Guid couponId, CancellationToken token = default)
        => _api.DeleteExpectingReasonAsync($"/api/admin/store/coupons/{couponId}", token);

    // ── reviews ──────────────────────────────────────────────────────────────

    public Task<LoadResult<StoreReviewAdminRecord>> GetStoreReviewsAsync(
        StoreReviewStatus? status = null, string? search = null, Guid? productId = null, CancellationToken token = default)
    {
        var query = new List<string>();
        if (status is { } s) query.Add($"status={s}");
        if (!string.IsNullOrWhiteSpace(search)) query.Add($"q={Uri.EscapeDataString(search.Trim())}");
        if (productId is { } p) query.Add($"productId={p}");
        return _api.GetListAsync<StoreReviewAdminRecord>(
            "/api/admin/store/reviews" + (query.Count == 0 ? "" : "?" + string.Join('&', query)), token);
    }

    public Task<(StoreReviewAdminRecord? Result, string? Error)> ApproveStoreReviewAsync(Guid reviewId, CancellationToken token = default)
        => _api.SendExpectingReasonAsync<object, StoreReviewAdminRecord>(
               HttpMethod.Post, $"/api/admin/store/reviews/{reviewId}/approve", new { }, token);

    public Task<(StoreReviewAdminRecord? Result, string? Error)> RejectStoreReviewAsync(
        Guid reviewId, RejectStoreReviewRequest request, CancellationToken token = default)
        => _api.SendExpectingReasonAsync<RejectStoreReviewRequest, StoreReviewAdminRecord>(
               HttpMethod.Post, $"/api/admin/store/reviews/{reviewId}/reject", request, token);

    public Task<(StoreReviewAdminRecord? Result, string? Error)> ReplyToStoreReviewAsync(
        Guid reviewId, ReplyToStoreReviewRequest request, CancellationToken token = default)
        => _api.SendExpectingReasonAsync<ReplyToStoreReviewRequest, StoreReviewAdminRecord>(
               HttpMethod.Put, $"/api/admin/store/reviews/{reviewId}/reply", request, token);

    public Task<(bool Deleted, string? Error)> DeleteStoreReviewAsync(Guid reviewId, CancellationToken token = default)
        => _api.DeleteExpectingReasonAsync($"/api/admin/store/reviews/{reviewId}", token);

    // ── settings and dashboard ───────────────────────────────────────────────

    public Task<ItemResult<StoreSettingsAdminRecord>> GetStoreSettingsAsync(CancellationToken token = default)
        => _api.GetItemAsync<StoreSettingsAdminRecord>("/api/admin/store/settings", token);

    public Task<(StoreSettingsAdminRecord? Result, string? Error)> SaveStoreSettingsAsync(
        SaveStoreSettingsRequest request, CancellationToken token = default)
        => _api.SendExpectingReasonAsync<SaveStoreSettingsRequest, StoreSettingsAdminRecord>(
               HttpMethod.Put, "/api/admin/store/settings", request, token);

    public Task<ItemResult<StoreDashboardRecord>> GetStoreDashboardAsync(int days = 30, CancellationToken token = default)
        => _api.GetItemAsync<StoreDashboardRecord>($"/api/admin/store/dashboard?days={days}", token);

    // ── the order desk (S5.5) ────────────────────────────────────────────────

    public Task<LoadResult<StoreOrderListRecord>> GetStoreOrdersAsync(StoreOrderListQuery query, CancellationToken token = default)
        => _api.GetListAsync<StoreOrderListRecord>($"/api/admin/store/orders?{query.Parameters()}", token);

    public Task<ItemResult<StoreOrderDetailAdminRecord>> GetStoreOrderAdminAsync(Guid orderId, CancellationToken token = default)
        => _api.GetItemAsync<StoreOrderDetailAdminRecord>($"/api/admin/store/orders/{orderId}", token);

    public Task<ItemResult<StoreInvoiceRecord>> GetStoreOrderInvoiceAdminAsync(Guid orderId, CancellationToken token = default)
        => _api.GetItemAsync<StoreInvoiceRecord>($"/api/admin/store/orders/{orderId}/invoice", token);

    public Task<LoadResult<StoreRefundRecord>> GetStoreOrderRefundsAsync(Guid orderId, CancellationToken token = default)
        => _api.GetListAsync<StoreRefundRecord>($"/api/admin/store/orders/{orderId}/refunds", token);

    public async Task<(byte[] Data, string FileName)?> DownloadStoreOrdersCsvAsync(StoreOrderListQuery query, CancellationToken token = default)
    {
        var result = await _api.GetBytesAsync($"/api/admin/store/orders/export.csv?{query.Parameters()}", "store-orders.csv", token);
        return result is { } r ? (r.Data, r.FileName) : null;
    }

    public async Task<(byte[] Data, string FileName)?> DownloadStoreRefundsCsvAsync(DateTime? from = null, DateTime? to = null, CancellationToken token = default)
    {
        var query = new StoreOrderListQuery(From: from, To: to).Parameters();
        var result = await _api.GetBytesAsync($"/api/admin/store/orders/refunds/export.csv?{query}", "store-refunds.csv", token);
        return result is { } r ? (r.Data, r.FileName) : null;
    }

    private Task<(StoreOrderDetailAdminRecord? Result, string? Error)> DeskAsync(HttpMethod method, string path, object? body, CancellationToken token)
        => _api.SendExpectingReasonAsync<object, StoreOrderDetailAdminRecord>(method, path, body ?? new { }, token);

    // Packages (store sellers P7): each is packed, shipped and delivered on its own.
    public Task<(StoreOrderDetailAdminRecord? Result, string? Error)> PackStoreParcelAsync(Guid orderId, Guid parcelId, CancellationToken token = default)
        => DeskAsync(HttpMethod.Post, $"/api/admin/store/orders/{orderId}/parcels/{parcelId}/pack", null, token);

    public Task<(StoreOrderDetailAdminRecord? Result, string? Error)> ShipStoreParcelAsync(Guid orderId, Guid parcelId, StoreShipmentInfo shipment, CancellationToken token = default)
        => DeskAsync(HttpMethod.Post, $"/api/admin/store/orders/{orderId}/parcels/{parcelId}/ship", shipment, token);

    public Task<(StoreOrderDetailAdminRecord? Result, string? Error)> CorrectStoreParcelTrackingAsync(Guid orderId, Guid parcelId, StoreShipmentInfo shipment, CancellationToken token = default)
        => DeskAsync(HttpMethod.Put, $"/api/admin/store/orders/{orderId}/parcels/{parcelId}/tracking", shipment, token);

    public Task<(StoreOrderDetailAdminRecord? Result, string? Error)> DeliverStoreParcelAsync(Guid orderId, Guid parcelId, CancellationToken token = default)
        => DeskAsync(HttpMethod.Post, $"/api/admin/store/orders/{orderId}/parcels/{parcelId}/deliver", null, token);

    public Task<(StoreOrderDetailAdminRecord? Result, string? Error)> CancelStoreParcelAsync(Guid orderId, Guid parcelId, CancelStoreOrderRequest request, CancellationToken token = default)
        => DeskAsync(HttpMethod.Post, $"/api/admin/store/orders/{orderId}/parcels/{parcelId}/cancel", request, token);

    public Task<(StoreOrderDetailAdminRecord? Result, string? Error)> CancelStoreOrderAsync(Guid orderId, CancelStoreOrderRequest request, CancellationToken token = default)
        => DeskAsync(HttpMethod.Post, $"/api/admin/store/orders/{orderId}/cancel", request, token);

    public Task<(StoreOrderDetailAdminRecord? Result, string? Error)> AddStoreOrderNoteAsync(Guid orderId, string note, CancellationToken token = default)
        => DeskAsync(HttpMethod.Post, $"/api/admin/store/orders/{orderId}/notes", new AddStoreOrderNoteRequest(note), token);

    public Task<(StoreOrderDetailAdminRecord? Result, string? Error)> ChangeStoreOrderAddressAsync(Guid orderId, ChangeStoreOrderAddressRequest request, CancellationToken token = default)
        => DeskAsync(HttpMethod.Put, $"/api/admin/store/orders/{orderId}/address", request, token);

    public Task<(StoreOrderDetailAdminRecord? Result, string? Error)> ClearStoreOrderAttentionAsync(Guid orderId, CancellationToken token = default)
        => DeskAsync(HttpMethod.Post, $"/api/admin/store/orders/{orderId}/attention/clear", null, token);

    public Task<(StoreOrderDetailAdminRecord? Result, string? Error)> ResendStoreOrderLetterAsync(Guid orderId, string kind, CancellationToken token = default)
        => DeskAsync(HttpMethod.Post, $"/api/admin/store/orders/{orderId}/resend", new ResendStoreOrderLetterRequest(kind), token);

    public Task<(StoreOrderDetailAdminRecord? Result, string? Error)> ReleaseStoreOrderAsync(Guid orderId, CancellationToken token = default)
        => DeskAsync(HttpMethod.Post, $"/api/admin/store/orders/{orderId}/release", null, token);

    public Task<(StoreOrderDetailAdminRecord? Result, string? Error)> RefundStoreOrderAsync(Guid orderId, StoreRefundRequest request, CancellationToken token = default)
        => DeskAsync(HttpMethod.Post, $"/api/admin/store/orders/{orderId}/refunds", request, token);

    public Task<(StoreOrderDetailAdminRecord? Result, string? Error)> RetryStoreRefundAsync(Guid orderId, Guid refundId, CancellationToken token = default)
        => DeskAsync(HttpMethod.Post, $"/api/admin/store/orders/{orderId}/refunds/{refundId}/retry", null, token);

    public Task<(StoreOrderDetailAdminRecord? Result, string? Error)> RetryStoreRefundTaxAsync(Guid orderId, Guid refundId, CancellationToken token = default)
        => DeskAsync(HttpMethod.Post, $"/api/admin/store/orders/{orderId}/refunds/{refundId}/retry-tax", null, token);
}
