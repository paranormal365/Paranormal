using Ben.Service.Models.Entities;

namespace Ben.Web.Library.Services;

/// <summary>
/// The Equipment slice of <see cref="IBenAdminClient"/> — backlog item #55: personal and (later
/// phases) org-owned equipment, the public catalog, and the loan/checkout workflow.
/// </summary>
/// <remarks>
/// Part of the same domain-slice split as <see cref="IBenInvestigationClient"/> and the rest —
/// <see cref="IBenAdminClient"/> inherits every slice, so the single adapter and existing callers
/// are unaffected by a new one appearing.
/// </remarks>
public interface IBenEquipmentClient
{
    // ── Public catalog (Phase 1) ─────────────────────────────────────────────

    Task<IReadOnlyList<EquipmentCategoryRecord>> GetEquipmentCategoriesAsync(CancellationToken token = default);
    Task<IReadOnlyList<EquipmentBrandRecord>> GetEquipmentBrandsAsync(string? search = null, CancellationToken token = default);
    Task<IReadOnlyList<EquipmentModelRecord>> GetEquipmentModelsForBrandAsync(Guid brandId, Guid? categoryId = null, CancellationToken token = default);
    Task<IReadOnlyList<EquipmentModelRecord>> SearchEquipmentModelsAsync(string? search = null, Guid? categoryId = null, CancellationToken token = default);
    /// <summary>Items their owners chose to list publicly. Carries no owner identity and no serial.</summary>
    Task<IReadOnlyList<PublicEquipmentItemRecord>> GetPublicEquipmentItemsAsync(string? search = null, Guid? categoryId = null, CancellationToken token = default);

    Task<EquipmentBrandRecord?> ProposeEquipmentBrandAsync(string name, CancellationToken token = default);
    Task<EquipmentModelRecord?> ProposeEquipmentModelAsync(UpsertEquipmentModelRequest request, CancellationToken token = default);

    // ── My equipment (Phase 1) ───────────────────────────────────────────────

    Task<IReadOnlyList<EquipmentItemRecord>> GetMyEquipmentAsync(CancellationToken token = default);
    Task<EquipmentItemRecord?> GetMyEquipmentItemAsync(Guid id, CancellationToken token = default);
    Task<EquipmentItemRecord?> CreateMyEquipmentItemAsync(UpsertEquipmentItemRequest request, CancellationToken token = default);
    Task<EquipmentItemRecord?> UpdateMyEquipmentItemAsync(Guid id, UpsertEquipmentItemRequest request, CancellationToken token = default);
    Task<bool> DeleteMyEquipmentItemAsync(Guid id, CancellationToken token = default);
    Task<EquipmentItemPhotoRecord?> AttachMyEquipmentPhotoAsync(Guid id, MultipartFormDataContent content, CancellationToken token = default);
    Task<bool> DetachMyEquipmentPhotoAsync(Guid id, Guid photoId, CancellationToken token = default);
    Task<bool> SetMyEquipmentPrimaryPhotoAsync(Guid id, Guid photoId, CancellationToken token = default);

    /// <summary>Fetches an equipment photo's raw bytes for data:-URI rendering — never a plain &lt;img src&gt;.</summary>
    Task<(byte[] Data, string ContentType, string FileName)?> GetEquipmentPhotoBytesAsync(Guid photoId, CancellationToken token = default);

    // ── Sharing with groups (Phase 2) ────────────────────────────────────────

    /// <summary>The caller's groups, each flagged with whether this item is shared with it.</summary>
    Task<IReadOnlyList<EquipmentShareOptionRecord>> GetMyEquipmentSharesAsync(Guid itemId, CancellationToken token = default);

    /// <summary>Replaces the item's shares wholesale; groups omitted are unshared.</summary>
    Task<IReadOnlyList<EquipmentShareOptionRecord>> SetMyEquipmentSharesAsync(Guid itemId, IReadOnlyList<Guid> organizationIds, CancellationToken token = default);

    /// <summary>Shares or unshares every one of the caller's non-retired items with one group.</summary>
    Task<BulkEquipmentShareResult?> BulkShareMyEquipmentAsync(Guid organizationId, bool share, CancellationToken token = default);

    /// <summary>Members' personal gear shared with this group. Never carries a serial number.</summary>
    Task<IReadOnlyList<SharedEquipmentItemRecord>> GetOrgSharedEquipmentAsync(Guid orgId, CancellationToken token = default);

    // ── The group's own equipment (Phase 3) ──────────────────────────────────

