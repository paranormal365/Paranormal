using Ben.Data.Common.Enums;
using Ben.Data.Source.Context;
using Ben.Data.Source.Entities;
using Ben.Data.Source.Services;
using Ben.Data.WebApi.Services.Billing;
using Microsoft.EntityFrameworkCore;

namespace Ben.Data.WebApi.Services.Events;

/// <summary>
/// Whether this organization may put another event live, and what it will cost them (item 235).
/// </summary>
/// <remarks>
/// <para><b>Every event is bought (Ben, 10/21/2026).</b> "Events are not part of subscriptions.
/// Investigations are part of subscriptions. Events are their own beast and not included in any of
/// them." Before this date a plan carrying the <see cref="TierCapability.HostEvents"/> capability —
/// which, capabilities being exclusions, was every paid plan — published events against a cap
/// instead of a credit. That path is gone: no plan, the top band included, publishes an event. A
/// subscription buys a lower price for a credit (<see cref="EventCreditOffer"/>), never the event.</para>
///
/// <para><b>Anyone can hold the credit.</b> "Events may be purchased by anyone": a group buys credits
/// for itself, and a person can buy them for the events they organize. Publishing spends the group's
/// own credit first and, when it has none, one held by the person pressing the button.</para>
///
/// <para><b>Publishing is the moment</b>, not creating. A draft has no page, takes no bookings and
/// sends no confirmations, so it costs nothing and can be abandoned freely; publishing is the act
/// that lets other people's answers start arriving. And an event that has been published once is
/// paid for for ever — taking it down and putting it back up must not charge twice, which is what
/// <see cref="Ben.Data.Source.Entities.HostedEvent.FirstPublishedUtc"/> is for. That is also what keeps
/// every event published under a plan before 10/21/2026 live and free to put back up.</para>
/// </remarks>
public sealed class HostedEventEntitlement
{
    /// <param name="limits">
    /// No longer asked: the plan's event cap went with plans that included events. Kept so the
    /// container and the fixtures that build this need not change.
    /// </param>
    public HostedEventEntitlement(SubscriptionLimitGuard limits) => _ = limits;

    /// <summary>
    /// How an organization's next event will be paid for, and whether it can be.
    /// </summary>
    /// <param name="Kind">Always <see cref="EntitlementKind.Credit"/> now; kept so the shape is stable.</param>
    /// <param name="Refusal">
    /// The sentence to show, or null when publishing may go ahead. Always a sentence a person can
    /// act on — what is in the way, and what would clear it.
    /// </param>
    /// <param name="LiveNow">Events already live.</param>
    /// <param name="Ceiling">Always null: there is no plan cap any more.</param>
    /// <param name="CreditsAvailable">Unspent, unexpired credits the group holds, plus the asker's own.</param>
    public sealed record Verdict(
        EntitlementKind Kind,
        string? Refusal,
        int LiveNow,
        int? Ceiling,
        int CreditsAvailable)
    {
        /// <summary>Whether an event may go live right now.</summary>
        public bool MayPublish => Refusal is null;

        /// <summary>Whether going live will consume one of the credits held.</summary>
        public bool SpendsACredit => Refusal is null;
    }

    /// <summary>Which way an event is paid for.</summary>
    public enum EntitlementKind
    {
        /// <summary>One credit, one event. Value 1 kept from when a plan was the other answer.</summary>
        Credit = 1,
    }

    /// <summary>Events already live for this organization.</summary>
    public static Task<int> LiveEventsAsync(BenDataContext db, Guid organizationId, CancellationToken ct)
        => db.HostedEvents.CountAsync(
            e => e.OrganizationId == organizationId
              && HostedEventStates.CountingAgainstTheBand.Contains(e.LifecycleState), ct);

    /// <summary>
    /// Asks the question for one organization, without changing anything.
    /// </summary>
    /// <param name="excludingEventId">An event already published and being re-checked.</param>
    /// <param name="appUserId">
    /// The person asking, whose own credits can also pay. Null counts only the group's.
    /// </param>
    public async Task<Verdict> DescribeAsync(
        BenDataContext db, Guid organizationId, Guid? excludingEventId = null,
        CancellationToken ct = default, Guid? appUserId = null)
    {
        var live = await db.HostedEvents.CountAsync(
            e => e.OrganizationId == organizationId
              && HostedEventStates.CountingAgainstTheBand.Contains(e.LifecycleState)
              && (excludingEventId == null || e.Id != excludingEventId), ct);

        var now = DateTime.UtcNow;
        var credits = await EventCredits.SpendableCountAsync(db, organizationId, appUserId: null, now, ct);
        if (appUserId is { } person)
            credits += await EventCredits.SpendableCountAsync(db, organizationId: null, person, now, ct);

        return new Verdict(
            EntitlementKind.Credit,
            credits > 0 ? null : await NoCreditSentenceAsync(db, ct),
            live,
            Ceiling: null,
            CreditsAvailable: credits);
    }

    /// <summary>
    /// Takes the credit this event should be published against, or says why it cannot be.
    /// </summary>
    /// <remarks>
    /// <para>Called inside the publish, in the same save, so two tabs pressing the button cannot
    /// spend one credit twice — and so a credit can never go missing without an event going live
    /// for it.</para>
    ///
    /// <para><b>The group's credit first, then the publisher's own.</b> A credit the group bought is
    /// the group's money already set aside for this; a person's credit is theirs, and is only reached
    /// for when the group has nothing to spend.</para>
    ///
    /// <para>Returns the credit rather than saving it, because the caller owns the transaction.</para>
    /// </remarks>
    public async Task<(EventCredit? Spent, string? Refusal)> TakeForAsync(
        BenDataContext db, HostedEvent hostedEvent, Guid userId, CancellationToken ct)
    {
        var now = DateTime.UtcNow;
        var credit = await EventCredits.NextToSpendAsync(
                         db, hostedEvent.OrganizationId, appUserId: null, now, ct)
                  ?? await EventCredits.NextToSpendAsync(db, organizationId: null, userId, now, ct);

        if (credit is null) return (null, await NoCreditSentenceAsync(db, ct));

        if (EventCredits.WhyItCannotCover(credit, hostedEvent, now) is { } why)
            return (null, why);

        EventCredits.Spend(credit, hostedEvent, userId, now);
        return (credit, null);
    }

    /// <summary>
    /// What somebody without a credit is told, with today's price in it.
    /// </summary>
    /// <remarks>
    /// It names the price, because a refusal that does not is a refusal somebody has to go and
    /// research — and it reads the price from the settings, so it is never a number the site no
    /// longer charges. Nothing they have built is lost: the event stays a draft.
    /// </remarks>
    public static async Task<string> NoCreditSentenceAsync(BenDataContext db, CancellationToken ct)
        => NoCreditSentence(await EventCreditOffer.ReadAsync(db, ct));

    /// <summary>The no-credit sentence for a given offer.</summary>
    public static string NoCreditSentence(EventCreditOffer offer)
        => "Publishing an event uses one event credit, and none is ready to use. "
         + $"A credit is ${offer.UnitPrice:0.00} (${offer.SubscriberUnitPrice:0.00} for a group with a "
         + "paid plan) and covers one event, however many days in a row it runs. Buy one on the "
         + "Billing page; everything you have built here stays as it is until then.";
}
