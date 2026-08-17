using Ben.Data.Common.Enums;
using Ben.Service.Models.Entities;
using Ben.Web.Library.Services;

namespace Ben.Web.WebApp.Services.WebApi;

/// <summary>
/// The Equipment half of the adapter — implements <see cref="IBenEquipmentClient"/>.
/// </summary>
/// <remarks>
/// One partial class split across files by domain, matching the slices of IBenAdminClient.
/// The constructor and shared fields live in BenAdminClientAdapter.cs.
/// </remarks>
public sealed partial class BenAdminClientAdapter
{
    // ── Public catalog ────────────────────────────────────────────────────────

    public async Task<IReadOnlyList<EquipmentCategoryRecord>> GetEquipmentCategoriesAsync(CancellationToken token = default)
    {
        var result = await _api.GetAnonymousAsync<IReadOnlyList<EquipmentCategoryRecord>>("/api/equipment-catalog/categories", token);
        return result ?? [];
    }

    public async Task<IReadOnlyList<EquipmentBrandRecord>> GetEquipmentBrandsAsync(string? search = null, CancellationToken token = default)
    {
        var url = "/api/equipment-catalog/brands" + (string.IsNullOrWhiteSpace(search) ? "" : $"?search={Uri.EscapeDataString(search)}");
        var result = await _api.GetAsync<IReadOnlyList<EquipmentBrandRecord>>(url, token);
        return result ?? [];
    }

    public async Task<IReadOnlyList<EquipmentModelRecord>> GetEquipmentModelsForBrandAsync(Guid brandId, Guid? categoryId = null, CancellationToken token = default)
    {
        var url = $"/api/equipment-catalog/brands/{brandId}/models" + (categoryId is null ? "" : $"?categoryId={categoryId}");
        var result = await _api.GetAsync<IReadOnlyList<EquipmentModelRecord>>(url, token);
        return result ?? [];
    }

    public async Task<IReadOnlyList<EquipmentModelRecord>> SearchEquipmentModelsAsync(string? search = null, Guid? categoryId = null, CancellationToken token = default)
    {
        var query = new List<string>();
        if (!string.IsNullOrWhiteSpace(search)) query.Add($"search={Uri.EscapeDataString(search)}");
        if (categoryId is not null) query.Add($"categoryId={categoryId}");
        var url = "/api/equipment-catalog/models" + (query.Count == 0 ? "" : "?" + string.Join("&", query));
        var result = await _api.GetAsync<IReadOnlyList<EquipmentModelRecord>>(url, token);
        return result ?? [];
    }

    public async Task<IReadOnlyList<PublicEquipmentItemRecord>> GetPublicEquipmentItemsAsync(string? search = null, Guid? categoryId = null, CancellationToken token = default)
    {
        var query = new List<string>();
        if (!string.IsNullOrWhiteSpace(search)) query.Add($"search={Uri.EscapeDataString(search)}");
        if (categoryId is not null) query.Add($"categoryId={categoryId}");
        var url = "/api/equipment-catalog/items" + (query.Count == 0 ? "" : "?" + string.Join("&", query));
        var result = await _api.GetAnonymousAsync<IReadOnlyList<PublicEquipmentItemRecord>>(url, token);
        return result ?? [];
    }

    public Task<EquipmentBrandRecord?> ProposeEquipmentBrandAsync(string name, CancellationToken token = default)
        => _api.PostAsync<UpsertEquipmentBrandRequest, EquipmentBrandRecord>(
               "/api/equipment-catalog/brands", new UpsertEquipmentBrandRequest(name), token);

    public Task<EquipmentModelRecord?> ProposeEquipmentModelAsync(UpsertEquipmentModelRequest request, CancellationToken token = default)
        => _api.PostAsync<UpsertEquipmentModelRequest, EquipmentModelRecord>(
               "/api/equipment-catalog/models", request, token);

    // ── My equipment ──────────────────────────────────────────────────────────

    private const string MyEquipmentBase = "/api/me/equipment";

    public async Task<IReadOnlyList<EquipmentItemRecord>> GetMyEquipmentAsync(CancellationToken token = default)
    {
        var result = await _api.GetAsync<IReadOnlyList<EquipmentItemRecord>>(MyEquipmentBase, token);
        return result ?? [];
    }

    public Task<EquipmentItemRecord?> GetMyEquipmentItemAsync(Guid id, CancellationToken token = default)
        => _api.GetAsync<EquipmentItemRecord>($"{MyEquipmentBase}/{id}", token);

