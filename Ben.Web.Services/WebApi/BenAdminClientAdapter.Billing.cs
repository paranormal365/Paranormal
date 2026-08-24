using Ben.Service.Models;
using Ben.Service.Models.Admin;

namespace Ben.Web.Services.WebApi;

/// <summary>The billing slice — see <see cref="IBenBillingClient"/> for the contract.</summary>
public sealed partial class BenAdminClientAdapter
{
    public Task<LoadResult<SubscriptionTierAdminRecord>> GetSubscriptionTiersAsync(CancellationToken token = default)
        => _api.GetListAsync<SubscriptionTierAdminRecord>("/api/admin/subscription-tiers", token);

    /// <summary>Replaces a tier's included permission areas (item 156 Phase A).</summary>
    public async Task<(SubscriptionTierAdminRecord? Result, string? Error)> SetTierPermissionAreasAsync(
        Guid tierId, IReadOnlyList<Ben.Data.Common.Enums.OrganizationPermissionArea> areas,
        CancellationToken token = default)
        => await _api.SendExpectingReasonAsync<SetTierPermissionAreasRequest, SubscriptionTierAdminRecord>(
            HttpMethod.Put, $"/api/admin/subscription-tiers/{tierId}/permission-areas",
            new SetTierPermissionAreasRequest(areas), token);

    public async Task<(SubscriptionTierAdminRecord? Result, string? Error)> SetTierCapabilitiesAsync(
        Guid tierId, IReadOnlyList<Ben.Data.Common.Enums.TierCapability> capabilities,
        CancellationToken token = default)
        => await _api.SendExpectingReasonAsync<SetTierCapabilitiesRequest, SubscriptionTierAdminRecord>(
            HttpMethod.Put, $"/api/admin/subscription-tiers/{tierId}/capabilities",
            new SetTierCapabilitiesRequest(capabilities), token);

    public Task<string?> GetTierValidationAsync(CancellationToken token = default)
        => _api.GetAsync<string?>("/api/admin/subscription-tiers/validation", token);

    public Task<(SubscriptionTierAdminRecord? Result, string? Error)> CreateSubscriptionTierAsync(
        SaveSubscriptionTierRequest request, CancellationToken token = default)
        => _api.SendExpectingReasonAsync<SaveSubscriptionTierRequest, SubscriptionTierAdminRecord>(
            HttpMethod.Post, "/api/admin/subscription-tiers", request, token);

    public Task<(SubscriptionTierAdminRecord? Result, string? Error)> UpdateSubscriptionTierAsync(
        Guid id, SaveSubscriptionTierRequest request, CancellationToken token = default)
        => _api.SendExpectingReasonAsync<SaveSubscriptionTierRequest, SubscriptionTierAdminRecord>(
            HttpMethod.Put, $"/api/admin/subscription-tiers/{id}", request, token);

    public Task<TierImpactRecord?> PreviewTierImpactAsync(
        Guid id, SaveSubscriptionTierRequest request, CancellationToken token = default)
        => _api.PostAsync<SaveSubscriptionTierRequest, TierImpactRecord>(
            $"/api/admin/subscription-tiers/{id}/impact", request, token);

    public Task<LoadResult<CouponAdminRecord>> GetCouponsAsync(CancellationToken token = default)
        => _api.GetListAsync<CouponAdminRecord>("/api/admin/coupons", token);

    public Task<LoadResult<CouponCodeAdminRecord>> GetCouponCodesAsync(Guid couponId, CancellationToken token = default)
        => _api.GetListAsync<CouponCodeAdminRecord>($"/api/admin/coupons/{couponId}/codes", token);

    public Task<LoadResult<CouponRedemptionAdminRecord>> GetCouponRedemptionsAsync(Guid couponId, CancellationToken token = default)
        => _api.GetListAsync<CouponRedemptionAdminRecord>($"/api/admin/coupons/{couponId}/redemptions", token);

