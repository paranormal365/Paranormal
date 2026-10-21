using System.Globalization;
using System.Text.RegularExpressions;
using Ben.Data.Common.Enums;
using Ben.Data.Source.Context;
using Microsoft.EntityFrameworkCore;

namespace Ben.Data.WebApi.Services.Events;

/// <summary>One bundle offer: several event credits for one price.</summary>
public sealed record EventCreditBundle(int Quantity, decimal Price);

/// <summary>
/// What a purchase of event credits costs, worked out once and shown and charged the same.
/// </summary>
/// <param name="Quantity">How many credits are being bought.</param>
/// <param name="FullPrice">The price before any subscriber price: the bundle's price, or the
/// single price times the quantity.</param>
/// <param name="Payable">What is actually charged, before tax.</param>
/// <param name="SubscriberPrice">True when the paid-subscriber percentage was applied.</param>
/// <param name="Bundle">The bundle bought, when one was.</param>
public sealed record EventCreditQuote(
    int Quantity, decimal FullPrice, decimal Payable, bool SubscriberPrice, EventCreditBundle? Bundle);

/// <summary>
/// The arithmetic of event-credit prices (10/21/2026): the full price, the paid-subscriber price,
/// and bundles. Pure, so every rule here is pinned by a test with no database.
/// </summary>
/// <remarks>
/// <para><b>Ben's rules.</b> A credit is bought individually, by anyone. A group with an active paid
/// subscription pays a percentage of the price, 20 unless the SuperAdmin says otherwise — $99 becomes
/// $19.80. Bundles are "several credits for one price", and the subscriber percentage applies to them
/// as well.</para>
///
/// <para><b>Rounded once, to the cent, half away from zero</b> — the convention every other price on
/// the site uses (see <c>TourBilling</c>). The percentage is applied to the whole purchase, never to
/// one credit and then multiplied, so a cent is rounded once rather than onto every credit.</para>
/// </remarks>
public static class EventCreditPricing
{
    /// <summary>What a paid subscriber pays when the setting is unset: 20% of the full price.</summary>
    public const decimal DefaultSubscriberPercent = 20m;

    /// <summary>The most bundle offers the settings page accepts.</summary>
    public const int MaximumBundles = 6;

    /// <summary>Money rounded the way the site rounds money.</summary>
    public static decimal Cents(decimal amount) => Math.Round(amount, 2, MidpointRounding.AwayFromZero);

    /// <summary>A price at a subscriber's percentage: 99 at 20 is 19.80.</summary>
    public static decimal AtPercent(decimal price, decimal percent) => Cents(price * percent / 100m);

    /// <summary>
    /// Prices one purchase: a bundle when <paramref name="bundle"/> is given, otherwise
    /// <paramref name="quantity"/> single credits.
    /// </summary>
    public static EventCreditQuote Quote(
        decimal unitPrice, int quantity, EventCreditBundle? bundle, bool isSubscriber, decimal subscriberPercent)
    {
        var count = bundle?.Quantity ?? Math.Max(1, quantity);
        var full = bundle is not null ? Cents(bundle.Price) : Cents(unitPrice * count);
        var payable = isSubscriber ? AtPercent(full, subscriberPercent) : full;
        return new EventCreditQuote(count, full, payable, isSubscriber, bundle);
    }

    /// <summary>
    /// Shares a total across the credits it bought, to the cent, so the prices on the credits add up
    /// to exactly what was paid. Any odd cents go on the first credits.
    /// </summary>
    /// <remarks>
    /// Three for $250 is $83.34, $83.33 and $83.33, not three of $83.33 that lose a cent. A refund of
    /// one credit then gives back what that credit actually cost.
    /// </remarks>
    public static decimal[] Share(decimal total, int count)
    {
        if (count <= 0) return [];
        var cents = (long)Math.Round(total * 100m, MidpointRounding.AwayFromZero);
        var each = cents / count;
        var odd = cents - each * count;
        return [.. Enumerable.Range(0, count).Select(i => (each + (i < odd ? 1 : 0)) / 100m)];
    }

    /// <summary>The subscriber percentage a stored setting means, or null when it is not a usable one.</summary>
    public static decimal? ParsePercent(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        var text = raw.Trim().TrimEnd('%').Trim();
        return decimal.TryParse(text, NumberStyles.Number, CultureInfo.InvariantCulture, out var p)
               && p >= 1m && p <= 100m
            ? p
            : null;
    }

    /// <summary>Why a typed percentage cannot be saved, or null when it can (empty clears it).</summary>
    public static string? WhyNotAPercent(string? raw)
        => string.IsNullOrWhiteSpace(raw) || ParsePercent(raw) is not null
            ? null
            : "The paid-subscriber price is a percentage of the full price, from 1 to 100 — for example 20.";

