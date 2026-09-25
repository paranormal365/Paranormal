using Ben.Service.Models.Store;
using Ben.Web.Services.WebApi;

namespace Ben.Web.Services;

/// <summary>
/// A seller's own items (store sellers, backlog 251): the Selling workspace's only client. It can
/// reach nothing of the store's admin — the pages under Store/Selling inject this, never the
/// admin client, and a guard holds them to it.
/// </summary>
/// <remarks>Writes, when they arrive (P3), return the server's sentence, like the admin client.</remarks>
public interface IBenStoreSellerClient
{
    Task<LoadResult<SellerProductListRecord>> GetMySellerProductsAsync(CancellationToken token = default);

    Task<ItemResult<SellerWorkspaceSummary>> GetSellerWorkspaceSummaryAsync(CancellationToken token = default);

    /// <summary>One of the caller's items' history — no store price lines, the staff as "The store".</summary>
    Task<LoadResult<StoreProductChangeRecord>> GetMySellerProductHistoryAsync(Guid productId, CancellationToken token = default);
}