    public Task<(CouponAdminRecord? Result, string? Error)> CreateCouponAsync(
        SaveCouponRequest request, CancellationToken token = default)
        => _api.SendExpectingReasonAsync<SaveCouponRequest, CouponAdminRecord>(
            HttpMethod.Post, "/api/admin/coupons", request, token);

    public Task<(CouponAdminRecord? Result, string? Error)> UpdateCouponAsync(
        Guid id, SaveCouponRequest request, CancellationToken token = default)
        => _api.SendExpectingReasonAsync<SaveCouponRequest, CouponAdminRecord>(
            HttpMethod.Put, $"/api/admin/coupons/{id}", request, token);

    public Task<(IReadOnlyList<CouponCodeAdminRecord>? Result, string? Error)> GenerateCouponCodesAsync(
        Guid couponId, GenerateCouponCodesRequest request, CancellationToken token = default)
        => _api.SendExpectingReasonAsync<GenerateCouponCodesRequest, IReadOnlyList<CouponCodeAdminRecord>>(
            HttpMethod.Post, $"/api/admin/coupons/{couponId}/codes", request, token);

    public Task<(CouponCodeAdminRecord? Result, string? Error)> UpdateCouponCodeAsync(
        Guid couponId, Guid codeId, SaveCouponCodeRequest request, CancellationToken token = default)
        => _api.SendExpectingReasonAsync<SaveCouponCodeRequest, CouponCodeAdminRecord>(
            HttpMethod.Put, $"/api/admin/coupons/{couponId}/codes/{codeId}", request, token);

    public Task<LoadResult<OrganizationSubscriptionAdminRecord>> GetOrganizationSubscriptionsAsync(
        CancellationToken token = default)
        => _api.GetListAsync<OrganizationSubscriptionAdminRecord>("/api/admin/organization-subscriptions", token);

    public Task<(OrganizationSubscriptionAdminRecord? Result, string? Error)> SetOrganizationSubscriptionAsync(
        Guid organizationId, SetOrganizationSubscriptionRequest request, CancellationToken token = default)
        => _api.SendExpectingReasonAsync<SetOrganizationSubscriptionRequest, OrganizationSubscriptionAdminRecord>(
            HttpMethod.Put, $"/api/admin/organization-subscriptions/{organizationId}", request, token);

    public Task<LoadResult<PublicSubscriptionTier>> GetPublicPricingAsync(CancellationToken token = default)
        => _api.GetAnonymousListAsync<PublicSubscriptionTier>("/api/public/pricing", token);

    public Task<OrgSubscriptionView?> GetMySubscriptionAsync(Guid organizationId, CancellationToken token = default)
        => _api.GetAsync<OrgSubscriptionView>($"/api/organizations/{organizationId}/subscription", token);

    public Task<(SubscriptionQuoteResponse? Result, string? Error)> QuoteSubscriptionAsync(
        Guid organizationId, SubscriptionQuoteRequest request, CancellationToken token = default)
        => _api.SendExpectingReasonAsync<SubscriptionQuoteRequest, SubscriptionQuoteResponse>(
            HttpMethod.Post, $"/api/organizations/{organizationId}/subscription/quote", request, token);

    // ── the money trail (item 168) ────────────────────────────────────────────

    public Task<LoadResult<BillingLedgerEntryRecord>> GetBillingLedgerAsync(Guid? orgId = null, CancellationToken token = default)
        => _api.GetListAsync<BillingLedgerEntryRecord>(
            orgId is { } o ? $"/api/admin/billing/ledger?orgId={o}" : "/api/admin/billing/ledger", token);

    public Task<(BillingLedgerEntryRecord? Result, string? Error)> RecordChargeAsync(
        Guid orgId, RecordBillingEntryRequest request, CancellationToken token = default)
        => _api.SendExpectingReasonAsync<RecordBillingEntryRequest, BillingLedgerEntryRecord>(
            HttpMethod.Post, $"/api/admin/billing/organizations/{orgId}/charges", request, token);

