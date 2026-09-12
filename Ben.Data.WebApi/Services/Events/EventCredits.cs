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

    /// <summary>
    /// How long before an event starts that calling it off still returns the credit.
    /// </summary>
    /// <remarks>
    /// Ben, 2026-09-12: <i>"Maybe up to 48 hours before event?"</i> Overridable per site; this is
    /// what an unset setting means.
    /// </remarks>
    public static readonly TimeSpan CancellationWindow = TimeSpan.FromHours(48);

    /// <summary>The credit that was spent on this event, if one was.</summary>
    /// <remarks>
    /// An event on a plan slot spent nothing, and one published before credits existed spent
    /// nothing either, so a null here is ordinary rather than a fault.
    /// </remarks>
    public static Task<EventCredit?> SpentOnAsync(
        BenDataContext db, Guid hostedEventId, CancellationToken ct)
        => db.EventCredits.FirstOrDefaultAsync(
               c => c.SpentOnHostedEventId == hostedEventId && c.SpentUtc != null, ct);

    /// <summary>
    /// Puts a spent credit back in the holder's pocket. The caller saves.
    /// </summary>
    /// <remarks>
    /// <para><b>Un-spent, not refunded.</b> Ben, 2026-09-12: <i>"If it ends up getting cancelled,
    /// we should refund the credit"</i> and <i>"We don't refund money, only credit."</i> So
    /// <c>RefundedUtc</c> — which means the money went back and the credit is gone — is deliberately
    /// not touched. Clearing where it was spent is what makes it spendable again.</para>
    ///
    /// <para><b>Its expiry is not extended.</b> A credit bought last October still lapses next
    /// October whatever happened to the event it was spent on, because the year is what was sold.
    /// A credit that comes back already lapsed is gone, and the cancel screen says so rather than
    /// implying something is waiting that is not.</para>
    /// </remarks>
    public static void Unspend(EventCredit credit, Guid userId, DateTime now)
    {
        credit.SpentUtc = null;
        credit.SpentOnHostedEventId = null;
        credit.DateUpdated = now;
        credit.UpdatedByAppUserId = userId;
    }

    /// <summary>
    /// Whether calling this event off now returns its credit, and the sentence either way.
    /// </summary>
    /// <param name="startsUtc">When the event's first date begins, on the venue's own clock.</param>
    /// <remarks>
    /// <para>A window rather than "always" because a venue that cancels the morning of has already
    /// had the benefit: the event was advertised, it took bookings, and people arranged their
    /// weekend around it. A window rather than "never" because plans change and forty-eight hours
    /// is enough notice that nobody has set off.</para>
    ///
    /// <para>Returns a sentence in both cases, because the cancel screen has to say which is about
    /// to happen BEFORE somebody presses the button. Finding out afterwards that ninety-nine
    /// dollars did not come back is the conversation this exists to avoid.</para>
    /// </remarks>
    public static (bool Returns, string Sentence) WhatCancellingDoesToTheCredit(
        EventCredit? spent, DateTime startsUtc, DateTime now, TimeSpan window)
    {
        if (spent is null)
            return (false, "No event credit was spent on this one, so there is nothing to come back.");

        var deadline = startsUtc - window;
        if (now > deadline)
            return (false,
                $"It is less than {Describe(window)} before this event starts, so the event credit "
                + "spent on it does not come back.");

        return (true,
            spent.ExpiresUtc <= now
                ? "The event credit spent on this comes back, but it has already lapsed, so there "
                + "is nothing left to spend."
                : $"The event credit spent on this comes back, and can be spent again until "
                + $"{spent.ExpiresUtc:MM/dd/yyyy}.");
    }

    /// <summary>A window as somebody would say it: "48 hours", "2 days".</summary>
    private static string Describe(TimeSpan window)
        => window.TotalHours >= 48 && window.TotalHours % 24 == 0
            ? $"{window.TotalDays:0} days"
            : $"{window.TotalHours:0} hours";

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
