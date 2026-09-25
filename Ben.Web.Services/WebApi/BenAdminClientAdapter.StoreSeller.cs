using Ben.Service.Models.Store;

namespace Ben.Web.Services.WebApi;

// Store sellers (backlog 251): the seller's own items. Every route is a literal so the route
// guards can see who calls it.
public sealed partial class BenAdminClientAdapter
{
    public Task<LoadResult<SellerProductListRecord>> GetMySellerProductsAsync(CancellationToken token = default)
        => _api.GetListAsync<SellerProductListRecord>("/api/seller/store/products", token);

    public Task<ItemResult<SellerWorkspaceSummary>> GetSellerWorkspaceSummaryAsync(CancellationToken token = default)
        => _api.GetItemAsync<SellerWorkspaceSummary>("/api/seller/store/products/summary", token);

    public Task<LoadResult<StoreProductChangeRecord>> GetMySellerProductHistoryAsync(Guid productId, CancellationToken token = default)
        => _api.GetListAsync<StoreProductChangeRecord>($"/api/seller/store/products/{productId}/history", token);

    // ── the editor (P3) ──────────────────────────────────────────────────────

    public Task<ItemResult<SellerItemRecord>> GetMySellerItemAsync(Guid productId, CancellationToken token = default)
        => _api.GetItemAsync<SellerItemRecord>($"/api/seller/store/products/{productId}", token);

    public Task<LoadResult<SellerCategoryRecord>> GetSellerCategoriesAsync(CancellationToken token = default)
        => _api.GetListAsync<SellerCategoryRecord>("/api/seller/store/products/categories", token);

    public Task<(SellerItemRecord? Result, string? Error)> CreateSellerItemAsync(CreateSellerItemRequest request, CancellationToken token = default)
        => _api.SendExpectingReasonAsync<CreateSellerItemRequest, SellerItemRecord>(
               HttpMethod.Post, "/api/seller/store/products", request, token);

    public Task<(SellerItemRecord? Result, string? Error)> SaveSellerItemAsync(
        Guid productId, SaveSellerItemRequest request, CancellationToken token = default)
        => _api.SendExpectingReasonAsync<SaveSellerItemRequest, SellerItemRecord>(
               HttpMethod.Put, $"/api/seller/store/products/{productId}", request, token);

    public Task<(bool Deleted, string? Error)> DeleteSellerItemAsync(Guid productId, CancellationToken token = default)
        => _api.DeleteExpectingReasonAsync($"/api/seller/store/products/{productId}", token);

    public Task<(SellerItemRecord? Result, string? Error)> RequestSellerSaleAsync(
        Guid productId, SellerSaleRequest request, CancellationToken token = default)
        => _api.SendExpectingReasonAsync<SellerSaleRequest, SellerItemRecord>(
               HttpMethod.Post, $"/api/seller/store/products/{productId}/sale-request", request, token);

    public Task<(SellerItemRecord? Result, string? Error)> WithdrawSellerSaleAsync(Guid productId, CancellationToken token = default)
        => _api.SendExpectingReasonAsync<object, SellerItemRecord>(
               HttpMethod.Delete, $"/api/seller/store/products/{productId}/sale-request", new { }, token);

    public Task<(SellerItemRecord? Result, string? Error)> TakeSellerItemOffSaleAsync(Guid productId, CancellationToken token = default)
        => _api.SendExpectingReasonAsync<object, SellerItemRecord>(
               HttpMethod.Post, $"/api/seller/store/products/{productId}/off-sale", new { }, token);

    public Task<(SellerItemRecord? Result, string? Error)> SaveSellerItemOptionsAsync(
        Guid productId, SaveStoreOptionsRequest request, CancellationToken token = default)
        => _api.SendExpectingReasonAsync<SaveStoreOptionsRequest, SellerItemRecord>(
               HttpMethod.Put, $"/api/seller/store/products/{productId}/options", request, token);

    public Task<(SellerItemRecord? Result, string? Error)> GenerateSellerVariantsAsync(Guid productId, CancellationToken token = default)
        => _api.SendExpectingReasonAsync<object, SellerItemRecord>(
               HttpMethod.Post, $"/api/seller/store/products/{productId}/variants/generate", new { }, token);