    public Task<(BillingLedgerEntryRecord? Result, string? Error)> RecordPaymentAsync(
        Guid orgId, RecordBillingEntryRequest request, CancellationToken token = default)
        => _api.SendExpectingReasonAsync<RecordBillingEntryRequest, BillingLedgerEntryRecord>(
            HttpMethod.Post, $"/api/admin/billing/organizations/{orgId}/payments", request, token);

    public Task<(BillingLedgerEntryRecord? Result, string? Error)> RecordAdjustmentAsync(
        Guid orgId, RecordAdjustmentRequest request, CancellationToken token = default)
        => _api.SendExpectingReasonAsync<RecordAdjustmentRequest, BillingLedgerEntryRecord>(
            HttpMethod.Post, $"/api/admin/billing/organizations/{orgId}/adjustments", request, token);

    public Task<(BillingLedgerEntryRecord? Result, string? Error)> RecordReferralPayoutAsync(
        RecordReferralPayoutRequest request, CancellationToken token = default)
        => _api.SendExpectingReasonAsync<RecordReferralPayoutRequest, BillingLedgerEntryRecord>(
            HttpMethod.Post, "/api/admin/billing/referral-payouts", request, token);

    public Task<LoadResult<TaxRateRuleRecord>> GetTaxRatesAsync(CancellationToken token = default)
        => _api.GetListAsync<TaxRateRuleRecord>("/api/admin/billing/tax-rates", token);

    public Task<(TaxRateRuleRecord? Result, string? Error)> SaveTaxRateAsync(
        SaveTaxRateRuleRequest request, CancellationToken token = default)
        => _api.SendExpectingReasonAsync<SaveTaxRateRuleRequest, TaxRateRuleRecord>(
            HttpMethod.Put, "/api/admin/billing/tax-rates", request, token);

    public Task<bool> DeleteTaxRateAsync(Guid id, CancellationToken token = default)
        => _api.DeleteAsync($"/api/admin/billing/tax-rates/{id}", token);

    public Task<LoadResult<ReferrerSummaryRecord>> GetReferrersAsync(CancellationToken token = default)
        => _api.GetListAsync<ReferrerSummaryRecord>("/api/admin/billing/referrers", token);

    public Task<LoadResult<OrgBillingHistoryRecord>> GetOrgBillingHistoryAsync(Guid organizationId, CancellationToken token = default)
        => _api.GetListAsync<OrgBillingHistoryRecord>($"/api/organizations/{organizationId}/billing/history", token);

    public Task<LoadResult<MemberSeatAdminRecord>> GetMemberSeatsAsync(Guid? orgId = null, CancellationToken token = default)
        => _api.GetListAsync<MemberSeatAdminRecord>(
            orgId is { } o ? $"/api/admin/billing/member-seats?orgId={o}" : "/api/admin/billing/member-seats", token);

    public Task<(MemberSeatAdminRecord? Result, string? Error)> SetMemberSeatAsync(
        Guid seatId, SetMemberSeatRequest request, CancellationToken token = default)
        => _api.SendExpectingReasonAsync<SetMemberSeatRequest, MemberSeatAdminRecord>(
            HttpMethod.Put, $"/api/admin/billing/member-seats/{seatId}", request, token);

    public Task<LoadResult<MyMemberSeatRecord>> GetMySeatAsync(Guid organizationId, CancellationToken token = default)
        => _api.GetListAsync<MyMemberSeatRecord>($"/api/organizations/{organizationId}/billing/my-seats", token);

    public async Task<(byte[] Data, string FileName)?> DownloadReceiptAsync(
        Guid organizationId, Guid entryId, CancellationToken token = default)
    {
        var result = await _api.GetBytesAsync(
            $"/api/organizations/{organizationId}/billing/receipts/{entryId}", "receipt.html", token);
        return result is { } r ? (r.Data, r.FileName) : null;
    }
}
