using Ben.Data.Source.Context;
using Ben.Data.Source.Entities;
using Microsoft.EntityFrameworkCore;

namespace Ben.Data.WebApi.Services.Events;

/// <summary>
/// Holding, spending and expiring event credits (item 235).
/// </summary>
/// <remarks>
/// <para>One credit buys one event. Ben set the price at $99 on 2026-09-11 and it lives in a site
/// setting rather than a constant, so it moves without a deployment — and because each credit
/// freezes what was paid for it, moving the price never reaches one somebody already holds.</para>
///
/// <para>Every rule about a credit is here rather than in the controller that happens to need it,
/// because "which one gets spent" has exactly one right answer and three places asking it
/// separately would eventually disagree.</para>
/// </remarks>
public static class EventCredits
{
    /// <summary>What a credit costs when nobody has said otherwise.</summary>
    /// <remarks>
    /// Ben's number. A fraction of the per-ticket cut a ticketing platform takes on a forty-guest
    /// weekend, and a low single-digit percentage of what the host collects.
    /// </remarks>
    public const decimal DefaultPriceUsd = 99m;

    /// <summary>How long an unspent credit lasts.</summary>
    public static readonly TimeSpan Life = TimeSpan.FromDays(365);

    /// <summary>How long before it lapses the holder is warned.</summary>
    /// <remarks>
    /// Ben, 2026-09-11: "30-day warning sounds reasonable." It costs nothing to send and saves the
    /// support ticket that arrives when somebody finds out a credit expired last week.
    /// </remarks>
    public static readonly TimeSpan WarningLead = TimeSpan.FromDays(30);

    /// <summary>The most anybody may buy in one go, so a typo is not a four-figure charge.</summary>
    public const int MaximumPerPurchase = 20;

    /// <summary>Every credit this owner holds, spent and unspent, newest purchase first.</summary>
    public static Task<List<EventCredit>> HeldByAsync(
        BenDataContext db, Guid? organizationId, Guid? appUserId, CancellationToken ct)
        => db.EventCredits.AsNoTracking()
            .Where(c => organizationId != null
                ? c.OwnerOrganizationId == organizationId
                : c.OwnerAppUserId == appUserId)
            .OrderByDescending(c => c.PurchasedUtc)
            .ToListAsync(ct);

    /// <summary>How many this owner could spend right now.</summary>
    public static Task<int> SpendableCountAsync(
        BenDataContext db, Guid? organizationId, Guid? appUserId, DateTime now, CancellationToken ct)
        => Spendable(db, organizationId, appUserId, now).CountAsync(ct);

    /// <summary>
    /// The credit that should be spent next: the oldest one still usable.
    /// </summary>
    /// <remarks>
    /// <b>Oldest first, and it matters.</b> Spending the newest would let the oldest lapse while
    /// its owner was actively using the site, which is somebody's money quietly thrown away. This
    /// is the order the test pins.
    /// </remarks>
    public static Task<EventCredit?> NextToSpendAsync(
        BenDataContext db, Guid? organizationId, Guid? appUserId, DateTime now, CancellationToken ct)
        => Spendable(db, organizationId, appUserId, now)
            .OrderBy(c => c.ExpiresUtc)
            .ThenBy(c => c.PurchasedUtc)
            .FirstOrDefaultAsync(ct);

    private static IQueryable<EventCredit> Spendable(
        BenDataContext db, Guid? organizationId, Guid? appUserId, DateTime now)
        => db.EventCredits
            .Where(c => (organizationId != null
                    ? c.OwnerOrganizationId == organizationId
                    : c.OwnerAppUserId == appUserId)
                && c.SpentUtc == null
                && c.RefundedUtc == null
                && c.ExpiresUtc > now);

    /// <summary>
    /// Why this credit cannot cover this event, or null when it can.
    /// </summary>
    /// <remarks>
    /// <b>The year is checked at both ends.</b> The credit must be unexpired, and the event's first
    /// date must fall inside the credit's year — which is Ben's "a year to have hosted the event"
    /// read literally, and stops a credit being parked by publishing a placeholder dated years out.
    /// The refusal names both dates, because "that won't work" sends somebody to support and
    /// "expires 14 March, your event is 2 April" sends them to the right button.
    /// </remarks>
    public static string? WhyItCannotCover(EventCredit credit, HostedEvent hostedEvent, DateTime now)
    {
        if (credit.SpentUtc is not null) return "That credit has already been used.";
        if (credit.RefundedUtc is not null) return "That credit was refunded.";

        if (credit.ExpiresUtc <= now)
            return $"That credit ran out on {credit.ExpiresUtc:MM/dd/yyyy}.";

        if (hostedEvent.StartsOn.Date > credit.ExpiresUtc.Date)
            return $"That credit runs out on {credit.ExpiresUtc:MM/dd/yyyy} and this event starts "
                 + $"on {hostedEvent.StartsOn:MM/dd/yyyy}. A credit bought today would cover it.";

        return null;
    }

    /// <summary>
    /// Marks a credit as spent on an event. The caller saves.
    /// </summary>
    /// <remarks>
    /// Not saved here on purpose: spending the credit and publishing the event are one act, and
    /// half of it landing is the worst outcome available — a credit gone and nothing live.
    /// </remarks>
    public static void Spend(EventCredit credit, HostedEvent hostedEvent, Guid userId, DateTime now)
    {
        credit.SpentUtc = now;
        credit.SpentOnHostedEventId = hostedEvent.Id;
        credit.DateUpdated = now;
        credit.UpdatedByAppUserId = userId;
    }

    /// <summary>Credits about to lapse whose holder has not been warned.</summary>
    public static Task<List<EventCredit>> DueAWarningAsync(
        BenDataContext db, DateTime now, CancellationToken ct)
        => db.EventCredits
            .Where(c => c.SpentUtc == null
                     && c.RefundedUtc == null
                     && c.ExpiryWarningSentUtc == null
                     && c.ExpiresUtc > now
                     && c.ExpiresUtc <= now.Add(WarningLead))
            .ToListAsync(ct);
}