    public Task<EquipmentItemRecord?> CreateMyEquipmentItemAsync(UpsertEquipmentItemRequest request, CancellationToken token = default)
        => _api.PostAsync<UpsertEquipmentItemRequest, EquipmentItemRecord>(MyEquipmentBase, request, token);

    public Task<EquipmentItemRecord?> UpdateMyEquipmentItemAsync(Guid id, UpsertEquipmentItemRequest request, CancellationToken token = default)
        => _api.PutAsync<UpsertEquipmentItemRequest, EquipmentItemRecord>($"{MyEquipmentBase}/{id}", request, token);

    public Task<bool> DeleteMyEquipmentItemAsync(Guid id, CancellationToken token = default)
        => _api.DeleteAsync($"{MyEquipmentBase}/{id}", token);

    public Task<EquipmentItemPhotoRecord?> AttachMyEquipmentPhotoAsync(Guid id, MultipartFormDataContent content, CancellationToken token = default)
        => _api.PostMultipartAsync<EquipmentItemPhotoRecord>($"{MyEquipmentBase}/{id}/photos", content, token);

    public Task<bool> DetachMyEquipmentPhotoAsync(Guid id, Guid photoId, CancellationToken token = default)
        => _api.DeleteAsync($"{MyEquipmentBase}/{id}/photos/{photoId}", token);

    public Task<bool> SetMyEquipmentPrimaryPhotoAsync(Guid id, Guid photoId, CancellationToken token = default)
        => _api.PutVoidAsync<object>($"{MyEquipmentBase}/{id}/photos/{photoId}/primary", new { }, token);

    public Task<(byte[] Data, string ContentType, string FileName)?> GetEquipmentPhotoBytesAsync(Guid photoId, CancellationToken token = default)
        => _api.GetBytesAsync($"/api/equipment/photos/{photoId}/content", "photo", token);

    // ── Sharing with groups ───────────────────────────────────────────────────

    public async Task<IReadOnlyList<EquipmentShareOptionRecord>> GetMyEquipmentSharesAsync(Guid itemId, CancellationToken token = default)
    {
        var result = await _api.GetAsync<IReadOnlyList<EquipmentShareOptionRecord>>($"{MyEquipmentBase}/{itemId}/shares", token);
        return result ?? [];
    }

    public async Task<IReadOnlyList<EquipmentShareOptionRecord>> SetMyEquipmentSharesAsync(Guid itemId, IReadOnlyList<Guid> organizationIds, CancellationToken token = default)
    {
        var result = await _api.PutAsync<SetEquipmentSharesRequest, IReadOnlyList<EquipmentShareOptionRecord>>(
            $"{MyEquipmentBase}/{itemId}/shares", new SetEquipmentSharesRequest(organizationIds), token);
        return result ?? [];
    }

    public Task<BulkEquipmentShareResult?> BulkShareMyEquipmentAsync(Guid organizationId, bool share, CancellationToken token = default)
        => _api.PostAsync<BulkEquipmentShareRequest, BulkEquipmentShareResult>(
               $"{MyEquipmentBase}/shares/bulk", new BulkEquipmentShareRequest(organizationId, share), token);

    public async Task<IReadOnlyList<SharedEquipmentItemRecord>> GetOrgSharedEquipmentAsync(Guid orgId, CancellationToken token = default)
    {
        var result = await _api.GetAsync<IReadOnlyList<SharedEquipmentItemRecord>>($"/api/organizations/{orgId}/equipment/shared", token);
        return result ?? [];
    }

    // ── The group's own equipment ─────────────────────────────────────────────

    private static string OrgEquipBase(Guid orgId) => $"/api/organizations/{orgId}/equipment";

    public async Task<OrgEquipmentListRecord> GetOrgEquipmentAsync(Guid orgId, CancellationToken token = default)
    {
        var result = await _api.GetAsync<OrgEquipmentListRecord>(OrgEquipBase(orgId), token);
        // A swallowed non-2xx lands here as null. Default to "no permission", so a failed call can
        // never open an affordance — a permission gap should close, not open.
        return result ?? new OrgEquipmentListRecord(false, []);
    }

    public Task<EquipmentItemRecord?> CreateOrgEquipmentAsync(Guid orgId, UpsertOrgEquipmentItemRequest request, CancellationToken token = default)
        => _api.PostAsync<UpsertOrgEquipmentItemRequest, EquipmentItemRecord>(OrgEquipBase(orgId), request, token);

