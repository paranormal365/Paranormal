using Ben.Service.Models.Store;
using Ben.Web.Services.WebApi;

namespace Ben.Web.Services;

/// <summary>
/// A seller's own items (store sellers, backlog 251): the Selling workspace's only client. It can
/// reach nothing of the store's admin — the pages under Store/Selling inject this, never the
/// admin client, and a guard holds them to it.
/// </summary>
/// <remarks>
/// Every write answers with the item as the seller's editor reads it, or the server's sentence
/// saying why not — the admin client's convention.
/// </remarks>
public interface IBenStoreSellerClient
{
    Task<LoadResult<SellerProductListRecord>> GetMySellerProductsAsync(CancellationToken token = default);

    Task<ItemResult<SellerWorkspaceSummary>> GetSellerWorkspaceSummaryAsync(CancellationToken token = default);

    /// <summary>One of the caller's items' history — no store price lines, the staff as "The store".</summary>
    Task<LoadResult<StoreProductChangeRecord>> GetMySellerProductHistoryAsync(Guid productId, CancellationToken token = default);

    // ── the editor (P3) ──────────────────────────────────────────────────────

    Task<ItemResult<SellerItemRecord>> GetMySellerItemAsync(Guid productId, CancellationToken token = default);

    Task<LoadResult<SellerCategoryRecord>> GetSellerCategoriesAsync(CancellationToken token = default);

    Task<(SellerItemRecord? Result, string? Error)> CreateSellerItemAsync(CreateSellerItemRequest request, CancellationToken token = default);

    Task<(SellerItemRecord? Result, string? Error)> SaveSellerItemAsync(Guid productId, SaveSellerItemRequest request, CancellationToken token = default);

    /// <summary>A draft that has never been on sale; anything else is refused in words.</summary>
    Task<(bool Deleted, string? Error)> DeleteSellerItemAsync(Guid productId, CancellationToken token = default);

    Task<(SellerItemRecord? Result, string? Error)> RequestSellerSaleAsync(Guid productId, SellerSaleRequest request, CancellationToken token = default);

    Task<(SellerItemRecord? Result, string? Error)> WithdrawSellerSaleAsync(Guid productId, CancellationToken token = default);

    Task<(SellerItemRecord? Result, string? Error)> TakeSellerItemOffSaleAsync(Guid productId, CancellationToken token = default);

    Task<(SellerItemRecord? Result, string? Error)> SaveSellerItemOptionsAsync(
        Guid productId, SaveStoreOptionsRequest request, CancellationToken token = default);

    Task<(SellerItemRecord? Result, string? Error)> GenerateSellerVariantsAsync(Guid productId, CancellationToken token = default);

    Task<(SellerItemRecord? Result, string? Error)> CreateSellerVariantAsync(
        Guid productId, SaveSellerVariantRequest request, CancellationToken token = default);

    Task<(SellerItemRecord? Result, string? Error)> SaveSellerVariantAsync(
        Guid productId, Guid variantId, SaveSellerVariantRequest request, CancellationToken token = default);

    Task<(SellerItemRecord? Result, string? Error)> DeleteSellerVariantAsync(Guid productId, Guid variantId, CancellationToken token = default);

    Task<(SellerItemRecord? Result, string? Error)> AdjustSellerStockAsync(
        Guid productId, Guid variantId, AdjustStockRequest request, CancellationToken token = default);

    /// <summary>A multipart body: the picture in <c>file</c>, optionally <c>altText</c> and <c>variantId</c>.</summary>
    Task<(SellerItemRecord? Result, string? Error)> AddSellerItemImageAsync(
        Guid productId, MultipartFormDataContent content, CancellationToken token = default);

    Task<(SellerItemRecord? Result, string? Error)> ReorderSellerItemImagesAsync(
        Guid productId, ReorderRequest request, CancellationToken token = default);

    Task<(SellerItemRecord? Result, string? Error)> SaveSellerItemImageAsync(
        Guid productId, Guid imageId, UpdateStoreImageRequest request, CancellationToken token = default);

    Task<(SellerItemRecord? Result, string? Error)> DeleteSellerItemImageAsync(Guid productId, Guid imageId, CancellationToken token = default);

    // ── earnings (P10) ───────────────────────────────────────────────────────

    Task<ItemResult<SellerEarningsRecord>> GetMySellerEarningsAsync(CancellationToken token = default);

    // ── packages (P7) ────────────────────────────────────────────────────────

    Task<LoadResult<SellerParcelRecord>> GetMySellerParcelsAsync(bool openOnly = false, CancellationToken token = default);

