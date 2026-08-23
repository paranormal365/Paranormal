using Ben.Data.Common.Enums;

namespace Ben.Service.Models.Admin;

/// <summary>What a band costs at one cadence, as the Administration screens see it.</summary>
/// <param name="Interval">The cadence.</param>
/// <param name="Price">The whole price for one period at that cadence — not a monthly equivalent.</param>
/// <param name="IsActive">Retired prices stay for the periods that were billed against them.</param>
/// <param name="SavingPercentAgainstMonthly">
/// How much cheaper this is than paying monthly, floored. Null for the monthly row itself, for a
/// band with no monthly price to compare against, and for a cadence that is not actually cheaper.
/// Derived on the server so the screen and the checkout cannot disagree about it.
/// </param>
public record SubscriptionTierPriceAdminRecord(
    BillingInterval Interval,
    decimal Price,
    bool IsActive,
    int? SavingPercentAgainstMonthly);

/// <summary>One cap on a band. Null max is written-down-unlimited; zero is feature-off.</summary>
public record SubscriptionTierLimitAdminRecord(SubscriptionLimit Limit, int? MaxValue);

/// <summary>One band in the price list, with every cadence it is sold at.</summary>
/// <param name="OrganizationCount">
/// How many organizations are currently on this band. The number that makes retiring a band a
/// decision rather than a click.
/// </param>
public record SubscriptionTierAdminRecord(
    Guid Id,
    string Name,
    int MinMembers,
    int? MaxMembers,
    int SortOrder,
    bool IsActive,
    IReadOnlyList<SubscriptionTierPriceAdminRecord> Prices,
    IReadOnlyList<SubscriptionTierLimitAdminRecord> Limits,
    int OrganizationCount,
    // Item 156 Phase A: the checklist of permission areas this tier includes. Defaulted so
    // callers written before the field existed keep deserializing.
    IReadOnlyList<Ben.Data.Common.Enums.OrganizationPermissionArea>? IncludedAreas = null);

/// <summary>Replaces a tier's included-areas checklist (item 156 Phase A).</summary>
public sealed record SetTierPermissionAreasRequest(
    IReadOnlyList<Ben.Data.Common.Enums.OrganizationPermissionArea> Areas);

/// <summary>A price to write, as the editor sends it.</summary>
public record SaveTierPriceRequest(BillingInterval Interval, decimal Price, bool IsActive);

/// <summary>
/// A band and its whole price list, saved together.
/// </summary>
/// <remarks>
/// The prices come with the band rather than through their own endpoint. Saving a band and its
/// cadences separately means a moment where the band exists at no price, and the validation that
/// matters — that the bands tile the whole member range — has to run against the finished state.
/// </remarks>
public record SaveSubscriptionTierRequest(
    string Name,
    int MinMembers,
    int? MaxMembers,
    int SortOrder,
    bool IsActive,
    IReadOnlyList<SaveTierPriceRequest> Prices,
    IReadOnlyList<SubscriptionTierLimitAdminRecord> Limits);

/// <summary>One redeemable string under a campaign.</summary>
/// <param name="RestrictedToAppUserName">
/// The addressed account's display name, resolved for the screen. Null when the code is open to
/// anybody. The screen must not show a raw id to explain who a code belongs to.
/// </param>
public record CouponCodeAdminRecord(
    Guid Id,
    string Code,
    int? MaxRedemptions,
    int RedemptionCount,
    string? IssuedTo,
    Guid? RestrictedToAppUserId,
    string? RestrictedToAppUserName,
    bool IsActive,
    DateTime DateCreated);

/// <summary>A discount campaign as Administration sees it.</summary>
/// <param name="CodeCount">How many codes exist under it. One, for a shared campaign.</param>
/// <param name="SharedCode">
/// The single code, for a shared campaign — so the list can show it without a second request.
/// Null for a generated batch, where there is no one code to show.
/// </param>
/// <param name="Problem">
/// What is wrong with this campaign, or null. Surfaced in the list rather than only at redemption,
/// because a coupon that takes nothing off looks completely normal until somebody tries it.
/// </param>
public record CouponAdminRecord(
    Guid Id,
    string Name,
    string? Description,
    CouponKind Kind,
    int? PercentOff,
    decimal? AmountOff,
    CouponDuration Duration,
    int? DurationPeriods,
    int? MaxRedemptions,
    int RedemptionCount,
    DateTime? ValidFromUtc,
    DateTime? RedeemByUtc,
    BillingInterval? AppliesToInterval,
    CouponApplicability AppliesTo,
    bool IsActive,
    int CodeCount,
    string? SharedCode,
    string? Problem,
    DateTime DateCreated);