    public Task<EquipmentItemRecord?> UpdateOrgEquipmentAsync(Guid orgId, Guid itemId, UpsertOrgEquipmentItemRequest request, CancellationToken token = default)
        => _api.PutAsync<UpsertOrgEquipmentItemRequest, EquipmentItemRecord>($"{OrgEquipBase(orgId)}/{itemId}", request, token);

    public Task<bool> DeleteOrgEquipmentAsync(Guid orgId, Guid itemId, CancellationToken token = default)
        => _api.DeleteAsync($"{OrgEquipBase(orgId)}/{itemId}", token);

    public Task<EquipmentItemRecord?> SetOrgEquipmentHolderAsync(Guid orgId, Guid itemId, Guid? appUserId, CancellationToken token = default)
        => _api.PutAsync<SetEquipmentHolderRequest, EquipmentItemRecord>(
               $"{OrgEquipBase(orgId)}/{itemId}/holder", new SetEquipmentHolderRequest(appUserId), token);

    public async Task<IReadOnlyList<EquipmentServiceLogRecord>> GetOrgEquipmentServiceLogAsync(Guid orgId, Guid itemId, CancellationToken token = default)
    {
        var result = await _api.GetAsync<IReadOnlyList<EquipmentServiceLogRecord>>($"{OrgEquipBase(orgId)}/{itemId}/service-log", token);
        return result ?? [];
    }

    public Task<EquipmentItemPhotoRecord?> AttachOrgEquipmentPhotoAsync(Guid orgId, Guid itemId, MultipartFormDataContent content, CancellationToken token = default)
        => _api.PostMultipartAsync<EquipmentItemPhotoRecord>($"{OrgEquipBase(orgId)}/{itemId}/photos", content, token);

    public Task<bool> DetachOrgEquipmentPhotoAsync(Guid orgId, Guid itemId, Guid photoId, CancellationToken token = default)
        => _api.DeleteAsync($"{OrgEquipBase(orgId)}/{itemId}/photos/{photoId}", token);

    // PostVoid/PutVoid report the real outcome. Deserialising a 204 would return null and be
    // indistinguishable from a 403, which would have reported every refusal as a success.
    public Task<bool> SetOrgEquipmentPrimaryPhotoAsync(Guid orgId, Guid itemId, Guid photoId, CancellationToken token = default)
        => _api.PutVoidAsync($"{OrgEquipBase(orgId)}/{itemId}/photos/{photoId}/primary", new object(), token);

    public Task<bool> RetireMyEquipmentAsync(Guid itemId, bool retired, CancellationToken token = default)
        => _api.PostVoidAsync($"{MyEquipmentBase}/{itemId}/{(retired ? "retire" : "unretire")}", new object(), token);

    public Task<bool> RetireOrgEquipmentAsync(Guid orgId, Guid itemId, bool retired, CancellationToken token = default)
        => _api.PostVoidAsync($"{OrgEquipBase(orgId)}/{itemId}/{(retired ? "retire" : "unretire")}", new object(), token);

    public Task<(byte[] Data, string ContentType, string FileName)?> GetEquipmentPhotoThumbnailAsync(Guid photoId, CancellationToken token = default)
        => _api.GetBytesAsync($"/api/equipment/photos/{photoId}/thumbnail", "photo", token);

    public Task<UploadFileMetadataRecord?> GetEquipmentPhotoMetadataAsync(Guid photoId, CancellationToken token = default)
        => _api.GetAsync<UploadFileMetadataRecord>($"/api/equipment/photos/{photoId}/metadata", token);

    public Task<EquipmentServiceLogRecord?> AddOrgEquipmentServiceLogAsync(Guid orgId, Guid itemId, AddEquipmentServiceLogRequest request, CancellationToken token = default)
        => _api.PostAsync<AddEquipmentServiceLogRequest, EquipmentServiceLogRecord>(
               $"{OrgEquipBase(orgId)}/{itemId}/service-log", request, token);

    // ── Borrowing ─────────────────────────────────────────────────────────────

    private const string CheckoutBase = "/api/equipment-checkouts";

    public Task<BorrowEligibilityRecord?> GetBorrowEligibilityAsync(Guid itemId, CancellationToken token = default)
        => _api.GetAsync<BorrowEligibilityRecord>($"{CheckoutBase}/eligibility/{itemId}", token);

    public Task<EquipmentCheckoutRecord?> RequestEquipmentCheckoutAsync(RequestEquipmentCheckoutRequest request, CancellationToken token = default)
        => _api.PostAsync<RequestEquipmentCheckoutRequest, EquipmentCheckoutRecord>(CheckoutBase, request, token);

