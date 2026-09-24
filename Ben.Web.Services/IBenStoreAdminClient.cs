using Ben.Service.Models.Store;
using Ben.Web.Services.WebApi;

namespace Ben.Web.Services;

/// <summary>
/// The store's back office: categories, products, stock, discount codes, reviews and settings
/// (storefront S1). SuperAdmin only, and never behind the store switch — the catalogue is entered
/// while the shop is dark.
/// </summary>
/// <remarks>
/// Writes return the server's sentence, because every refusal here is something an admin has to
/// act on — "Recorders still holds 2 products. Move them to another category first."
/// </remarks>
public interface IBenStoreAdminClient
{
    // ── categories ───────────────────────────────────────────────────────────

    Task<LoadResult<StoreCategoryAdminRecord>> GetStoreCategoriesAsync(CancellationToken token = default);

    Task<(StoreCategoryAdminRecord? Result, string? Error)> CreateStoreCategoryAsync(
        SaveStoreCategoryRequest request, CancellationToken token = default);

    /// <summary>Saves a category; the result says how many live products a hide took off the store.</summary>
    Task<(StoreCategorySaveResult? Result, string? Error)> SaveStoreCategoryAsync(
        Guid categoryId, SaveStoreCategoryRequest request, CancellationToken token = default);

    Task<(List<StoreCategoryAdminRecord>? Result, string? Error)> ReorderStoreCategoriesAsync(
        ReorderRequest request, CancellationToken token = default);

    /// <summary>A multipart body with the picture in a part named <c>file</c>.</summary>
    Task<(StoreCategoryAdminRecord? Result, string? Error)> SetStoreCategoryImageAsync(
        Guid categoryId, MultipartFormDataContent content, CancellationToken token = default);

    Task<(StoreCategoryAdminRecord? Result, string? Error)> RemoveStoreCategoryImageAsync(
        Guid categoryId, CancellationToken token = default);

    Task<(bool Deleted, string? Error)> DeleteStoreCategoryAsync(Guid categoryId, CancellationToken token = default);
}
