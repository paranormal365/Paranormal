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
}
