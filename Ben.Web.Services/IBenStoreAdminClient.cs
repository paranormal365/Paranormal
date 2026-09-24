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

    // ── products ─────────────────────────────────────────────────────────────

    Task<LoadResult<StoreProductListAdminRecord>> GetStoreProductsAsync(
        string? search = null, Guid? categoryId = null, bool? active = null, CancellationToken token = default);

    Task<ItemResult<StoreProductAdminRecord>> GetStoreProductAsync(Guid productId, CancellationToken token = default);

    Task<(StoreProductAdminRecord? Result, string? Error)> CreateStoreProductAsync(
        CreateStoreProductRequest request, CancellationToken token = default);

    Task<(StoreProductAdminRecord? Result, string? Error)> SaveStoreProductAsync(
        Guid productId, SaveStoreProductRequest request, CancellationToken token = default);

    /// <summary>Puts a product on sale; refused with what is missing — a price, a picture, a live variant.</summary>
    Task<(StoreProductAdminRecord? Result, string? Error)> ActivateStoreProductAsync(Guid productId, CancellationToken token = default);

    Task<(StoreProductAdminRecord? Result, string? Error)> DeactivateStoreProductAsync(Guid productId, CancellationToken token = default);

    Task<(StoreProductAdminRecord? Result, string? Error)> DuplicateStoreProductAsync(Guid productId, CancellationToken token = default);

    Task<(bool Deleted, string? Error)> DeleteStoreProductAsync(Guid productId, CancellationToken token = default);

    Task<(StoreProductAdminRecord? Result, string? Error)> SaveStoreProductOptionsAsync(
        Guid productId, SaveStoreOptionsRequest request, CancellationToken token = default);

    Task<(StoreVariantsGenerated? Result, string? Error)> GenerateStoreVariantsAsync(Guid productId, CancellationToken token = default);

    Task<(StoreProductAdminRecord? Result, string? Error)> CreateStoreVariantAsync(
        Guid productId, SaveStoreVariantRequest request, CancellationToken token = default);

    Task<(StoreProductAdminRecord? Result, string? Error)> SaveStoreVariantAsync(
        Guid productId, Guid variantId, SaveStoreVariantRequest request, CancellationToken token = default);

    Task<(StoreProductAdminRecord? Result, string? Error)> DeleteStoreVariantAsync(
        Guid productId, Guid variantId, CancellationToken token = default);

    Task<(StoreProductAdminRecord? Result, string? Error)> AdjustStoreVariantStockAsync(
        Guid productId, Guid variantId, AdjustStockRequest request, CancellationToken token = default);

    Task<LoadResult<StoreStockMovementRecord>> GetStoreVariantStockLogAsync(
        Guid productId, Guid variantId, CancellationToken token = default);

    /// <summary>A multipart body: the picture in <c>file</c>, optionally <c>altText</c> and <c>variantId</c>.</summary>
    Task<(StoreProductAdminRecord? Result, string? Error)> AddStoreProductImageAsync(
        Guid productId, MultipartFormDataContent content, CancellationToken token = default);

    Task<(StoreProductAdminRecord? Result, string? Error)> ReorderStoreProductImagesAsync(
        Guid productId, ReorderRequest request, CancellationToken token = default);

    Task<(StoreProductAdminRecord? Result, string? Error)> SaveStoreProductImageAsync(
        Guid productId, Guid imageId, UpdateStoreImageRequest request, CancellationToken token = default);

    Task<(StoreProductAdminRecord? Result, string? Error)> DeleteStoreProductImageAsync(
        Guid productId, Guid imageId, CancellationToken token = default);

    // ── stock ────────────────────────────────────────────────────────────────

    Task<LoadResult<StoreStockRow>> GetStoreStockAsync(string? search = null, bool lowOnly = false, CancellationToken token = default);

    /// <summary>Several variants at once, all or nothing; the refusal names the SKU that stopped it.</summary>
    Task<(StoreStockAdjusted? Result, string? Error)> AdjustStoreStockAsync(
        BulkAdjustStockRequest request, CancellationToken token = default);

    /// <summary>A multipart body with a <c>Sku,Delta</c> CSV in <c>file</c>.</summary>
    Task<(StoreStockAdjusted? Result, string? Error)> ImportStoreStockAsync(
        MultipartFormDataContent content, CancellationToken token = default);

    /// <summary>Every active variant's stock as a CSV file, fetched with the admin's token.</summary>
    Task<(byte[] Data, string FileName)?> DownloadStoreStockCsvAsync(CancellationToken token = default);
}
