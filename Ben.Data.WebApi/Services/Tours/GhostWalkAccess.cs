using System.Globalization;
using Ben.Data.Common.Enums;
using Ben.Data.Source.Context;
using Ben.Data.Source.Entities;
using Microsoft.EntityFrameworkCore;

namespace Ben.Data.WebApi.Services.Tours;

/// <summary>The Ghost Walk Tour subscription as the settings offer it today.</summary>
/// <param name="OnSale">The switch is on AND a monthly price above zero is set.</param>
/// <param name="MonthlyPrice">The price for one month, before tax. Zero when none is set.</param>
public sealed record GhostWalkOffer(bool OnSale, decimal MonthlyPrice)
{
    /// <summary>Reads the offer. Unset switch is OFF; an unset or nonsense price means not on sale.</summary>
    public static async Task<GhostWalkOffer> ReadAsync(BenDataContext db, CancellationToken ct)
    {
        var enabled = await SiteSettingsService.GetBoolAsync(db, SiteSettingKeys.GhostWalkEnabled, whenUnset: false, ct);
        var raw = await SiteSettingsService.GetAsync(db, SiteSettingKeys.GhostWalkMonthlyPriceUsd, ct);
        var price = decimal.TryParse(raw, NumberStyles.Number, CultureInfo.InvariantCulture, out var p) && p > 0m
            ? Math.Round(p, 2, MidpointRounding.AwayFromZero)
            : 0m;
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
/// What gates running tours (10/21/2026): the Ghost Walk Tour subscription.
/// </summary>
/// <remarks>
/// <para><b>What gated tours before this.</b> Nothing did. A tour could be defined, dated and booked
/// by any group; the only door was <see cref="SiteSettingKeys.AllowTourBusinessSignUps"/>, which
/// stops a NEW group calling itself a tour company and nothing else. The per-tour business plan
/// (item 233) priced tours for a business that chose to subscribe, but holding no plan never stopped
/// a tour.</para>
///
/// <para><b>What is gated now: starting something new.</b> Adding a tour, bringing a retired one back,
/// and putting a new date for a tour on the calendar. Tours and dates that already exist carry on:
/// guests who have signed up keep their places, and nobody's Saturday walk disappears overnight because
/// a switch was turned on.</para>
///
/// <para><b>Who passes.</b> A standing Ghost Walk Tour subscription. And, so the change is not a
/// surprise bill for somebody already paying: a group whose investigation plan is the per-tour
/// business plan, which has been paying for its tours since item 233.</para>
///
/// <para><b>Off means ungated.</b> While the subscription is not on sale nothing is held back and
/// nothing is said: a refusal that names a product nobody can buy is a closed door with no sign.</para>
/// </remarks>
public static class GhostWalkAccess
{
    /// <summary>The group's Ghost Walk Tour subscription row, if it has one.</summary>
    public static Task<GhostWalkSubscription?> SubscriptionAsync(BenDataContext db, Guid organizationId, CancellationToken ct)
        => db.GhostWalkSubscriptions.AsNoTracking().FirstOrDefaultAsync(s => s.OrganizationId == organizationId, ct);

    /// <summary>Whether the group holds a Ghost Walk Tour subscription that is paid through now.</summary>
    public static Task<bool> IsSubscribedAsync(BenDataContext db, Guid organizationId, DateTime now, CancellationToken ct)
        => db.GhostWalkSubscriptions.AsNoTracking().AnyAsync(
            s => s.OrganizationId == organizationId
              && s.Status == SubscriptionStatus.Active
              && s.CurrentPeriodEnd != null && s.CurrentPeriodEnd > now, ct);

    /// <summary>
    /// The transition: a group already paying for tours through the per-tour business plan.
    /// </summary>
    private static async Task<bool> PaysPerTourAsync(BenDataContext db, Guid organizationId, DateTime now, CancellationToken ct)
    {
        var sub = await db.OrganizationSubscriptions.AsNoTracking()
            .Where(s => s.OrganizationId == organizationId
                     && s.Status == SubscriptionStatus.Active
                     && s.CurrentPeriodEnd != null && s.CurrentPeriodEnd > now)
            .Select(s => s.SubscriptionTierId)
            .FirstOrDefaultAsync(ct);
        if (sub is not { } tierId) return false;

        return await db.SubscriptionTiers.AsNoTracking()
            .AnyAsync(t => t.Id == tierId && !t.IsBandedByMembers, ct);
    }

    /// <summary>
    /// Why this group cannot start a new tour or tour date, or null when it can.
    /// </summary>
    public static async Task<string?> WhyNotAsync(BenDataContext db, Guid organizationId, DateTime now, CancellationToken ct)
    {
        var offer = await GhostWalkOffer.ReadAsync(db, ct);
        if (!offer.OnSale) return null;

        if (await IsSubscribedAsync(db, organizationId, now, ct)) return null;
        if (await PaysPerTourAsync(db, organizationId, now, ct)) return null;

        return RefusalFor(offer);
    }

    /// <summary>The sentence a group without the subscription is shown. It names the price and the way in.</summary>
    public static string RefusalFor(GhostWalkOffer offer)
        => $"Running tours needs a Ghost Walk Tour subscription: ${offer.MonthlyPrice:0.00} a month, with "
         + "no limit on tours or guests. Subscribe on your group's Billing page. Tours and dates you "
         + "already have keep running.";
}
