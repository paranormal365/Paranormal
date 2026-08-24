using Ben.Service.Models;
using Ben.Service.Models.Admin;
using Ben.Web.Services.WebApi;

namespace Ben.Web.Services;

/// <summary>
/// Subscriptions, the price list, and coupons — the billing slice of the admin client.
/// </summary>
/// <remarks>
/// Everything here except <see cref="QuoteSubscriptionAsync"/> is SuperAdmin-only, enforced by the
/// WebApi. The quote is the one member-facing call: it backs the coupon line on a group's own
/// checkout, and is gated on the group's OrganizationSettings permission instead.
/// </remarks>
public interface IBenBillingClient
{
    // ── the price list ────────────────────────────────────────────────────────

    /// <summary>Every band with its per-cadence prices and caps, in display order.</summary>
    Task<LoadResult<SubscriptionTierAdminRecord>> GetSubscriptionTiersAsync(CancellationToken token = default);
    Task<(SubscriptionTierAdminRecord? Result, string? Error)> SetTierPermissionAreasAsync(Guid tierId, IReadOnlyList<Ben.Data.Common.Enums.OrganizationPermissionArea> areas, CancellationToken token = default);
    Task<(SubscriptionTierAdminRecord? Result, string? Error)> SetTierCapabilitiesAsync(Guid tierId, IReadOnlyList<Ben.Data.Common.Enums.TierCapability> capabilities, CancellationToken token = default);

    /// <summary>What is wrong with the price list as it stands, or null when it is sound.</summary>
    Task<string?> GetTierValidationAsync(CancellationToken token = default);

    /// <summary>Creates a band. The reason string is the server's refusal, when it refused.</summary>
    Task<(SubscriptionTierAdminRecord? Result, string? Error)> CreateSubscriptionTierAsync(
        SaveSubscriptionTierRequest request, CancellationToken token = default);

    /// <summary>Saves a band and its whole price list together.</summary>
    Task<(SubscriptionTierAdminRecord? Result, string? Error)> UpdateSubscriptionTierAsync(
        Guid id, SaveSubscriptionTierRequest request, CancellationToken token = default);

    /// <summary>
    /// What saving this edit would do to the groups on the band — computed without saving or
    /// sending anything. The editor shows it before the save is confirmed.
    /// </summary>
    Task<TierImpactRecord?> PreviewTierImpactAsync(
        Guid id, SaveSubscriptionTierRequest request, CancellationToken token = default);

    // ── coupons ───────────────────────────────────────────────────────────────

    /// <summary>Every campaign, newest first, each carrying its misconfiguration when it has one.</summary>
    Task<LoadResult<CouponAdminRecord>> GetCouponsAsync(CancellationToken token = default);

    /// <summary>The codes under one campaign, with who each is addressed to.</summary>
    Task<LoadResult<CouponCodeAdminRecord>> GetCouponCodesAsync(Guid couponId, CancellationToken token = default);

    /// <summary>The referral report: every redemption under a campaign, with the frozen money.</summary>
    Task<LoadResult<CouponRedemptionAdminRecord>> GetCouponRedemptionsAsync(Guid couponId, CancellationToken token = default);

    Task<(CouponAdminRecord? Result, string? Error)> CreateCouponAsync(
        SaveCouponRequest request, CancellationToken token = default);

    Task<(CouponAdminRecord? Result, string? Error)> UpdateCouponAsync(
        Guid id, SaveCouponRequest request, CancellationToken token = default);

    /// <summary>Generates a batch of codes under a campaign, returning the batch itself.</summary>
    Task<(IReadOnlyList<CouponCodeAdminRecord>? Result, string? Error)> GenerateCouponCodesAsync(
        Guid couponId, GenerateCouponCodesRequest request, CancellationToken token = default);

    /// <summary>Edits one code — withdrawing it, capping it, or addressing it to somebody.</summary>
    Task<(CouponCodeAdminRecord? Result, string? Error)> UpdateCouponCodeAsync(
        Guid couponId, Guid codeId, SaveCouponCodeRequest request, CancellationToken token = default);

