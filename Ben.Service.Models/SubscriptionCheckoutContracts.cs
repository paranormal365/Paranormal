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
    int? CouponAppliesForPeriods,
    // Item 168: tax is its own line, computed on the payable amount from the group's state.
    // Zero when no rule matches — an honest zero, not an assumption hidden in the total.
    decimal TaxRatePercent = 0m,
    decimal Tax = 0m,
    // Item 233: the quantity behind the list price. A group is one unit of its band; a tour
    // business is one unit per tour it runs. Defaulted so an older server reads as before.
    int Units = 1,
    decimal? UnitPrice = null,
    string UnitsAre = "members")
{
    public decimal TotalWithTax => Payable + Tax;

    /// <summary>Whether the list price is a multiple worth showing — "3 tours × $29.00".</summary>
    public bool IsPerUnit => UnitsAre == "tours";
}

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
    IReadOnlyList<PublicTierLimit> Limits,
    IReadOnlyList<Ben.Data.Common.Enums.OrganizationPermissionArea>? IncludedAreas = null,
    IReadOnlyList<Ben.Data.Common.Enums.TierCapability>? IncludedCapabilities = null,
    // Item 198: false means this plan is not a member band at all — it is sold to a kind of
    // business, and showing it a headcount range says something untrue about who it is for.
    // Appended, because this record is built positionally.
    bool IsBandedByMembers = true);

/// <summary>
/// What the pricing page says beside the bands: the free account's storage, and what an event costs.
/// </summary>
/// <remarks>
/// Ben, 10/13/2026: each plan shows what it includes, and the page offers events for sale with what
/// one includes. Both numbers are site settings, so they are read here rather than typed on the page.
/// </remarks>
/// <param name="FreeAccountStorageMegabytes">How much one person may keep without a paid plan.
/// Members of a group on a paid plan aren't capped.</param>
public record PublicPricingExtras(
    int FreeAccountStorageMegabytes, PublicEventOffer Events,
    // 10/21/2026: the Ghost Walk Tour subscription. Null when it is not on sale, so a page that has
    // it in hand has a price to show and a page without it says nothing at all.
    PublicGhostWalkOffer? GhostWalk = null);

/// <summary>The Ghost Walk Tour subscription as a visitor reads it (10/21/2026). Only sent while on sale.</summary>
/// <param name="MonthlyPrice">One month, before tax.</param>
public record PublicGhostWalkOffer(decimal MonthlyPrice);

/// <summary>One bundle of event credits as a buyer reads it.</summary>
/// <param name="Quantity">How many credits.</param>
/// <param name="FullPrice">The bundle's price as the settings say it.</param>
/// <param name="Price">What THIS reader pays for it — the subscriber percentage of the full price
/// for a group with a paid plan, otherwise the full price. On the public price list it is the full
/// price.</param>
/// <param name="SubscriberPrice">The paid-subscriber price for the bundle, for the price list to show
/// beside the full one.</param>
public record EventCreditBundleOffer(int Quantity, decimal FullPrice, decimal Price, decimal SubscriberPrice);

/// <summary>One hosted event bought on its own (an event credit), as a visitor reads it.</summary>
/// <param name="OnSale">Whether events can be bought at the moment.</param>
/// <param name="UnitPrice">The price of one event, before tax.</param>
/// <param name="LifeDays">How long a bought event stays usable.</param>
/// <param name="MaximumPerPurchase">The most that can be bought at once.</param>
/// <param name="CancellationWindowHours">Calling an event off at least this long before it starts
/// gives the event back.</param>
/// <param name="StorageMegabytes">Room for each event's files, gallery and photos.</param>
/// <param name="FilesKeptDays">How long an event's files are kept after its last date.</param>
/// <param name="SubscriberPercent">What a group with a paid plan pays, as a percentage of the full
/// price (10/21/2026).</param>
/// <param name="SubscriberUnitPrice">One event at that percentage.</param>
/// <param name="Bundles">Several events for one price, smallest first.</param>
public record PublicEventOffer(
    bool OnSale, decimal UnitPrice, int LifeDays, int MaximumPerPurchase,
    int CancellationWindowHours, int StorageMegabytes, int FilesKeptDays,
    decimal SubscriberPercent = 20m, decimal SubscriberUnitPrice = 0m,
    IReadOnlyList<EventCreditBundleOffer>? Bundles = null);

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
    bool AnyTermsHeldByContract,
    // When a missed renewal's two-week grace runs out; null when the plan isn't in one (Ben, 10/09/2026).
    DateTime? GraceUntilUtc = null);

/// <summary>Starts a paid checkout: the cadence, and whatever coupon was typed.</summary>
public record StartCheckoutRequest(BillingInterval Interval, string? CouponCode);

/// <param name="RedirectUrl">Where to send the person: Stripe's hosted page — or, for a
/// 100%-off period that charges nothing, straight back to billing, already subscribed.</param>
/// <param name="PaidWithoutCharge">True on the free-coupon path, so the screen can say
/// "you're subscribed" instead of "redirecting to payment…".</param>
public record StartCheckoutResponse(string RedirectUrl, bool PaidWithoutCharge);

/// <summary>
/// A group's Ghost Walk Tour subscription as its billing page shows it (10/21/2026).
/// </summary>
/// <param name="OnSale">Whether it can be bought. False hides the card, unless the group already
/// holds a period that is still running.</param>
/// <param name="MonthlyPrice">Today's price for one month, before tax.</param>
/// <param name="Status">The row's status, or null when the group has never subscribed.</param>
/// <param name="IsStanding">Paid through right now, so tours are unlocked.</param>
/// <param name="CurrentPeriodEnd">When the paid month ends.</param>
/// <param name="CancelAtPeriodEnd">The group has said not to renew.</param>
/// <param name="PricePaid">What the current month was charged.</param>
public record GhostWalkSubscriptionView(
    bool OnSale, decimal MonthlyPrice, SubscriptionStatus? Status, bool IsStanding,
    DateTime? CurrentPeriodEnd, bool CancelAtPeriodEnd, decimal? PricePaid);