    public Task<EquipmentCheckoutRecord?> ApproveEquipmentCheckoutAsync(Guid checkoutId, DateTime? dateDue, string? reviewNotes, CancellationToken token = default)
        => _api.PostAsync<ApproveEquipmentCheckoutRequest, EquipmentCheckoutRecord>(
               $"{CheckoutBase}/{checkoutId}/approve", new ApproveEquipmentCheckoutRequest(dateDue, reviewNotes), token);

    public Task<EquipmentCheckoutRecord?> DenyEquipmentCheckoutAsync(Guid checkoutId, string reviewNotes, CancellationToken token = default)
        => _api.PostAsync<DenyEquipmentCheckoutRequest, EquipmentCheckoutRecord>(
               $"{CheckoutBase}/{checkoutId}/deny", new DenyEquipmentCheckoutRequest(reviewNotes), token);

    public Task<EquipmentCheckoutRecord?> CancelEquipmentCheckoutAsync(Guid checkoutId, CancellationToken token = default)
        => _api.PostAsync<object, EquipmentCheckoutRecord>($"{CheckoutBase}/{checkoutId}/cancel", new object(), token);

    public Task<EquipmentCheckoutRecord?> ConfirmEquipmentHandoffAsync(Guid checkoutId, CancellationToken token = default)
        => _api.PostAsync<object, EquipmentCheckoutRecord>($"{CheckoutBase}/{checkoutId}/confirm-handoff", new object(), token);

    public Task<EquipmentCheckoutRecord?> ReturnEquipmentCheckoutAsync(Guid checkoutId, string? conditionNotes, CancellationToken token = default)
        => _api.PostAsync<ReturnEquipmentCheckoutRequest, EquipmentCheckoutRecord>(
               $"{CheckoutBase}/{checkoutId}/return", new ReturnEquipmentCheckoutRequest(conditionNotes), token);

    public async Task<IReadOnlyList<EquipmentCheckoutRecord>> GetMyEquipmentCheckoutsAsync(string role = "borrower", CancellationToken token = default)
    {
        var result = await _api.GetAsync<IReadOnlyList<EquipmentCheckoutRecord>>($"/api/me/equipment-checkouts?role={role}", token);
        return result ?? [];
    }

    public async Task<OrgCheckoutListRecord> GetOrgEquipmentCheckoutsAsync(Guid orgId, CancellationToken token = default)
    {
        var result = await _api.GetAsync<OrgCheckoutListRecord>($"/api/organizations/{orgId}/equipment-checkouts", token);
        // A swallowed 404 means no permission — default to the closed answer.
        return result ?? new OrgCheckoutListRecord(false, []);
    }

    public async Task<IReadOnlyList<EquipmentCheckoutRecord>> GetEquipmentItemCheckoutsAsync(Guid itemId, CancellationToken token = default)
    {
        var result = await _api.GetAsync<IReadOnlyList<EquipmentCheckoutRecord>>($"/api/equipment/{itemId}/checkouts", token);
        return result ?? [];
    }

    // ── Condition photos, renewals, history ───────────────────────────────────

    public async Task<IReadOnlyList<EquipmentCheckoutPhotoRecord>> GetCheckoutPhotosAsync(Guid checkoutId, CancellationToken token = default)
    {
        var result = await _api.GetAsync<IReadOnlyList<EquipmentCheckoutPhotoRecord>>($"{CheckoutBase}/{checkoutId}/photos", token);
        return result ?? [];
    }

    public Task<bool> DeleteCheckoutPhotoAsync(Guid checkoutId, Guid photoId, CancellationToken token = default)
        => _api.DeleteAsync($"{CheckoutBase}/{checkoutId}/photos/{photoId}", token);

    public Task<EquipmentCheckoutPhotoRecord?> UploadCheckoutPhotoAsync(
        Guid checkoutId, EquipmentPhotoStage stage, MultipartFormDataContent content, CancellationToken token = default)
        => _api.PostMultipartAsync<EquipmentCheckoutPhotoRecord>(
               $"{CheckoutBase}/{checkoutId}/photos?stage={stage}", content, token);

    public Task<(byte[] Data, string ContentType, string FileName)?> GetCheckoutPhotoBytesAsync(Guid photoId, CancellationToken token = default)
        => _api.GetBytesAsync($"{CheckoutBase}/photos/{photoId}/content", "photo", token);

    public async Task<IReadOnlyList<EquipmentCheckoutRenewalRecord>> GetCheckoutRenewalsAsync(Guid checkoutId, CancellationToken token = default)
    {
        var result = await _api.GetAsync<IReadOnlyList<EquipmentCheckoutRenewalRecord>>($"{CheckoutBase}/{checkoutId}/renewals", token);
        return result ?? [];
    }

