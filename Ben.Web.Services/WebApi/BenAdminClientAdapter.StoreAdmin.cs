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
}
