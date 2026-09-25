using Ben.Data.Common.Enums;
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

    /// <summary>Everybody who can be named as an item's seller: the holders of the Seller role.</summary>
    Task<LoadResult<StoreSellerRecord>> GetStoreSellersAsync(CancellationToken token = default);

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

    /// <summary>An item's history, newest first, every line with names (store sellers P2).</summary>
    Task<LoadResult<StoreProductChangeRecord>> GetStoreProductHistoryAsync(Guid productId, CancellationToken token = default);

    Task<ItemResult<StorePartsRecord>> GetStoreProductPartsAsync(Guid productId, CancellationToken token = default);

    Task<(StorePartsRecord? Result, string? Error)> SaveStoreProductPartsAsync(Guid productId, SaveStorePartsRequest request, CancellationToken token = default);

    Task<(StorePartsRecord? Result, string? Error)> SetStorePartPictureAsync(Guid productId, Guid partId, MultipartFormDataContent content, CancellationToken token = default);

    Task<(StorePartsRecord? Result, string? Error)> RemoveStorePartPictureAsync(Guid productId, Guid partId, CancellationToken token = default);

    /// <summary>Sellers' requests to go on sale: the ones waiting, oldest first, or with <paramref name="decided"/> the answered ones.</summary>
    Task<LoadResult<StoreSaleRequestRecord>> GetStoreSaleRequestsAsync(bool decided = false, CancellationToken token = default);

    /// <summary>Puts the item on sale at the store's prices and fixes the seller's asking price onto it.</summary>
    Task<(StoreSaleRequestRecord? Result, string? Error)> ApproveStoreSaleRequestAsync(Guid requestId, CancellationToken token = default);

    Task<(StoreSaleRequestRecord? Result, string? Error)> DeclineStoreSaleRequestAsync(
        Guid requestId, DeclineSaleRequest request, CancellationToken token = default);

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

    // ── discount codes ───────────────────────────────────────────────────────

    Task<LoadResult<StoreCouponAdminRecord>> GetStoreCouponsAsync(CancellationToken token = default);

    Task<(StoreCouponAdminRecord? Result, string? Error)> CreateStoreCouponAsync(
        SaveStoreCouponRequest request, CancellationToken token = default);

    Task<(StoreCouponAdminRecord? Result, string? Error)> SaveStoreCouponAsync(
        Guid couponId, SaveStoreCouponRequest request, CancellationToken token = default);

    /// <summary>Refused once the code has been used — retire it instead.</summary>
    Task<(bool Deleted, string? Error)> DeleteStoreCouponAsync(Guid couponId, CancellationToken token = default);

    // ── reviews ──────────────────────────────────────────────────────────────

    Task<LoadResult<StoreReviewAdminRecord>> GetStoreReviewsAsync(
        StoreReviewStatus? status = null, string? search = null, Guid? productId = null, CancellationToken token = default);

    Task<(StoreReviewAdminRecord? Result, string? Error)> ApproveStoreReviewAsync(Guid reviewId, CancellationToken token = default);

    /// <summary>Refuses a review; the reason is shown to the reviewer.</summary>
    Task<(StoreReviewAdminRecord? Result, string? Error)> RejectStoreReviewAsync(
        Guid reviewId, RejectStoreReviewRequest request, CancellationToken token = default);

    Task<(StoreReviewAdminRecord? Result, string? Error)> ReplyToStoreReviewAsync(
        Guid reviewId, ReplyToStoreReviewRequest request, CancellationToken token = default);

    Task<(bool Deleted, string? Error)> DeleteStoreReviewAsync(Guid reviewId, CancellationToken token = default);

    // ── settings and dashboard ───────────────────────────────────────────────

    /// <summary>The settings and the ready-to-sell checklist (which asks Stripe, so allow a moment).</summary>
    Task<ItemResult<StoreSettingsAdminRecord>> GetStoreSettingsAsync(CancellationToken token = default);

    /// <summary>Saves the whole form or none of it; the refusal names the one value that is wrong.</summary>
    Task<(StoreSettingsAdminRecord? Result, string? Error)> SaveStoreSettingsAsync(
        SaveStoreSettingsRequest request, CancellationToken token = default);

    Task<ItemResult<StoreDashboardRecord>> GetStoreDashboardAsync(int days = 30, CancellationToken token = default);

    // ── The order desk (storefront S5.5) ──────────────────────────────────────
    // Every action answers with the order as it now stands, or the refusal in words.

    Task<LoadResult<StoreOrderListRecord>> GetStoreOrdersAsync(StoreOrderListQuery query, CancellationToken token = default);

    Task<ItemResult<StoreOrderDetailAdminRecord>> GetStoreOrderAdminAsync(Guid orderId, CancellationToken token = default);

    Task<ItemResult<StoreInvoiceRecord>> GetStoreOrderInvoiceAdminAsync(Guid orderId, CancellationToken token = default);

    Task<LoadResult<StoreRefundRecord>> GetStoreOrderRefundsAsync(Guid orderId, CancellationToken token = default);

    Task<(byte[] Data, string FileName)?> DownloadStoreOrdersCsvAsync(StoreOrderListQuery query, CancellationToken token = default);

    Task<(byte[] Data, string FileName)?> DownloadStoreRefundsCsvAsync(DateTime? from = null, DateTime? to = null, CancellationToken token = default);

    Task<(StoreOrderDetailAdminRecord? Result, string? Error)> PackStoreParcelAsync(Guid orderId, Guid parcelId, CancellationToken token = default);

    Task<(StoreOrderDetailAdminRecord? Result, string? Error)> ShipStoreParcelAsync(Guid orderId, Guid parcelId, StoreShipmentInfo shipment, CancellationToken token = default);

    Task<(StoreOrderDetailAdminRecord? Result, string? Error)> CorrectStoreParcelTrackingAsync(Guid orderId, Guid parcelId, StoreShipmentInfo shipment, CancellationToken token = default);

    Task<(StoreOrderDetailAdminRecord? Result, string? Error)> DeliverStoreParcelAsync(Guid orderId, Guid parcelId, CancellationToken token = default);

    Task<(StoreOrderDetailAdminRecord? Result, string? Error)> CancelStoreOrderAsync(Guid orderId, CancelStoreOrderRequest request, CancellationToken token = default);

    Task<(StoreOrderDetailAdminRecord? Result, string? Error)> AddStoreOrderNoteAsync(Guid orderId, string note, CancellationToken token = default);

    Task<(StoreOrderDetailAdminRecord? Result, string? Error)> ChangeStoreOrderAddressAsync(Guid orderId, ChangeStoreOrderAddressRequest request, CancellationToken token = default);

    Task<(StoreOrderDetailAdminRecord? Result, string? Error)> ClearStoreOrderAttentionAsync(Guid orderId, CancellationToken token = default);

    Task<(StoreOrderDetailAdminRecord? Result, string? Error)> ResendStoreOrderLetterAsync(Guid orderId, string kind, CancellationToken token = default);

    Task<(StoreOrderDetailAdminRecord? Result, string? Error)> ReleaseStoreOrderAsync(Guid orderId, CancellationToken token = default);

    Task<(StoreOrderDetailAdminRecord? Result, string? Error)> RefundStoreOrderAsync(Guid orderId, StoreRefundRequest request, CancellationToken token = default);

    Task<(StoreOrderDetailAdminRecord? Result, string? Error)> RetryStoreRefundAsync(Guid orderId, Guid refundId, CancellationToken token = default);

    Task<(StoreOrderDetailAdminRecord? Result, string? Error)> RetryStoreRefundTaxAsync(Guid orderId, Guid refundId, CancellationToken token = default);
}
