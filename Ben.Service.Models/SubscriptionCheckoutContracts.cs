using Ben.Data.Common.Enums;

namespace Ben.Service.Models;

/// <summary>What a group is about to be charged, with the coupon line filled in or refused.</summary>
/// <param name="TierName">The band the group's current member count puts it on.</param>
/// <param name="Interval">The cadence being quoted.</param>
/// <param name="ListPrice">One period at that cadence, before any discount.</param>
/// <param name="Discount">The coupon line. Zero when no code was given or the code was refused.</param>
/// <param name="Payable">What would actually be charged.</param>
/// <param name="CouponRefusedBecause">
/// The sentence to show beside the coupon box when the typed code cannot be used — already
/// person-readable, never an HTTP status. Null when there was no code or the code applied.
/// </param>
/// <param name="CouponAppliesForPeriods">
/// How many periods the discount would run: 1, N, or null for forever. Shown so "20% off" and
/// "20% off your first month" cannot be confused at the moment of paying.
/// </param>
public record SubscriptionQuoteResponse(
    string TierName,
    BillingInterval Interval,
    decimal ListPrice,
    decimal Discount,
    decimal Payable,
    string? CouponRefusedBecause,
    int? CouponAppliesForPeriods);

/// <summary>Asking what a period would cost, optionally with a coupon code typed in.</summary>
public record SubscriptionQuoteRequest(BillingInterval Interval, string? CouponCode);

/// <summary>One cap, as the public pricing page shows it.</summary>
/// <param name="Limit">What is capped, as the enum name — the client renders the label.</param>
/// <param name="MaxValue">The cap. Null is unlimited; zero means the feature is not included.</param>
public record PublicTierLimit(SubscriptionLimit Limit, int? MaxValue);

/// <summary>One cadence a band is sold at, with the derived saving.</summary>
/// <param name="Price">The whole price for one period — not a monthly equivalent.</param>
/// <param name="SavingPercentAgainstMonthly">
/// "Save 16%" on the card, floored server-side so the page and the admin screen cannot disagree.
/// </param>
public record PublicTierPrice(BillingInterval Interval, decimal Price, int? SavingPercentAgainstMonthly);

/// <summary>
/// One band on the public pricing page.
/// </summary>
/// <remarks>
/// Deliberately a different shape from <c>SubscriptionTierAdminRecord</c>: no ids beyond the one
/// the checkout needs, no audit fields, no organization counts, no retired rows. A public
/// projection that reuses the admin record would leak whatever the admin record gains next.
/// </remarks>
public record PublicSubscriptionTier(
    Guid Id,
    string Name,
    int MinMembers,
    int? MaxMembers,
    IReadOnlyList<PublicTierPrice> Prices,
    IReadOnlyList<PublicTierLimit> Limits);

/// <summary>One cap as it actually binds a group right now.</summary>
/// <param name="FromContract">
/// True when the group's contract is holding this value against a worse live tier — the "your
/// current terms until {date}" case.
/// </param>
public record OrgEffectiveLimit(SubscriptionLimit Limit, int? MaxValue, bool FromContract);

/// <summary>
/// A group's own view of where it stands: what it is on, until when, and on what terms.
/// </summary>
/// <param name="TierName">
/// The band as it was sold to them — from the contract snapshot when one exists, so a rename or
/// retirement of the live tier cannot rewrite what their screen says they bought.
/// </param>
/// <param name="AnyTermsHeldByContract">
/// True when at least one limit or the price is being held by the contract — the page leads with
/// one sentence about it instead of making the reader hunt the flags.
/// </param>
public record OrgSubscriptionView(
    SubscriptionStatus Status,
    string? TierName,
    BillingInterval Interval,
    decimal? Price,
    bool PriceFromContract,
    DateTime? CurrentPeriodEnd,
    bool CancelAtPeriodEnd,
    IReadOnlyList<OrgEffectiveLimit> Limits,
    bool AnyTermsHeldByContract);
