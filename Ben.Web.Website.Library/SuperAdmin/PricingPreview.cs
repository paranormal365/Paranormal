using System.Globalization;
using Ben.Data.Common.Enums;
using Ben.Service.Models;
using Ben.Service.Models.Admin;

namespace Ben.Web.Website.Library.SuperAdmin;

/// <summary>
/// The one plain sentence under each section of Administration → Pricing (Ben, 10/22/2026), e.g.
/// "A group with a paid plan pays $19.80 per event."
/// </summary>
/// <remarks>
/// <para>Worked out from what the server says it charges — the public price list for events and
/// the Ghost Walk Tour subscription, and the tier rows for plans — never from the boxes on the page.
/// A preview computed here from the typed values would be a second copy of the pricing rules, and
/// the first time the two disagreed the sentence would promise a price checkout does not charge.</para>
///
/// <para>Static and separate from the page so the sentences can be tested without rendering it.</para>
/// </remarks>
public static class PricingPreview
{
    private static readonly CultureInfo Us = CultureInfo.GetCultureInfo("en-US");

    /// <summary>A price as the site writes one: $19.80.</summary>
    public static string Money(decimal amount) => amount.ToString("$#,0.00", Us);

    /// <summary>A percentage without trailing zeros: 20%, 12.5%.</summary>
    public static string Percent(decimal value) => value.ToString("0.##", Us) + "%";

    /// <summary>The plans sentence: how many are on sale, and from what to what each month.</summary>
    /// <param name="tiers">The tier rows; retired ones are left out here.</param>
    /// <param name="purchasesOn">The "Sell plans and seats" switch as the site reads it.</param>
    public static string Plans(IReadOnlyList<SubscriptionTierAdminRecord> tiers, bool purchasesOn)
    {
        var live = tiers.Where(t => t.IsActive).ToList();
        if (live.Count == 0)
            return "There are no plans yet, so the Pricing page has nothing to list.";

        var monthly = live
            .SelectMany(t => t.Prices.Where(p => p.IsActive && p.Interval == BillingInterval.Monthly))
            .Select(p => p.Price)
            .Where(p => p > 0m)
            .ToList();

        var count = live.Count == 1 ? "1 plan" : $"{live.Count} plans";
        var range = monthly.Count switch
        {
            0 => "",
            _ when monthly.Min() == monthly.Max() => $" at {Money(monthly.Min())} a month",
            _ => $", from {Money(monthly.Min())} to {Money(monthly.Max())} a month",
        };

        return purchasesOn
            ? $"Groups can buy {count}{range}."
            : $"Plans aren't on sale. The Pricing page still shows {count}{range}, without the buttons to buy.";
    }

    /// <summary>The events sentence, from the price list the public Pricing page reads.</summary>
    /// <param name="offer">Null when the price list could not be read.</param>
    public static string Events(PublicEventOffer? offer)
    {
        if (offer is null) return "The event prices couldn't be read just now.";
        if (!offer.OnSale) return "Event credits aren't on sale. Credits people already have still work.";

        var line = $"One event costs {Money(offer.UnitPrice)}. A group with a paid plan pays "
                   + $"{Money(offer.SubscriberUnitPrice)} per event ({Percent(offer.SubscriberPercent)} of the full price).";

        if (offer.Bundles is { Count: > 0 } bundles)
        {
            var each = bundles.Select(b => $"{b.Quantity} for {Money(b.FullPrice)} ({Money(b.SubscriberPrice)} with a paid plan)");
            line += $" Bundles: {string.Join(", ", each)}.";
        }

        return line;
    }

    /// <summary>The Ghost Walk Tour sentence.</summary>
    /// <param name="offer">The offer as the public price list sends it: null whenever it is not on sale.</param>
    /// <param name="switchedOn">The "Sell the Ghost Walk Tour subscription" switch as the site reads it.</param>
    /// <param name="extrasRead">Whether the price list could be read at all.</param>
    public static string GhostWalk(PublicGhostWalkOffer? offer, bool switchedOn, bool extrasRead = true)
    {
        if (offer is { } on)
            return $"A tour group pays {Money(on.MonthlyPrice)} a month for each tour route, with unlimited walks and guests.";
        if (!extrasRead) return "The Ghost Walk Tour price couldn't be read just now.";
        return switchedOn
            ? "It's switched on but has no price, so no tour group is offered it."
            : "It's off, so tour groups aren't offered it and the site doesn't mention it.";
    }

    /// <summary>What the menus do with Pricing, given both selling switches.</summary>
    public static string Menus(bool plansOn, bool eventsOn)
        => plansOn || eventsOn
            ? "The menus, the home page and the footer link to the Pricing page."
            : "Nothing is on sale, so the menus, the home page and the footer don't link to the Pricing page.";
}