    public Task<EquipmentCheckoutRenewalRecord?> RequestCheckoutRenewalAsync(Guid checkoutId, DateTime requestedDateDue, string? notes, CancellationToken token = default)
        => _api.PostAsync<RequestEquipmentRenewalRequest, EquipmentCheckoutRenewalRecord>(
               $"{CheckoutBase}/{checkoutId}/renewals", new RequestEquipmentRenewalRequest(requestedDateDue, notes), token);

    public Task<EquipmentCheckoutRenewalRecord?> ReviewCheckoutRenewalAsync(Guid checkoutId, Guid renewalId, bool approve, string? reviewNotes, CancellationToken token = default)
        => _api.PostAsync<ReviewEquipmentRenewalRequest, EquipmentCheckoutRenewalRecord>(
               $"{CheckoutBase}/{checkoutId}/renewals/{renewalId}/review", new ReviewEquipmentRenewalRequest(approve, reviewNotes), token);

    public async Task<IReadOnlyList<EquipmentHistoryEntryRecord>> GetEquipmentItemHistoryAsync(Guid itemId, CancellationToken token = default)
    {
        var result = await _api.GetAsync<IReadOnlyList<EquipmentHistoryEntryRecord>>($"/api/equipment/{itemId}/history", token);
        return result ?? [];
    }

    // ── SuperAdmin taxonomy moderation ───────────────────────────────────────

    private const string AdminTaxonomyBase = "/api/admin/equipment-taxonomy";

    public async Task<IReadOnlyList<EquipmentCategoryRecord>> GetAdminEquipmentCategoriesAsync(CancellationToken token = default)
    {
        var result = await _api.GetAsync<IReadOnlyList<EquipmentCategoryRecord>>($"{AdminTaxonomyBase}/categories", token);
        return result ?? [];
    }

    public Task<EquipmentCategoryRecord?> CreateEquipmentCategoryAsync(UpsertEquipmentCategoryRequest request, CancellationToken token = default)
        => _api.PostAsync<UpsertEquipmentCategoryRequest, EquipmentCategoryRecord>($"{AdminTaxonomyBase}/categories", request, token);

    public Task<EquipmentCategoryRecord?> UpdateEquipmentCategoryAsync(Guid id, UpsertEquipmentCategoryRequest request, CancellationToken token = default)
        => _api.PutAsync<UpsertEquipmentCategoryRequest, EquipmentCategoryRecord>($"{AdminTaxonomyBase}/categories/{id}", request, token);

    public Task<bool> DeleteEquipmentCategoryAsync(Guid id, CancellationToken token = default)
        => _api.DeleteAsync($"{AdminTaxonomyBase}/categories/{id}", token);

    public async Task<IReadOnlyList<EquipmentBrandRecord>> GetAdminEquipmentBrandsAsync(CancellationToken token = default)
    {
        var result = await _api.GetAsync<IReadOnlyList<EquipmentBrandRecord>>($"{AdminTaxonomyBase}/brands", token);
        return result ?? [];
    }

    public Task<EquipmentBrandRecord?> ApproveEquipmentBrandAsync(Guid id, CancellationToken token = default)
        => _api.PutAsync<object, EquipmentBrandRecord>($"{AdminTaxonomyBase}/brands/{id}/approve", new { }, token);

    public Task<bool> RejectEquipmentBrandAsync(Guid id, CancellationToken token = default)
        => _api.DeleteAsync($"{AdminTaxonomyBase}/brands/{id}", token);

    public async Task<IReadOnlyList<EquipmentModelRecord>> GetAdminEquipmentModelsAsync(Guid? brandId = null, CancellationToken token = default)
    {
        var url = $"{AdminTaxonomyBase}/models" + (brandId is null ? "" : $"?brandId={brandId}");
        var result = await _api.GetAsync<IReadOnlyList<EquipmentModelRecord>>(url, token);
        return result ?? [];
    }

    public Task<EquipmentModelRecord?> ApproveEquipmentModelAsync(Guid id, CancellationToken token = default)
        => _api.PutAsync<object, EquipmentModelRecord>($"{AdminTaxonomyBase}/models/{id}/approve", new { }, token);

    public Task<bool> RejectEquipmentModelAsync(Guid id, CancellationToken token = default)
        => _api.DeleteAsync($"{AdminTaxonomyBase}/models/{id}", token);
}
