using Ben.Data.Common.Enums;
using Ben.Service.Models.Entities;
using Ben.Web.Services;

namespace Ben.Web.Services.WebApi;

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

    public Task<LoadResult<EquipmentCategoryRecord>> GetEquipmentCategoriesAsync(CancellationToken token = default)
        => _api.GetAnonymousListAsync<EquipmentCategoryRecord>("/api/equipment-catalog/categories", token);

    public Task<LoadResult<EquipmentBrandRecord>> GetEquipmentBrandsAsync(string? search = null, CancellationToken token = default)
    {        var url = "/api/equipment-catalog/brands" + (string.IsNullOrWhiteSpace(search) ? "" : $"?search={Uri.EscapeDataString(search)}");
        return _api.GetListAsync<EquipmentBrandRecord>(url, token);
    }

    public Task<LoadResult<EquipmentModelRecord>> GetEquipmentModelsForBrandAsync(Guid brandId, Guid? categoryId = null, CancellationToken token = default)
    {        var url = $"/api/equipment-catalog/brands/{brandId}/models" + (categoryId is null ? "" : $"?categoryId={categoryId}");
        return _api.GetListAsync<EquipmentModelRecord>(url, token);
    }

    public Task<LoadResult<EquipmentModelRecord>> SearchEquipmentModelsAsync(string? search = null, Guid? categoryId = null, CancellationToken token = default)
    {        var query = new List<string>();
        if (!string.IsNullOrWhiteSpace(search)) query.Add($"search={Uri.EscapeDataString(search)}");
        if (categoryId is not null) query.Add($"categoryId={categoryId}");
        var url = "/api/equipment-catalog/models" + (query.Count == 0 ? "" : "?" + string.Join("&", query));
        return _api.GetListAsync<EquipmentModelRecord>(url, token);
    }

    public Task<LoadResult<PublicEquipmentItemRecord>> GetPublicEquipmentItemsAsync(string? search = null, Guid? categoryId = null, CancellationToken token = default)
    {        var query = new List<string>();
        if (!string.IsNullOrWhiteSpace(search)) query.Add($"search={Uri.EscapeDataString(search)}");
        if (categoryId is not null) query.Add($"categoryId={categoryId}");
        var url = "/api/equipment-catalog/items" + (query.Count == 0 ? "" : "?" + string.Join("&", query));
        return _api.GetAnonymousListAsync<PublicEquipmentItemRecord>(url, token);
    }

    public Task<EquipmentModelPageRecord?> GetEquipmentModelPageAsync(Guid modelId, CancellationToken token = default)
        => _api.GetAnonymousAsync<EquipmentModelPageRecord>($"/api/equipment-catalog/models/{modelId}", token);

    public Task<EquipmentModelPageRecord?> GetEquipmentModelPageBySlugAsync(
        string brandSlug, string modelSlug, CancellationToken token = default)
        => _api.GetAsync<EquipmentModelPageRecord>(
               $"/api/equipment-catalog/makes/{Uri.EscapeDataString(brandSlug)}/{Uri.EscapeDataString(modelSlug)}", token);

    public Task<EquipmentItemDetailRecord?> GetEquipmentItemAsync(Guid itemId, CancellationToken token = default)
        => _api.GetAnonymousAsync<EquipmentItemDetailRecord>($"/api/equipment/items/{itemId}", token);

    // Counters must never cost the reader anything: failures are swallowed, and the caller does not
    // await these before doing the thing the user actually asked for.
    public Task RecordEquipmentViewAsync(Guid itemId, CancellationToken token = default)
        => _api.PostAnonymousVoidAsync($"/api/equipment-catalog/items/{itemId}/viewed", new object(), token);

    public Task RecordEquipmentLinkClickAsync(Guid itemId, CancellationToken token = default)
        => _api.PostAnonymousVoidAsync($"/api/equipment-catalog/items/{itemId}/link-clicked", new object(), token);

    public Task<bool> SetPhotoCatalogExclusionAsync(Guid itemId, Guid photoId, bool exclude, Guid? orgId = null, CancellationToken token = default)
        => _api.PutVoidAsync(
               orgId is Guid o
                   ? $"{OrgEquipBase(o)}/{itemId}/photos/{photoId}/catalog-exclusion"
                   : $"{MyEquipmentBase}/{itemId}/photos/{photoId}/catalog-exclusion",
               new SetPhotoCatalogExclusionRequest(exclude), token);

    // ── FAQs and anonymous questions (Phase 6c) ─────────────────────────────

    public Task<LoadResult<EquipmentFaqRecord>> GetEquipmentFaqsAsync(Guid itemId, CancellationToken token = default)
    {        // Anonymous: the FAQ of a publicly-listed piece is readable by a passer-by, and the server
        // decides that from the item, not from whether a token arrived.
        return _api.GetAnonymousListAsync<EquipmentFaqRecord>($"/api/equipment/items/{itemId}/faqs", token);
    }

    public Task<EquipmentFaqRecord?> AddEquipmentFaqAsync(Guid itemId, UpsertEquipmentFaqRequest request, CancellationToken token = default)
        => _api.PostAsync<UpsertEquipmentFaqRequest, EquipmentFaqRecord>(
               $"/api/equipment/items/{itemId}/faqs", request, token);

    public Task<EquipmentFaqRecord?> UpdateEquipmentFaqAsync(Guid itemId, Guid faqId, UpsertEquipmentFaqRequest request, CancellationToken token = default)
        => _api.PutAsync<UpsertEquipmentFaqRequest, EquipmentFaqRecord>(
               $"/api/equipment/items/{itemId}/faqs/{faqId}", request, token);

    public Task<bool> DeleteEquipmentFaqAsync(Guid itemId, Guid faqId, CancellationToken token = default)
        => _api.DeleteAsync($"/api/equipment/items/{itemId}/faqs/{faqId}", token);

    public Task<AskedQuestionRecord?> AskEquipmentQuestionAsync(Guid itemId, string questionText, CancellationToken token = default)
        => _api.PostAsync<AskEquipmentQuestionRequest, AskedQuestionRecord>(
               $"/api/equipment/items/{itemId}/questions", new AskEquipmentQuestionRequest(questionText), token);

    public Task<LoadResult<AskedQuestionRecord>> GetMyAskedQuestionsAsync(CancellationToken token = default)
        => _api.GetListAsync<AskedQuestionRecord>("/api/me/equipment-questions/asked", token);

    public Task<LoadResult<ReceivedQuestionRecord>> GetMyReceivedQuestionsAsync(CancellationToken token = default)
        => _api.GetListAsync<ReceivedQuestionRecord>("/api/me/equipment-questions/received", token);

    public Task<ReceivedQuestionRecord?> AnswerEquipmentQuestionAsync(Guid questionId, AnswerEquipmentQuestionRequest request, CancellationToken token = default)
        => _api.PutAsync<AnswerEquipmentQuestionRequest, ReceivedQuestionRecord>(
               $"/api/me/equipment-questions/{questionId}/answer", request, token);

    public Task<EquipmentFaqRecord?> PromoteQuestionToFaqAsync(Guid questionId, PromoteQuestionToFaqRequest request, CancellationToken token = default)
        => _api.PostAsync<PromoteQuestionToFaqRequest, EquipmentFaqRecord>(
               $"/api/me/equipment-questions/{questionId}/promote-to-faq", request, token);

    // ── Mutual loan feedback (Phase 6d) ─────────────────────────────────────

    public Task<bool> SubmitLoanFeedbackAsync(Guid checkoutId, SubmitLoanFeedbackRequest request, CancellationToken token = default)
        => _api.PostVoidAsync($"/api/equipment/checkouts/{checkoutId}/feedback", request, token);

    public Task<LoanFeedbackStateRecord?> GetLoanFeedbackStateAsync(Guid checkoutId, CancellationToken token = default)
        => _api.GetAsync<LoanFeedbackStateRecord>($"/api/equipment/checkouts/{checkoutId}/feedback-state", token);

    public Task<BorrowerFeedbackPanelRecord?> GetBorrowerFeedbackAsync(Guid checkoutId, CancellationToken token = default)
        => _api.GetAsync<BorrowerFeedbackPanelRecord>($"/api/equipment/checkouts/{checkoutId}/borrower-feedback", token);

    public Task<LenderFeedbackPanelRecord?> GetLenderFeedbackAsync(Guid itemId, CancellationToken token = default)
        => _api.GetAsync<LenderFeedbackPanelRecord>($"/api/equipment/items/{itemId}/lender-feedback", token);

    public Task<LoadResult<ProductReviewRecord>> GetProductReviewsAsync(Guid modelId, CancellationToken token = default)
        => _api.GetAnonymousListAsync<ProductReviewRecord>($"/api/equipment-catalog/models/{modelId}/reviews", token);

    // Not coalesced to an empty list: a 404 here means "not yours to moderate", and the page says
    // something different in that case than it does for a group with no feedback yet.
    public Task<IReadOnlyList<ModeratedFeedbackRecord>?> GetEquipmentFeedbackForModerationAsync(Guid orgId, CancellationToken token = default)
        => _api.GetAsync<IReadOnlyList<ModeratedFeedbackRecord>>(
               $"/api/organizations/{orgId}/equipment-feedback", token);

    public Task<bool> DeleteEquipmentFeedbackAsync(Guid orgId, Guid feedbackId, CancellationToken token = default)
        => _api.DeleteAsync($"/api/organizations/{orgId}/equipment-feedback/{feedbackId}", token);

    public async Task<TaxonomyProposal<EquipmentBrandRecord>> ProposeEquipmentBrandAsync(
        string name, bool confirmDistinct = false, CancellationToken token = default)
    {
        var (created, conflict) = await _api
            .PostExpectingConflictAsync<UpsertEquipmentBrandRequest, EquipmentBrandRecord, ProbableDuplicateResponse>(
                "/api/equipment-catalog/brands",
                new UpsertEquipmentBrandRequest(name, confirmDistinct), token);

        return new TaxonomyProposal<EquipmentBrandRecord>(created, conflict);
    }

    public Task<EquipmentModelRecord?> ProposeEquipmentModelAsync(UpsertEquipmentModelRequest request, CancellationToken token = default)
        => _api.PostAsync<UpsertEquipmentModelRequest, EquipmentModelRecord>(
               "/api/equipment-catalog/models", request, token);

    // ── My equipment ──────────────────────────────────────────────────────────

    private const string MyEquipmentBase = "/api/me/equipment";

    public Task<LoadResult<EquipmentItemRecord>> GetMyEquipmentAsync(CancellationToken token = default)
        => _api.GetListAsync<EquipmentItemRecord>(MyEquipmentBase, token);

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

    public Task<LoadResult<EquipmentShareOptionRecord>> GetMyEquipmentSharesAsync(Guid itemId, CancellationToken token = default)
        => _api.GetListAsync<EquipmentShareOptionRecord>($"{MyEquipmentBase}/{itemId}/shares", token);

    /// <summary>
    /// Saves which organizations an item is shared with, and says so when it did not save.
    /// </summary>
    /// <remarks>
    /// <para>A <b>save</b>, not a load, so it does not return <see cref="LoadResult{T}"/> — the
    /// question is "did this happen?", not "is this list real?". But it had the same defect: a
    /// refused PUT became <c>null</c> and then <c>?? []</c>, so the caller was handed an empty
    /// share list as though the item had successfully been shared with nobody, and
    /// <c>EquipmentShareEditor</c> closed its dialog reporting success.</para>
    ///
    /// <para><c>SendExpectingReasonAsync</c> rather than a bare bool: sharing is refused for
    /// reasons a person can act on — not a member of that group any more, item withdrawn — and
    /// "Save failed" is not one of them.</para>
    /// </remarks>
    public async Task<(IReadOnlyList<EquipmentShareOptionRecord> Shares, string? Error)> SetMyEquipmentSharesAsync(Guid itemId, IReadOnlyList<Guid> organizationIds, CancellationToken token = default)
    {
        var (result, error) = await _api.SendExpectingReasonAsync<SetEquipmentSharesRequest, IReadOnlyList<EquipmentShareOptionRecord>>(
            HttpMethod.Put, $"{MyEquipmentBase}/{itemId}/shares", new SetEquipmentSharesRequest(organizationIds), token);

        // Explicit rather than `result ?? []`: an empty list is returned only alongside a reason,
        // so there is no path on which the caller receives "shared with nobody" as a fact.
        if (result is null)
            return ([], error ?? "The sharing change could not be saved.");

        return (result, null);
    }

    public Task<BulkEquipmentShareResult?> BulkShareMyEquipmentAsync(Guid organizationId, bool share, CancellationToken token = default)
        => _api.PostAsync<BulkEquipmentShareRequest, BulkEquipmentShareResult>(
               $"{MyEquipmentBase}/shares/bulk", new BulkEquipmentShareRequest(organizationId, share), token);

    public Task<LoadResult<SharedEquipmentItemRecord>> GetOrgSharedEquipmentAsync(Guid orgId, CancellationToken token = default)
        => _api.GetListAsync<SharedEquipmentItemRecord>($"/api/organizations/{orgId}/equipment/shared", token);

    // ── The group's own equipment ─────────────────────────────────────────────

    private static string OrgEquipBase(Guid orgId) => $"/api/organizations/{orgId}/equipment";

    public async Task<OrgEquipmentListRecord> GetOrgEquipmentAsync(Guid orgId, CancellationToken token = default)
    {
        var result = await _api.GetAsync<OrgEquipmentListRecord>(OrgEquipBase(orgId), token);
        // A swallowed non-2xx lands here as null. Default to "no permission", so a failed call can
        // never open an affordance — a permission gap should close, not open.
        return result ?? new OrgEquipmentListRecord(false, []);
    }

    public Task<(EquipmentItemRecord? Result, string? Error)> CreateOrgEquipmentAsync(Guid orgId, UpsertOrgEquipmentItemRequest request, CancellationToken token = default)
        => _api.SendExpectingReasonAsync<UpsertOrgEquipmentItemRequest, EquipmentItemRecord>(
               HttpMethod.Post, OrgEquipBase(orgId), request, token);

    public Task<EquipmentItemRecord?> UpdateOrgEquipmentAsync(Guid orgId, Guid itemId, UpsertOrgEquipmentItemRequest request, CancellationToken token = default)
        => _api.PutAsync<UpsertOrgEquipmentItemRequest, EquipmentItemRecord>($"{OrgEquipBase(orgId)}/{itemId}", request, token);

    public Task<bool> DeleteOrgEquipmentAsync(Guid orgId, Guid itemId, CancellationToken token = default)
        => _api.DeleteAsync($"{OrgEquipBase(orgId)}/{itemId}", token);

    public Task<EquipmentItemRecord?> SetOrgEquipmentHolderAsync(Guid orgId, Guid itemId, Guid? appUserId, CancellationToken token = default)
        => _api.PutAsync<SetEquipmentHolderRequest, EquipmentItemRecord>(
               $"{OrgEquipBase(orgId)}/{itemId}/holder", new SetEquipmentHolderRequest(appUserId), token);

    public Task<LoadResult<EquipmentServiceLogRecord>> GetOrgEquipmentServiceLogAsync(Guid orgId, Guid itemId, CancellationToken token = default)
        => _api.GetListAsync<EquipmentServiceLogRecord>($"{OrgEquipBase(orgId)}/{itemId}/service-log", token);

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

    // Reason-carrying, which also rescues a sentence that was being silently dropped: the
    // "already promised to someone else" guard on approval never reached a screen through the
    // old PostAsync — the sixth instance of the server-guard-needs-a-UI-path lesson.
    public Task<(EquipmentCheckoutRecord? Result, string? Error)> ApproveEquipmentCheckoutAsync(Guid checkoutId, DateTime? dateDue, string? reviewNotes, CancellationToken token = default)
        => _api.SendExpectingReasonAsync<ApproveEquipmentCheckoutRequest, EquipmentCheckoutRecord>(
               HttpMethod.Post, $"{CheckoutBase}/{checkoutId}/approve", new ApproveEquipmentCheckoutRequest(dateDue, reviewNotes), token);

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

    public Task<LoadResult<EquipmentCheckoutRecord>> GetMyEquipmentCheckoutsAsync(string role = "borrower", CancellationToken token = default)
        => _api.GetListAsync<EquipmentCheckoutRecord>($"/api/me/equipment-checkouts?role={role}", token);

    public async Task<OrgCheckoutListRecord> GetOrgEquipmentCheckoutsAsync(Guid orgId, CancellationToken token = default)
    {
        var result = await _api.GetAsync<OrgCheckoutListRecord>($"/api/organizations/{orgId}/equipment-checkouts", token);
        // A swallowed 404 means no permission — default to the closed answer.
        return result ?? new OrgCheckoutListRecord(false, []);
    }

    public Task<LoadResult<EquipmentCheckoutRecord>> GetEquipmentItemCheckoutsAsync(Guid itemId, CancellationToken token = default)
        => _api.GetListAsync<EquipmentCheckoutRecord>($"/api/equipment/{itemId}/checkouts", token);

    // ── Condition photos, renewals, history ───────────────────────────────────

    public Task<LoadResult<EquipmentCheckoutPhotoRecord>> GetCheckoutPhotosAsync(Guid checkoutId, CancellationToken token = default)
        => _api.GetListAsync<EquipmentCheckoutPhotoRecord>($"{CheckoutBase}/{checkoutId}/photos", token);

    public Task<bool> DeleteCheckoutPhotoAsync(Guid checkoutId, Guid photoId, CancellationToken token = default)
        => _api.DeleteAsync($"{CheckoutBase}/{checkoutId}/photos/{photoId}", token);

    public Task<EquipmentCheckoutPhotoRecord?> UploadCheckoutPhotoAsync(
        Guid checkoutId, EquipmentPhotoStage stage, MultipartFormDataContent content, CancellationToken token = default)
        => _api.PostMultipartAsync<EquipmentCheckoutPhotoRecord>(
               $"{CheckoutBase}/{checkoutId}/photos?stage={stage}", content, token);

    public Task<(byte[] Data, string ContentType, string FileName)?> GetCheckoutPhotoBytesAsync(Guid photoId, CancellationToken token = default)
        => _api.GetBytesAsync($"{CheckoutBase}/photos/{photoId}/content", "photo", token);

    public Task<LoadResult<EquipmentCheckoutRenewalRecord>> GetCheckoutRenewalsAsync(Guid checkoutId, CancellationToken token = default)
        => _api.GetListAsync<EquipmentCheckoutRenewalRecord>($"{CheckoutBase}/{checkoutId}/renewals", token);

    public Task<EquipmentCheckoutRenewalRecord?> RequestCheckoutRenewalAsync(Guid checkoutId, DateTime requestedDateDue, string? notes, CancellationToken token = default)
        => _api.PostAsync<RequestEquipmentRenewalRequest, EquipmentCheckoutRenewalRecord>(
               $"{CheckoutBase}/{checkoutId}/renewals", new RequestEquipmentRenewalRequest(requestedDateDue, notes), token);

    public Task<EquipmentCheckoutRenewalRecord?> ReviewCheckoutRenewalAsync(Guid checkoutId, Guid renewalId, bool approve, string? reviewNotes, CancellationToken token = default)
        => _api.PostAsync<ReviewEquipmentRenewalRequest, EquipmentCheckoutRenewalRecord>(
               $"{CheckoutBase}/{checkoutId}/renewals/{renewalId}/review", new ReviewEquipmentRenewalRequest(approve, reviewNotes), token);

    public Task<LoadResult<EquipmentHistoryEntryRecord>> GetEquipmentItemHistoryAsync(Guid itemId, CancellationToken token = default)
        => _api.GetListAsync<EquipmentHistoryEntryRecord>($"/api/equipment/{itemId}/history", token);

    // ── SuperAdmin taxonomy moderation ───────────────────────────────────────

    private const string AdminTaxonomyBase = "/api/admin/equipment-taxonomy";

    public Task<LoadResult<EquipmentCategoryRecord>> GetAdminEquipmentCategoriesAsync(CancellationToken token = default)
        => _api.GetListAsync<EquipmentCategoryRecord>($"{AdminTaxonomyBase}/categories", token);

    public Task<EquipmentCategoryRecord?> CreateEquipmentCategoryAsync(UpsertEquipmentCategoryRequest request, CancellationToken token = default)
        => _api.PostAsync<UpsertEquipmentCategoryRequest, EquipmentCategoryRecord>($"{AdminTaxonomyBase}/categories", request, token);

    public Task<EquipmentCategoryRecord?> UpdateEquipmentCategoryAsync(Guid id, UpsertEquipmentCategoryRequest request, CancellationToken token = default)
        => _api.PutAsync<UpsertEquipmentCategoryRequest, EquipmentCategoryRecord>($"{AdminTaxonomyBase}/categories/{id}", request, token);

    public Task<bool> DeleteEquipmentCategoryAsync(Guid id, CancellationToken token = default)
        => _api.DeleteAsync($"{AdminTaxonomyBase}/categories/{id}", token);

    public Task<LoadResult<EquipmentBrandRecord>> GetAdminEquipmentBrandsAsync(CancellationToken token = default)
        => _api.GetListAsync<EquipmentBrandRecord>($"{AdminTaxonomyBase}/brands", token);

    public Task<EquipmentBrandRecord?> ApproveEquipmentBrandAsync(Guid id, CancellationToken token = default)
        => _api.PutAsync<object, EquipmentBrandRecord>($"{AdminTaxonomyBase}/brands/{id}/approve", new { }, token);

    public Task<bool> RejectEquipmentBrandAsync(Guid id, CancellationToken token = default)
        => _api.DeleteAsync($"{AdminTaxonomyBase}/brands/{id}", token);

    public Task<LoadResult<EquipmentModelRecord>> GetAdminEquipmentModelsAsync(Guid? brandId = null, CancellationToken token = default)
    {        var url = $"{AdminTaxonomyBase}/models" + (brandId is null ? "" : $"?brandId={brandId}");
        return _api.GetListAsync<EquipmentModelRecord>(url, token);
    }

    public Task<EquipmentModelRecord?> ApproveEquipmentModelAsync(Guid id, CancellationToken token = default)
        => _api.PutAsync<object, EquipmentModelRecord>($"{AdminTaxonomyBase}/models/{id}/approve", new { }, token);

    public Task<bool> RejectEquipmentModelAsync(Guid id, CancellationToken token = default)
        => _api.DeleteAsync($"{AdminTaxonomyBase}/models/{id}", token);
}
