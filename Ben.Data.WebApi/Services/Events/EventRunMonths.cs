using System.Globalization;
using Ben.Data.Common.Enums;
using Ben.Data.Source.Context;
using Ben.Data.Source.Entities;
using Microsoft.EntityFrameworkCore;

namespace Ben.Data.WebApi.Services.Events;

/// <summary>
/// Extra months for a show's run (10/24/2026). See <see cref="EventRunMonth"/> for Ben's rules.
/// </summary>
/// <remarks>
/// <para>One credit covers a show's run up to a month from its first performance; each extra month applied
/// to the show adds a calendar month. A stay (days in a row) never takes one.</para>
///
/// <para><b>Never a credit.</b> Nothing here is read by publishing, and nothing in <c>EventCredits</c> is read
/// here.</para>
/// </remarks>
public static class EventRunMonths
{
    public const decimal DefaultPercent = 50m;
    public const int DefaultCap = 11;
    public const int HighestCap = 36;

    /// <summary>How long a month waits, held, before it lapses: a year from purchase, as credits.</summary>
    public static readonly TimeSpan Life = TimeSpan.FromDays(365);

    /// <summary>The months counting toward this show's run: on it, and not refunded.</summary>
    public static Task<int> AppliedToAsync(BenDataContext db, Guid hostedEventId, CancellationToken ct)
        => db.EventRunMonths.CountAsync(m => m.AppliedToHostedEventId == hostedEventId && m.RefundedUtc == null, ct);

    /// <summary>The run's limit: performances must fall before this date.</summary>
    public static DateTime LimitFor(DateTime firstPerformance, int months) => firstPerformance.Date.AddMonths(1 + months);

    /// <summary>
    /// Why this show can't take extra months now, or null when it can. Shared by buying and by applying held
    /// months, so both say the same thing.
    /// </summary>
    /// <param name="lastEndsUtc">When the show's last performance ends, by its own window rule.</param>
    public static string? WhyNotExtend(HostedEvent show, int alreadyApplied, int adding, int cap, DateTime lastEndsUtc, DateTime now)
    {
        if (!show.DatesAreSeparate)
            return "Extra months are for a show's run. A weekend or a stay of days in a row is one event; another is its own event.";
        if (HostedEventStates.CalledOff.Contains(show.LifecycleState))
            return "This show has been called off, so it can't take extra months.";
        if (show.LifecycleState is not (HostedEventLifecycleState.Published or HostedEventLifecycleState.Live))
            return show.LifecycleState == HostedEventLifecycleState.Draft
                ? "Publish the show first. Its credit covers the first month; extra months come after."
                : "This show is over, so it can't take extra months.";
        if (now >= lastEndsUtc)
            return "This show is over, so it can't take extra months.";
        if (adding < 1)
            return "Choose at least one month.";
        if (alreadyApplied + adding > cap)
            return alreadyApplied >= cap
                ? $"This show already has {cap} extra months, the most one show can have."
                : $"This show has {alreadyApplied} extra month{(alreadyApplied == 1 ? "" : "s")}, and one show can have {cap}. "
                  + $"Choose {cap - alreadyApplied} or fewer.";
        return null;
    }

    /// <summary>The percentage a stored setting means, or null when it is not a usable one.</summary>
    public static decimal? ParsePercent(string? raw) => EventCreditPricing.ParsePercent(raw);

    public static string? WhyNotAPercent(string? raw)
        => string.IsNullOrWhiteSpace(raw) || ParsePercent(raw) is not null
            ? null
            : "An extra month is a percentage of the buyer's credit price, from 1 to 100 — for example 50.";

    public static int? ParseCap(string? raw)
        => int.TryParse(raw?.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var n) && n is >= 1 and <= HighestCap
            ? n
            : null;

    public static string? WhyNotACap(string? raw)
        => string.IsNullOrWhiteSpace(raw) || ParseCap(raw) is not null
            ? null
            : $"Write the most extra months one show may have as a whole number from 1 to {HighestCap} — for example 11.";

    /// <summary>The settings as they stand: the percentage and the cap, with the same fallbacks everywhere.</summary>
    public static async Task<(decimal Percent, int Cap)> ReadAsync(BenDataContext db, CancellationToken ct)
    {
        var percent = ParsePercent(await SiteSettingsService.GetAsync(db, SiteSettingKeys.EventRunMonthPercent, ct)) ?? DefaultPercent;
        var cap = ParseCap(await SiteSettingsService.GetAsync(db, SiteSettingKeys.EventRunMonthsPerShow, ct)) ?? DefaultCap;
        return (percent, cap);
    }

    /// <summary>One extra month's price for this buyer: the percentage of what they pay for a credit, to the cent.</summary>
    public static decimal PriceOfAMonth(EventCreditOffer offer, bool subscriber, decimal percent)
        => EventCreditPricing.AtPercent(subscriber ? offer.SubscriberUnitPrice : offer.UnitPrice, percent);

    /// <summary>Held months lapsing within the warning lead that haven't been warned about.</summary>
    public static Task<List<EventRunMonth>> DueAWarningAsync(BenDataContext db, DateTime now, CancellationToken ct)
        => db.EventRunMonths
            .Where(m => m.AppliedToHostedEventId == null && m.RefundedUtc == null && m.ExpiryWarningSentUtc == null
                     && m.ExpiresUtc > now && m.ExpiresUtc <= now.Add(EventCredits.WarningLead))
            .ToListAsync(ct);

    /// <summary>
    /// Takes every month off a show and holds it again for whoever paid. Called whenever a show is called
    /// off, whatever the timing (Ben, 10/24/2026: they always come back). The caller saves.
    /// </summary>
    /// <returns>How many came back.</returns>
    public static async Task<int> ReturnFromAsync(BenDataContext db, Guid hostedEventId, Guid userId, DateTime now, CancellationToken ct)
    {
        var months = await db.EventRunMonths
            .Where(m => m.AppliedToHostedEventId == hostedEventId && m.RefundedUtc == null)
            .ToListAsync(ct);
        foreach (var m in months)
        {
            m.AppliedToHostedEventId = null;
            m.AppliedUtc = null;
            m.DateUpdated = now;
            m.UpdatedByAppUserId = userId;
        }
        return months.Count;
    }
}
