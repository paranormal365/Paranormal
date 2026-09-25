using Ben.Service.Models.Store;

namespace Ben.Web.Services.WebApi;

// Store sellers (backlog 251): the seller's own items.
public sealed partial class BenAdminClientAdapter
{
    public Task<LoadResult<SellerProductListRecord>> GetMySellerProductsAsync(CancellationToken token = default)
        => _api.GetListAsync<SellerProductListRecord>("/api/seller/store/products", token);

    public Task<ItemResult<SellerWorkspaceSummary>> GetSellerWorkspaceSummaryAsync(CancellationToken token = default)
        => _api.GetItemAsync<SellerWorkspaceSummary>("/api/seller/store/products/summary", token);

    public Task<LoadResult<StoreProductChangeRecord>> GetMySellerProductHistoryAsync(Guid productId, CancellationToken token = default)
        => _api.GetListAsync<StoreProductChangeRecord>($"/api/seller/store/products/{productId}/history", token);
}
