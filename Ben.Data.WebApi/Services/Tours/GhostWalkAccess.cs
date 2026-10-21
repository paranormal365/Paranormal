using System.Globalization;
using Ben.Data.Common.Enums;
using Ben.Data.Source.Context;
using Ben.Data.Source.Entities;
using Ben.Data.Source.Services;
using Microsoft.EntityFrameworkCore;

namespace Ben.Data.WebApi.Services.Tours;

/// <summary>The Ghost Walk Tour subscription as the settings offer it today.</summary>
/// <param name="OnSale">The switch is on and the price is above zero.</param>
/// <param name="MonthlyPrice">One tour route for one month, before tax.</param>
public sealed record GhostWalkOffer(bool OnSale, decimal MonthlyPrice)
{
    /// <summary>Ben's price, 10/21/2026: $29 a month per tour route. What an unset price means.</summary>
    public const decimal DefaultMonthlyPrice = 29m;

    /// <summary>Reads the offer. Unset switch is OFF; an unset or nonsense price is the $29 default.</summary>
    public static async Task<GhostWalkOffer> ReadAsync(BenDataContext db, CancellationToken ct)
    {
        var enabled = await SiteSettingsService.GetBoolAsync(db, SiteSettingKeys.GhostWalkEnabled, whenUnset: false, ct);
        var raw = await SiteSettingsService.GetAsync(db, SiteSettingKeys.GhostWalkMonthlyPriceUsd, ct);
        var price = decimal.TryParse(raw, NumberStyles.Number, CultureInfo.InvariantCulture, out var p) && p > 0m
            ? Math.Round(p, 2, MidpointRounding.AwayFromZero)
            : DefaultMonthlyPrice;
        return new GhostWalkOffer(enabled && price > 0m, price);
    }

    /// <summary>Why a typed monthly price cannot be saved, or null when it can (empty clears it).</summary>
    public static string? WhyNotAPrice(string? raw)
        => string.IsNullOrWhiteSpace(raw)
           || (decimal.TryParse(raw.Trim(), NumberStyles.Number, CultureInfo.InvariantCulture, out var p) && p > 0m)
            ? null
            : "Write the price as a number above zero, without the dollar sign — for example 29 or 29.99.";
}

/// <summary>
/// What gates running tours: the Ghost Walk Tour subscription, priced per tour route (Ben, 10/21/2026).
/// </summary>
/// <remarks>
/// <para><b>A route is a <see cref="Tour"/></b> — "the walk down Printers Alley is one tour whether you
/// run it twice a year or four nights a week". The subscription pays for a number of routes; dates
/// and guests on a paid route are unlimited, so a new DATE needs only a standing subscription, while
/// a new ROUTE needs a route the subscription pays for.</para>
///
/// <para><b>Starting something new is what is gated.</b> Adding a route, bringing a retired one back,
/// and putting a new date on the calendar. Routes and dates that already exist carry on, and their
/// guests keep their places.</para>
///
/// <para><b>Off means ungated.</b> While the subscription is not on sale nothing is held back and
/// nothing is said: a refusal that names a product nobody can buy is a closed door with no sign.</para>
/// </remarks>
public static class GhostWalkAccess
{
    /// <summary>The group's Ghost Walk Tour subscription row, if it has one.</summary>
    public static Task<GhostWalkSubscription?> SubscriptionAsync(BenDataContext db, Guid organizationId, CancellationToken ct)
        => db.GhostWalkSubscriptions.FirstOrDefaultAsync(s => s.OrganizationId == organizationId, ct);

    /// <summary>Whether the group holds a Ghost Walk Tour subscription that is paid through now.</summary>
    public static Task<bool> IsSubscribedAsync(BenDataContext db, Guid organizationId, DateTime now, CancellationToken ct)
        => db.GhostWalkSubscriptions.AsNoTracking().AnyAsync(
            s => s.OrganizationId == organizationId
              && s.Status == SubscriptionStatus.Active
              && s.CurrentPeriodEnd != null && s.CurrentPeriodEnd > now, ct);

