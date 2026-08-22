using Ben.Service.Models;
using Ben.Service.Models.Admin;

namespace Ben.Web.Services.WebApi;

/// <summary>The billing slice — see <see cref="IBenBillingClient"/> for the contract.</summary>
public sealed partial class BenAdminClientAdapter
{
    public Task<LoadResult<SubscriptionTierAdminRecord>> GetSubscriptionTiersAsync(CancellationToken token = default)
        => _api.GetListAsync<SubscriptionTierAdminRecord>("/api/admin/subscription-tiers", token);

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
}