    private static readonly Regex BundleLine = new(
        @"^\s*(?<q>\d+)\s*(?:=|:|for)\s*\$?\s*(?<p>\d+(?:\.\d{1,2})?)\s*$",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    /// <summary>
    /// Reads the bundle setting: one offer per line (or separated by commas or semicolons), each
    /// written <c>quantity = price</c>.
    /// </summary>
    /// <param name="problem">The first thing wrong with it, said so the SuperAdmin can fix it.</param>
    /// <returns>The offers in order of size; empty when there are none or the setting is unusable.</returns>
    /// <remarks>
    /// An unusable setting offers no bundles rather than some of them: a half-read list is a price
    /// list nobody wrote. The settings page refuses to save one, so this is only the fallback.
    /// </remarks>
    public static IReadOnlyList<EventCreditBundle> ParseBundles(string? raw, out string? problem)
    {
        problem = null;
        if (string.IsNullOrWhiteSpace(raw)) return [];

        var bundles = new List<EventCreditBundle>();
        foreach (var piece in raw.Split(['\n', '\r', ',', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var match = BundleLine.Match(piece);
            if (!match.Success
                || !int.TryParse(match.Groups["q"].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var quantity)
                || !decimal.TryParse(match.Groups["p"].Value, NumberStyles.Number, CultureInfo.InvariantCulture, out var price))
            {
                problem = $"\"{piece}\" isn't a bundle. Write each one as how many = price, for example 3 = 249.";
                return [];
            }

            if (quantity < 2 || quantity > EventCredits.MaximumPerPurchase)
            {
                problem = $"A bundle is for 2 to {EventCredits.MaximumPerPurchase} credits; \"{piece}\" is for {quantity}.";
                return [];
            }

            if (price <= 0m)
            {
                problem = $"\"{piece}\" has no price. A bundle must cost something.";
                return [];
            }

            if (bundles.Any(b => b.Quantity == quantity))
            {
                problem = $"There are two bundles for {quantity} credits. Keep one.";
                return [];
            }

            bundles.Add(new EventCreditBundle(quantity, Cents(price)));
        }

        if (bundles.Count > MaximumBundles)
        {
            problem = $"There are {bundles.Count} bundles; the most is {MaximumBundles}.";
            return [];
        }

        return [.. bundles.OrderBy(b => b.Quantity)];
    }

    /// <summary>Why the typed bundle list cannot be saved, or null when it can.</summary>
    public static string? WhyNotBundles(string? raw)
    {
        ParseBundles(raw, out var problem);
        return problem;
    }
}

/// <summary>The event-credit offer as the settings say it today.</summary>
/// <param name="OnSale">The switch is on and the price is above zero.</param>
/// <param name="UnitPrice">One credit at the full price.</param>
/// <param name="SubscriberPercent">What a paid subscriber pays, as a percentage of the full price.</param>
/// <param name="Bundles">The bundle offers, smallest first.</param>
public sealed record EventCreditOffer(
    bool OnSale, decimal UnitPrice, decimal SubscriberPercent, IReadOnlyList<EventCreditBundle> Bundles)
{
    /// <summary>One credit at the paid-subscriber price.</summary>
    public decimal SubscriberUnitPrice => EventCreditPricing.AtPercent(UnitPrice, SubscriberPercent);

    /// <summary>
    /// Reads the offer from the settings, with the same fallbacks everywhere: unset switch is on,
    /// unset or nonsense price is the standard one, unset percentage is 20.
    /// </summary>
    public static async Task<EventCreditOffer> ReadAsync(BenDataContext db, CancellationToken ct)
    {
        var enabled = await SiteSettingsService.GetBoolAsync(db, SiteSettingKeys.EventCreditsEnabled, whenUnset: true, ct);

        var rawPrice = await SiteSettingsService.GetAsync(db, SiteSettingKeys.EventCreditPriceUsd, ct);
        var price = decimal.TryParse(rawPrice, NumberStyles.Any, CultureInfo.InvariantCulture, out var p) && p > 0m
            ? p
            : EventCredits.DefaultPriceUsd;

        var percent = EventCreditPricing.ParsePercent(
                          await SiteSettingsService.GetAsync(db, SiteSettingKeys.EventCreditSubscriberPercent, ct))
                      ?? EventCreditPricing.DefaultSubscriberPercent;

        var bundles = EventCreditPricing.ParseBundles(
            await SiteSettingsService.GetAsync(db, SiteSettingKeys.EventCreditBundles, ct), out _);

        return new EventCreditOffer(enabled && price > 0m, price, percent, bundles);
    }

    /// <summary>
    /// Whether this group pays the subscriber price: it holds an active paid subscription, either an
    /// investigation plan or the Ghost Walk Tour subscription, whose paid period has not run out.
    /// </summary>
    /// <remarks>
    /// Paid through, not merely present — the rule <c>PaidPlan</c> states: a Lapsed subscription is not
    /// a paid plan, or letting one expire would be a way to keep what it bought.
    /// </remarks>
    public static async Task<bool> IsPaidSubscriberAsync(
        BenDataContext db, Guid organizationId, DateTime now, CancellationToken ct)
        => await db.OrganizationSubscriptions.AsNoTracking().AnyAsync(
               s => s.OrganizationId == organizationId
                 && s.Status == SubscriptionStatus.Active
                 && s.CurrentPeriodEnd != null && s.CurrentPeriodEnd > now, ct)
        || await db.GhostWalkSubscriptions.AsNoTracking().AnyAsync(
               s => s.OrganizationId == organizationId
                 && s.Status == SubscriptionStatus.Active
                 && s.CurrentPeriodEnd != null && s.CurrentPeriodEnd > now, ct);

    /// <summary>The bundle for exactly this many credits, if one is offered.</summary>
    public EventCreditBundle? BundleFor(int quantity) => Bundles.FirstOrDefault(b => b.Quantity == quantity);
}