    /// <summary>Routes the group runs: tours not retired.</summary>
    public static Task<int> ActiveRoutesAsync(BenDataContext db, Guid organizationId, CancellationToken ct)
        => db.Tours.CountAsync(t => t.OrganizationId == organizationId && t.RetiredAtUtc == null, ct);

    /// <summary>
    /// Why this group cannot put a new DATE on the calendar for one of its routes, or null when it can.
    /// Dates are unlimited on a paid route, so only a standing subscription is asked for.
    /// </summary>
    public static async Task<string?> WhyNotAsync(BenDataContext db, Guid organizationId, DateTime now, CancellationToken ct)
    {
        var offer = await GhostWalkOffer.ReadAsync(db, ct);
        if (!offer.OnSale) return null;
        return await IsSubscribedAsync(db, organizationId, now, ct) ? null : RefusalFor(offer);
    }

    /// <summary>The answer for one more route: a tour added, or a retired one brought back.</summary>
    /// <param name="Refusal">Why not, or null when it may go ahead as it is.</param>
    /// <param name="NeedsARoute">True when the only thing in the way is that every paid route is in
    /// use — the group can say yes to adding a route to its subscription.</param>
    /// <param name="Subscription">The standing subscription, when there is one.</param>
    public sealed record RouteVerdict(string? Refusal, bool NeedsARoute, GhostWalkSubscription? Subscription, GhostWalkOffer Offer);

    /// <summary>Asks whether one more route may run, without changing anything.</summary>
    public static async Task<RouteVerdict> ForOneMoreRouteAsync(
        BenDataContext db, Guid organizationId, DateTime now, CancellationToken ct)
    {
        var offer = await GhostWalkOffer.ReadAsync(db, ct);
        var sub = await SubscriptionAsync(db, organizationId, ct);
        var standing = sub is not null && sub.IsStanding(now);

        if (!offer.OnSale) return new RouteVerdict(null, false, standing ? sub : null, offer);
        if (!standing) return new RouteVerdict(RefusalFor(offer), false, null, offer);

        var active = await ActiveRoutesAsync(db, organizationId, ct);
        if (active < sub!.Quantity) return new RouteVerdict(null, false, sub, offer);

        return new RouteVerdict(FullSentence(sub.Quantity, offer, sub, now), true, sub, offer);
    }

    /// <summary>The sentence a group without the subscription is shown. It names the price and the way in.</summary>
    public static string RefusalFor(GhostWalkOffer offer)
        => $"Running tours needs a Ghost Walk Tour subscription: ${offer.MonthlyPrice:0.00} a month per tour "
         + "route, with unlimited walks and guests on each. Subscribe on your group's Billing page. Tours "
         + "and dates you already have keep running.";

    /// <summary>What a group whose paid routes are all running is asked.</summary>
    public static string FullSentence(int paid, GhostWalkOffer offer, GhostWalkSubscription sub, DateTime now)
        => $"Your Ghost Walk Tour subscription pays for {paid} {(paid == 1 ? "route" : "routes")}, and "
         + $"{(paid == 1 ? "it is" : "all of them are")} running. A new route adds ${UnitMonthly(sub, offer):0.00} a month "
         + $"to your subscription; for the rest of this month only the days left are charged, "
         + $"${GhostWalkRoutes.Prorate(sub, now, +1):0.00}, on your next bill. Add a route to your subscription "
         + "to go ahead.";

    private static decimal UnitMonthly(GhostWalkSubscription sub, GhostWalkOffer offer) => offer.MonthlyPrice;
}

/// <summary>
/// Changing how many routes a Ghost Walk Tour subscription pays for, and what that is worth (10/21/2026).
/// </summary>
/// <remarks>
/// <para><b>Stripe's default proration.</b> Raising the quantity part-way through a month owes the
/// remaining days of the extra route; lowering it credits the remaining days of the route given up.
/// Like Stripe's default ("create prorations"), neither is charged on the spot: each waits as a line
/// on the next monthly bill. Whole days, counting today, rounded once to the cent —
/// <see cref="TourBilling.Remainder"/>, the arithmetic the per-tour plan already used.</para>
/// </remarks>
public static class GhostWalkRoutes
{
    /// <summary>The most routes one subscription may pay for, so a typo is not a four-figure bill.</summary>
    public const int MaximumRoutes = 50;

