using Ben.Data.Common.Enums;
using Ben.Data.Source.Entities;

namespace Ben.Data.WebApi.Services.Billing;

/// <summary>
/// Reads a band's price for a cadence, and the arithmetic around offering a yearly discount.
/// </summary>
/// <remarks>
/// Separate from <see cref="Ben.Data.Source.Services.SubscriptionTierResolver"/>, which answers
/// "which band?" — a different
/// question with different inputs. Splitting them keeps the band-tiling rules testable without a
/// price list and the price arithmetic testable without member counts.
/// </remarks>
public static class SubscriptionPricing
{
    /// <summary>How many months one period at <paramref name="interval"/> covers.</summary>
    /// <remarks>
    /// The enum's values <i>are</i> the month counts, so this is a cast. It exists as a named
    /// method anyway because <c>(int)interval</c> at a call site reads like a database id.
    /// </remarks>
    public static int MonthsIn(BillingInterval interval) => (int)interval;

    /// <summary>When a period starting at <paramref name="start"/> runs out.</summary>
    /// <remarks>
    /// <c>AddMonths</c> rather than adding days: a yearly subscription starting on 31 January must
    /// renew on 31 January, and 365 days gets that wrong every leap year. <c>AddMonths</c> also
    /// clamps 31 January + 1 month to 28 February rather than overflowing into March.
    /// </remarks>
    public static DateTime PeriodEnd(DateTime start, BillingInterval interval) =>
        start.AddMonths(MonthsIn(interval));

    /// <summary>
    /// What this band costs at this cadence, or null when it is not sold that way.
    /// </summary>
    /// <remarks>
    /// Null is a real answer and not a failure — a free band billed yearly is meaningless, and an
    /// introductory band may be monthly only. Callers offer the cadences that come back non-null
    /// rather than offering all four and failing at checkout.
    /// </remarks>
    public static decimal? PriceFor(SubscriptionTier tier, BillingInterval interval) =>
        tier.Prices
            .Where(p => p.IsActive && p.Interval == interval)
            .Select(p => (decimal?)p.Price)
            .FirstOrDefault();

    /// <summary>The cadences this band is actually sold at, cheapest cadence first.</summary>
    public static IReadOnlyList<BillingInterval> AvailableIntervals(SubscriptionTier tier) =>
        [.. tier.Prices.Where(p => p.IsActive).Select(p => p.Interval).Distinct().OrderBy(i => (int)i)];

    /// <summary>
    /// What twelve months at <paramref name="interval"/> would cost, for comparing cadences.
    /// </summary>
    /// <remarks>
    /// Yearly is the whole point of the comparison, so this is what makes "$150 a year" and "$15 a
    /// month" comparable at all. Not a price to charge anybody — it is a display figure.
    /// </remarks>
    public static decimal AnnualisedCost(decimal periodPrice, BillingInterval interval) =>
        Math.Round(periodPrice * 12m / MonthsIn(interval), 2, MidpointRounding.AwayFromZero);

    /// <summary>
    /// How much cheaper this cadence is than paying monthly, as a percentage. Null when there is
    /// no monthly price to compare against, or when monthly is free.
    /// </summary>
    /// <remarks>
    /// <para>This is the "save 17%" on the pricing page, and it is <b>derived</b> rather than
    /// stored. Ben asked for a percent discount on yearly; storing both the percentage and the
    /// yearly price would give the same figure two homes, and the two would drift the first time
    /// somebody edited one of them. The editor's "make yearly N% off" button writes the price and
    /// this reads the discount back out — one number, stored once.</para>
    ///
    /// <para>Rounds toward zero so a saving is never overstated. 16.7% shown as 17% is a claim
    /// that is not quite true, and on a price it is the kind that gets noticed.</para>
    /// </remarks>
    public static int? SavingPercentAgainstMonthly(SubscriptionTier tier, BillingInterval interval)
    {
        if (interval == BillingInterval.Monthly) return null;

        var monthly = PriceFor(tier, BillingInterval.Monthly);
        var here    = PriceFor(tier, interval);

        if (monthly is not > 0 || here is null) return null;

        var payingMonthly = monthly.Value * MonthsIn(interval);
        if (here.Value >= payingMonthly) return null;

        return (int)Math.Floor((payingMonthly - here.Value) * 100m / payingMonthly);
    }

    /// <summary>
    /// The price a "make this N% off paying monthly" button should write into a cadence's row.
    /// </summary>
    /// <remarks>
    /// The inverse of <see cref="SavingPercentAgainstMonthly"/>, and the reason the discount does
    /// not need storing: the SuperAdmin thinks in percentages, the database holds a price, and this
    /// is the one line that converts between them.
    /// </remarks>
    public static decimal PriceForSaving(decimal monthlyPrice, BillingInterval interval, int percentOff) =>
        Math.Round(
            monthlyPrice * MonthsIn(interval) * (100 - percentOff) / 100m,
            2, MidpointRounding.AwayFromZero);
}