    public Task<(SellerItemRecord? Result, string? Error)> CreateSellerVariantAsync(
        Guid productId, SaveSellerVariantRequest request, CancellationToken token = default)
        => _api.SendExpectingReasonAsync<SaveSellerVariantRequest, SellerItemRecord>(
               HttpMethod.Post, $"/api/seller/store/products/{productId}/variants", request, token);

    public Task<(SellerItemRecord? Result, string? Error)> SaveSellerVariantAsync(
        Guid productId, Guid variantId, SaveSellerVariantRequest request, CancellationToken token = default)
        => _api.SendExpectingReasonAsync<SaveSellerVariantRequest, SellerItemRecord>(
               HttpMethod.Put, $"/api/seller/store/products/{productId}/variants/{variantId}", request, token);

    public Task<(SellerItemRecord? Result, string? Error)> DeleteSellerVariantAsync(
        Guid productId, Guid variantId, CancellationToken token = default)
        => _api.SendExpectingReasonAsync<object, SellerItemRecord>(
               HttpMethod.Delete, $"/api/seller/store/products/{productId}/variants/{variantId}", new { }, token);

    public Task<(SellerItemRecord? Result, string? Error)> AdjustSellerStockAsync(
        Guid productId, Guid variantId, AdjustStockRequest request, CancellationToken token = default)
        => _api.SendExpectingReasonAsync<AdjustStockRequest, SellerItemRecord>(
               HttpMethod.Post, $"/api/seller/store/products/{productId}/variants/{variantId}/stock", request, token);

    public Task<(SellerItemRecord? Result, string? Error)> AddSellerItemImageAsync(
        Guid productId, MultipartFormDataContent content, CancellationToken token = default)
        => _api.PostMultipartExpectingReasonAsync<SellerItemRecord>($"/api/seller/store/products/{productId}/images", content, token);

    public Task<(SellerItemRecord? Result, string? Error)> ReorderSellerItemImagesAsync(
        Guid productId, ReorderRequest request, CancellationToken token = default)
        => _api.SendExpectingReasonAsync<ReorderRequest, SellerItemRecord>(
               HttpMethod.Put, $"/api/seller/store/products/{productId}/images/reorder", request, token);

    public Task<(SellerItemRecord? Result, string? Error)> SaveSellerItemImageAsync(
        Guid productId, Guid imageId, UpdateStoreImageRequest request, CancellationToken token = default)
        => _api.SendExpectingReasonAsync<UpdateStoreImageRequest, SellerItemRecord>(
               HttpMethod.Put, $"/api/seller/store/products/{productId}/images/{imageId}", request, token);

    public Task<ItemResult<StorePartsRecord>> GetSellerItemPartsAsync(Guid productId, CancellationToken token = default)
        => _api.GetItemAsync<StorePartsRecord>($"/api/seller/store/products/{productId}/parts", token);

    public Task<(StorePartsRecord? Result, string? Error)> SaveSellerItemPartsAsync(
        Guid productId, SaveStorePartsRequest request, CancellationToken token = default)
        => _api.SendExpectingReasonAsync<SaveStorePartsRequest, StorePartsRecord>(
               HttpMethod.Put, $"/api/seller/store/products/{productId}/parts", request, token);

    public Task<(StorePartsRecord? Result, string? Error)> SetSellerPartPictureAsync(
        Guid productId, Guid partId, MultipartFormDataContent content, CancellationToken token = default)
        => _api.PostMultipartExpectingReasonAsync<StorePartsRecord>($"/api/seller/store/products/{productId}/parts/{partId}/picture", content, token);

    public Task<(StorePartsRecord? Result, string? Error)> RemoveSellerPartPictureAsync(Guid productId, Guid partId, CancellationToken token = default)
        => _api.SendExpectingReasonAsync<object, StorePartsRecord>(
               HttpMethod.Delete, $"/api/seller/store/products/{productId}/parts/{partId}/picture", new { }, token);

    public Task<(SellerItemRecord? Result, string? Error)> DeleteSellerItemImageAsync(
        Guid productId, Guid imageId, CancellationToken token = default)
        => _api.SendExpectingReasonAsync<object, SellerItemRecord>(
               HttpMethod.Delete, $"/api/seller/store/products/{productId}/images/{imageId}", new { }, token);
}