    /// <summary>One route for the current period, at the price the period opened at.</summary>
    public static decimal UnitPrice(GhostWalkSubscription sub)
        => sub.UnitPriceAtPeriodStart > 0m
            ? sub.UnitPriceAtPeriodStart
            : sub.Quantity > 0 ? Math.Round(sub.PriceAtPeriodStart / sub.Quantity, 2, MidpointRounding.AwayFromZero) : 0m;

    /// <summary>
    /// What changing the quantity by <paramref name="delta"/> now is worth for the rest of the period:
    /// positive is owed, negative is credited.
    /// </summary>
    public static decimal Prorate(GhostWalkSubscription sub, DateTime now, int delta)
    {
        if (delta == 0 || sub.CurrentPeriodStart is not { } start || sub.CurrentPeriodEnd is not { } end) return 0m;
        var amount = TourBilling.Remainder(UnitPrice(sub), start, end, now, Math.Abs(delta));
        return delta > 0 ? amount : -amount;
    }

    /// <summary>
    /// Moves the quantity to <paramref name="to"/> and records what it is worth. The caller saves.
    /// </summary>
    /// <returns>The sentence the customer is shown.</returns>
    public static string Change(BenDataContext db, GhostWalkSubscription sub, int to, Guid userId, DateTime now)
    {
        var from = sub.Quantity;
        var amount = Prorate(sub, now, to - from);
        db.GhostWalkRouteChanges.Add(new GhostWalkRouteChange
        {
            Id = Guid.NewGuid(), GhostWalkSubscriptionId = sub.Id, ChangedUtc = now,
            FromQuantity = from, ToQuantity = to, ProratedAmount = amount,
            DateCreated = now, CreatedByAppUserId = userId,
        });
        sub.Quantity = to;
        sub.DateUpdated = now;
        sub.UpdatedByAppUserId = userId;

        var routes = to == 1 ? "1 route" : $"{to} routes";
        return amount >= 0m
            ? $"Your Ghost Walk Tour subscription now pays for {routes}. ${amount:0.00} for the days left this "
              + $"month goes on your next bill on {sub.CurrentPeriodEnd:MM/dd/yyyy}."
            : $"Your Ghost Walk Tour subscription now pays for {routes}. The ${-amount:0.00} for the days left "
              + $"this month comes off your next bill on {sub.CurrentPeriodEnd:MM/dd/yyyy}.";
    }

    /// <summary>Route changes waiting for the next bill, oldest first.</summary>
    public static Task<List<GhostWalkRouteChange>> UnsettledAsync(BenDataContext db, Guid subscriptionId, DateTime before, CancellationToken ct)
        => db.GhostWalkRouteChanges
            .Where(c => c.GhostWalkSubscriptionId == subscriptionId && c.SettledUtc == null && c.ChangedUtc <= before)
            .OrderBy(c => c.ChangedUtc)
            .ToListAsync(ct);

    /// <summary>
    /// When a route is retired: the subscription pays for one route fewer, down to the routes still
    /// running and never below one. Answers the sentence, or null when nothing changed. The caller saves.
    /// </summary>
    public static async Task<string?> AfterRetiringAsync(
        BenDataContext db, Guid organizationId, Guid userId, DateTime now, CancellationToken ct)
    {
        var sub = await GhostWalkAccess.SubscriptionAsync(db, organizationId, ct);
        if (sub is null || !sub.IsStanding(now)) return null;

        var running = Math.Max(1, await GhostWalkAccess.ActiveRoutesAsync(db, organizationId, ct));
        return sub.Quantity > running ? Change(db, sub, sub.Quantity - 1, userId, now) : null;
    }
}