    // ── organizations ─────────────────────────────────────────────────────────

    /// <summary>Every organization's standing, including those never set up.</summary>
    Task<LoadResult<OrganizationSubscriptionAdminRecord>> GetOrganizationSubscriptionsAsync(
        CancellationToken token = default);

    /// <summary>Sets one organization's subscription by hand — the manual payment provider.</summary>
    Task<(OrganizationSubscriptionAdminRecord? Result, string? Error)> SetOrganizationSubscriptionAsync(
        Guid organizationId, SetOrganizationSubscriptionRequest request, CancellationToken token = default);

    // ── public and group-facing ───────────────────────────────────────────────

    /// <summary>The public price list, exactly as the pricing page shows it. Anonymous.</summary>
    Task<LoadResult<PublicSubscriptionTier>> GetPublicPricingAsync(CancellationToken token = default);

    /// <summary>
    /// Where one of the caller's groups stands. Null when the caller may not see that group's
    /// billing — the page skips the card rather than guessing.
    /// </summary>
    Task<OrgSubscriptionView?> GetMySubscriptionAsync(Guid organizationId, CancellationToken token = default);

    // ── checkout ──────────────────────────────────────────────────────────────

    /// <summary>
    /// The coupon line: what one period would cost this group, with the typed code applied or the
    /// sentence explaining why it was not. Mutates nothing — typing a code is not redeeming it.
    /// </summary>
    Task<(SubscriptionQuoteResponse? Result, string? Error)> QuoteSubscriptionAsync(
        Guid organizationId, SubscriptionQuoteRequest request, CancellationToken token = default);

    // ── the money trail (item 168) ────────────────────────────────────────────

    /// <summary>The whole ledger, or one group's slice of it. SuperAdmin.</summary>
    Task<LoadResult<BillingLedgerEntryRecord>> GetBillingLedgerAsync(Guid? orgId = null, CancellationToken token = default);

    Task<(BillingLedgerEntryRecord? Result, string? Error)> RecordChargeAsync(
        Guid orgId, RecordBillingEntryRequest request, CancellationToken token = default);
    Task<(BillingLedgerEntryRecord? Result, string? Error)> RecordPaymentAsync(
        Guid orgId, RecordBillingEntryRequest request, CancellationToken token = default);
    Task<(BillingLedgerEntryRecord? Result, string? Error)> RecordAdjustmentAsync(
        Guid orgId, RecordAdjustmentRequest request, CancellationToken token = default);
    Task<(BillingLedgerEntryRecord? Result, string? Error)> RecordReferralPayoutAsync(
        RecordReferralPayoutRequest request, CancellationToken token = default);

    Task<LoadResult<TaxRateRuleRecord>> GetTaxRatesAsync(CancellationToken token = default);
    Task<(TaxRateRuleRecord? Result, string? Error)> SaveTaxRateAsync(
        SaveTaxRateRuleRequest request, CancellationToken token = default);
    Task<bool> DeleteTaxRateAsync(Guid id, CancellationToken token = default);

    /// <summary>Every referrer's standing: what their coupons brought in vs what has been paid out.</summary>
    Task<LoadResult<ReferrerSummaryRecord>> GetReferrersAsync(CancellationToken token = default);

    /// <summary>A group's own billing history — charges, payments, adjustments. Org-gated.</summary>
    Task<LoadResult<OrgBillingHistoryRecord>> GetOrgBillingHistoryAsync(Guid organizationId, CancellationToken token = default);

    /// <summary>A payment row's receipt as bytes, for the downloadFileFromBase64 hand-off.
    /// Null when the row is not a payment or the caller may not see it.</summary>
    Task<(byte[] Data, string FileName)?> DownloadReceiptAsync(Guid organizationId, Guid entryId, CancellationToken token = default);
}
