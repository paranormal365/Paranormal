using Ben.Data.Common.Enums;

namespace Ben.Data.Source.Services;

/// <summary>
/// The arithmetic of paying per tour (item 233): how many units a business is billed for, and
/// what a tour added mid-period owes for the rest of it.
/// </summary>
/// <remarks>
/// <para><b>Ben, 2026-09-10:</b> "The $29 per month is for a single tour no matter how many times
/// scheduled." The business tier's price is therefore a <i>unit</i> price, and the quantity is the
/// number of tours the business has not retired. A business with no tours yet is still on the
/// plan — it pays for one, the one it is about to define — so the count floors at one, the way a
/// group with no members still sits in the lowest band.</para>
///
/// <para><b>A tour added mid-period pays for the days left</b>, by whole days over the period's
/// whole days, rounded to the cent and never below nothing. Then the renewal re-counts from the
/// live tours, so the next period's price is simply units × unit price again. Retiring a tour
/// takes effect at renewal; nothing is refunded for the part of a period already paid.</para>
///
/// <para>Pure on purpose: every number here is checked by a test that needs no database.</para>
/// </remarks>
public static class TourBilling
{
    /// <summary>How many units the plan bills: the live tours for a business kind, one for anyone else.</summary>
    public static int Units(OrganizationKind kind, int activeTours)
        => SubscriptionTierResolver.IsBusinessKind(kind) ? Math.Max(1, activeTours) : 1;

    /// <summary>A period's list price: the tier's unit price times the units.</summary>
    public static decimal ListPrice(decimal unitPrice, int units)
        => Math.Round(unitPrice * Math.Max(1, units), 2, MidpointRounding.AwayFromZero);

    /// <summary>
    /// What one more unit owes for the rest of a period that has already been paid for.
    /// </summary>
    /// <param name="unitPrice">One unit for one whole period, at the period's cadence.</param>
    /// <param name="periodStart">When the paid period began.</param>
    /// <param name="periodEnd">When it ends.</param>
    /// <param name="now">The moment the unit is added.</param>
    /// <returns>Zero when the period is over or malformed; otherwise the day-prorated share.</returns>
    public static decimal Remainder(decimal unitPrice, DateTime periodStart, DateTime periodEnd, DateTime now)
    {
        var totalDays = (periodEnd - periodStart).TotalDays;
        if (totalDays <= 0 || now >= periodEnd) return 0m;

        // Whole days remaining, counting today as a day: a tour added at 11pm still ran that
        // evening. Capped at the period so a clock skew cannot bill more than a full unit.
        var daysLeft = Math.Min(Math.Ceiling((periodEnd - now).TotalDays), totalDays);
        if (daysLeft <= 0) return 0m;

        return Math.Round(unitPrice * (decimal)(daysLeft / totalDays), 2, MidpointRounding.AwayFromZero);
    }
}