    /// <summary>
    /// The group's own gear, plus whether the caller may add to it. Serials appear only for callers
    /// who may manage equipment; the CanManage verdict comes from the server, never from whether
    /// this call happened to succeed.
    /// </summary>
    Task<OrgEquipmentListRecord> GetOrgEquipmentAsync(Guid orgId, CancellationToken token = default);

    Task<EquipmentItemRecord?> CreateOrgEquipmentAsync(Guid orgId, UpsertOrgEquipmentItemRequest request, CancellationToken token = default);
    Task<EquipmentItemRecord?> UpdateOrgEquipmentAsync(Guid orgId, Guid itemId, UpsertOrgEquipmentItemRequest request, CancellationToken token = default);
    Task<bool> DeleteOrgEquipmentAsync(Guid orgId, Guid itemId, CancellationToken token = default);

    /// <summary>Records who currently holds a piece, or clears it with a null user id.</summary>
    Task<EquipmentItemRecord?> SetOrgEquipmentHolderAsync(Guid orgId, Guid itemId, Guid? appUserId, CancellationToken token = default);

    Task<IReadOnlyList<EquipmentServiceLogRecord>> GetOrgEquipmentServiceLogAsync(Guid orgId, Guid itemId, CancellationToken token = default);

    // ── Borrowing (Phase 4) ──────────────────────────────────────────────────

    /// <summary>Whether the caller may ask to borrow an item, and on whose behalf they could.</summary>
    Task<BorrowEligibilityRecord?> GetBorrowEligibilityAsync(Guid itemId, CancellationToken token = default);

    Task<EquipmentCheckoutRecord?> RequestEquipmentCheckoutAsync(RequestEquipmentCheckoutRequest request, CancellationToken token = default);
    Task<EquipmentCheckoutRecord?> ApproveEquipmentCheckoutAsync(Guid checkoutId, DateTime? dateDue, string? reviewNotes, CancellationToken token = default);
    Task<EquipmentCheckoutRecord?> DenyEquipmentCheckoutAsync(Guid checkoutId, string reviewNotes, CancellationToken token = default);
    Task<EquipmentCheckoutRecord?> CancelEquipmentCheckoutAsync(Guid checkoutId, CancellationToken token = default);
    Task<EquipmentCheckoutRecord?> ConfirmEquipmentHandoffAsync(Guid checkoutId, CancellationToken token = default);
    Task<EquipmentCheckoutRecord?> ReturnEquipmentCheckoutAsync(Guid checkoutId, string? conditionNotes, CancellationToken token = default);

    /// <summary>The caller's loans. <paramref name="role"/> is "borrower" or "approver".</summary>
    Task<IReadOnlyList<EquipmentCheckoutRecord>> GetMyEquipmentCheckoutsAsync(string role = "borrower", CancellationToken token = default);

    Task<IReadOnlyList<EquipmentCheckoutRecord>> GetOrgEquipmentCheckoutsAsync(Guid orgId, CancellationToken token = default);
    Task<IReadOnlyList<EquipmentCheckoutRecord>> GetEquipmentItemCheckoutsAsync(Guid itemId, CancellationToken token = default);
    Task<EquipmentServiceLogRecord?> AddOrgEquipmentServiceLogAsync(Guid orgId, Guid itemId, AddEquipmentServiceLogRequest request, CancellationToken token = default);

    // ── SuperAdmin taxonomy moderation (Phase 1) ─────────────────────────────

    Task<IReadOnlyList<EquipmentCategoryRecord>> GetAdminEquipmentCategoriesAsync(CancellationToken token = default);
    Task<EquipmentCategoryRecord?> CreateEquipmentCategoryAsync(UpsertEquipmentCategoryRequest request, CancellationToken token = default);
    Task<EquipmentCategoryRecord?> UpdateEquipmentCategoryAsync(Guid id, UpsertEquipmentCategoryRequest request, CancellationToken token = default);
    Task<bool> DeleteEquipmentCategoryAsync(Guid id, CancellationToken token = default);
    Task<IReadOnlyList<EquipmentBrandRecord>> GetAdminEquipmentBrandsAsync(CancellationToken token = default);
    Task<EquipmentBrandRecord?> ApproveEquipmentBrandAsync(Guid id, CancellationToken token = default);
    Task<bool> RejectEquipmentBrandAsync(Guid id, CancellationToken token = default);
    Task<IReadOnlyList<EquipmentModelRecord>> GetAdminEquipmentModelsAsync(Guid? brandId = null, CancellationToken token = default);
    Task<EquipmentModelRecord?> ApproveEquipmentModelAsync(Guid id, CancellationToken token = default);
    Task<bool> RejectEquipmentModelAsync(Guid id, CancellationToken token = default);
}