    Task<ItemResult<SellerParcelRecord>> GetMySellerParcelAsync(Guid parcelId, CancellationToken token = default);

    Task<(SellerParcelRecord? Result, string? Error)> PackSellerParcelAsync(Guid parcelId, CancellationToken token = default);

    Task<(SellerParcelRecord? Result, string? Error)> ShipSellerParcelAsync(Guid parcelId, StoreShipmentInfo shipment, CancellationToken token = default);

    Task<(SellerParcelRecord? Result, string? Error)> CorrectSellerParcelTrackingAsync(Guid parcelId, StoreShipmentInfo shipment, CancellationToken token = default);

    Task<(SellerParcelRecord? Result, string? Error)> DeliverSellerParcelAsync(Guid parcelId, CancellationToken token = default);

    // ── parts and cost (P4) ──────────────────────────────────────────────────

    Task<ItemResult<StorePartsRecord>> GetSellerItemPartsAsync(Guid productId, CancellationToken token = default);

    Task<(StorePartsRecord? Result, string? Error)> SaveSellerItemPartsAsync(Guid productId, SaveStorePartsRequest request, CancellationToken token = default);

    Task<(StorePartsRecord? Result, string? Error)> SetSellerPartPictureAsync(Guid productId, Guid partId, MultipartFormDataContent content, CancellationToken token = default);

    Task<(StorePartsRecord? Result, string? Error)> RemoveSellerPartPictureAsync(Guid productId, Guid partId, CancellationToken token = default);

    // ── files (P11) ──────────────────────────────────────────────────────────

    Task<LoadResult<StoreProductFileRecord>> GetSellerItemFilesAsync(Guid productId, CancellationToken token = default);

    Task<(List<StoreProductFileRecord>? Result, string? Error)> AddSellerItemFileAsync(Guid productId, MultipartFormDataContent content, CancellationToken token = default);

    Task<(List<StoreProductFileRecord>? Result, string? Error)> AddSellerItemManualAsync(Guid productId, SaveStoreManualRequest request, CancellationToken token = default);

    Task<(List<StoreProductFileRecord>? Result, string? Error)> SaveSellerItemFileAsync(Guid productId, Guid fileId, SaveStoreProductFileRequest request, CancellationToken token = default);

    Task<(List<StoreProductFileRecord>? Result, string? Error)> DeleteSellerItemFileAsync(Guid productId, Guid fileId, CancellationToken token = default);

    // ── FAQ and questions (P12) ──────────────────────────────────────────────

    Task<ItemResult<StoreFaqsRecord>> GetSellerItemFaqsAsync(Guid productId, CancellationToken token = default);

    Task<(StoreFaqsRecord? Result, string? Error)> SaveSellerItemFaqsAsync(Guid productId, SaveStoreFaqsRequest request, CancellationToken token = default);

    Task<LoadResult<StoreReceivedQuestionRecord>> GetSellerQuestionsAsync(bool openOnly = false, CancellationToken token = default);

    Task<(StoreReceivedQuestionRecord? Result, string? Error)> AnswerSellerQuestionAsync(Guid questionId, AnswerStoreQuestionRequest request, CancellationToken token = default);

    Task<(StoreReceivedQuestionRecord? Result, string? Error)> PromoteSellerQuestionAsync(Guid questionId, PromoteStoreQuestionRequest request, CancellationToken token = default);

    // ── versions (P13) ─────────────────────────────────────────────────────

    Task<ItemResult<StoreVersionInfo>> GetSellerItemVersionAsync(Guid productId, CancellationToken token = default);

    Task<(StoreVersionInfo? Result, string? Error)> SaveSellerItemVersionAsync(Guid productId, SaveStoreVersionRequest request, CancellationToken token = default);

    Task<(StoreNewVersionRecord? Result, string? Error)> StartSellerItemVersionAsync(Guid productId, StartStoreVersionRequest request, CancellationToken token = default);

    // ── page extras (P14) ────────────────────────────────────────────────────

    Task<ItemResult<StoreExtrasRecord>> GetSellerItemExtrasAsync(Guid productId, CancellationToken token = default);

    Task<(StoreExtrasRecord? Result, string? Error)> SaveSellerItemExtrasAsync(Guid productId, SaveSellerExtrasRequest request, CancellationToken token = default);

    Task<(StoreExtrasRecord? Result, string? Error)> AddSellerItemVideoAsync(Guid productId, MultipartFormDataContent content, CancellationToken token = default);

    Task<(StoreExtrasRecord? Result, string? Error)> DeleteSellerItemVideoAsync(Guid productId, Guid videoId, CancellationToken token = default);
}