/// <summary>
/// Creating or editing a campaign. The codes are managed separately once it exists.
/// </summary>
/// <param name="SharedCode">
/// For a shared campaign, the code itself. Ignored for a generated batch, whose codes come from
/// <see cref="GenerateCouponCodesRequest"/>.
/// </param>
public record SaveCouponRequest(
    string Name,
    string? Description,
    CouponKind Kind,
    int? PercentOff,
    decimal? AmountOff,
    CouponDuration Duration,
    int? DurationPeriods,
    int? MaxRedemptions,
    DateTime? ValidFromUtc,
    DateTime? RedeemByUtc,
    BillingInterval? AppliesToInterval,
    CouponApplicability AppliesTo,
    bool IsActive,
    string? SharedCode);

/// <summary>Generating a batch of codes under an existing campaign.</summary>
/// <param name="Count">How many codes to make.</param>
/// <param name="Prefix">A campaign marker such as PARACON, for the humans handing them out.</param>
/// <param name="MaxRedemptionsPerCode">
/// Usually one — the single-use batch. Null means each code is unlimited, which is a shared code
/// with extra steps and is almost never what is wanted.
/// </param>
/// <param name="RestrictedToAppUserId">
/// Addresses every code in this run to one account. For a run of one, which is the only shape
/// where addressing a whole batch to a single person makes sense.
/// </param>
public record GenerateCouponCodesRequest(
    int Count,
    string? Prefix,
    int? MaxRedemptionsPerCode,
    Guid? RestrictedToAppUserId);

/// <summary>Editing one code — withdrawing it, or addressing it to somebody.</summary>
public record SaveCouponCodeRequest(
    int? MaxRedemptions,
    string? IssuedTo,
    Guid? RestrictedToAppUserId,
    bool IsActive);

/// <summary>Where one organization stands, for the Administration list and detail.</summary>
/// <param name="CurrentMemberCount">
/// Members <i>now</i>, which is deliberately shown next to
/// <paramref name="MemberCountAtPeriodStart"/>: the gap between them is what the group will be
/// re-banded on at renewal, and it is the number an administrator is actually looking for.
/// </param>
/// <param name="ResolvedTierName">
/// The band the current member count would fall into, which may not be the band being billed.
/// Null when the price list cannot price anybody — itself worth showing.
/// </param>
public record OrganizationSubscriptionAdminRecord(
    Guid Id,
    Guid OrganizationId,
    string OrganizationName,
    SubscriptionStatus Status,
    Guid? SubscriptionTierId,
    string? SubscriptionTierName,
    BillingInterval Interval,
    int MemberCountAtPeriodStart,
    int CurrentMemberCount,
    string? ResolvedTierName,
    decimal PriceAtPeriodStart,
    DateTime? CurrentPeriodStart,
    DateTime? CurrentPeriodEnd,
    bool CancelAtPeriodEnd,
    DateTime? LapsedAtUtc,
    DateTime? FirstPaidPeriodStartUtc,
    string? ProviderName);

/// <summary>
/// A SuperAdmin setting an organization's subscription by hand.
/// </summary>
/// <remarks>
/// This is the manual provider. Until money is actually taken by Square or PayPal, somebody has to
/// be able to say "this group is paid up until March", and that somebody is a SuperAdmin. The same
/// endpoint stays useful afterwards for the cases every payment provider produces — a refund, a
/// comped account, a group that paid by cheque.
/// </remarks>
public record SetOrganizationSubscriptionRequest(
    SubscriptionStatus Status,
    Guid? SubscriptionTierId,
    BillingInterval Interval,
    DateTime? CurrentPeriodStart,
    DateTime? CurrentPeriodEnd,
    bool CancelAtPeriodEnd,
    string? Note,
    string? CouponCode = null);

/// <summary>One redemption on the referral report — enough to compute a reimbursement from.</summary>
/// <param name="ReferrerNote">The code's IssuedTo — who handed this code out.</param>
public record CouponRedemptionAdminRecord(
    string Code,
    string? ReferrerNote,
    string OrganizationName,
    DateTime RedeemedAtUtc,
    decimal ListPrice,
    decimal Discount,
    decimal Payable);

/// <summary>One consequence of a tier edit, for the confirm step.</summary>
public record TierChangeRecord(bool IsImprovement, string Sentence);

/// <summary>
/// What saving this edit will do, shown to the SuperAdmin before they commit it.
/// </summary>
/// <remarks>
/// The blast radius belongs on the screen before the save, not in support tickets after it:
/// "this lowers storage for 12 paid groups — they will be notified before renewal" is a decision,
/// and the person making it should see it stated.
/// </remarks>
public record TierImpactRecord(
    IReadOnlyList<TierChangeRecord> Changes,
    int GroupsMessagedNow,
    int PaidGroupsNoticed);
