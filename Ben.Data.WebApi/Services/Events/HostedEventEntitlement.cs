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
/// <para><b>One place asks the question</b>, because there are two quite different answers and a
/// screen that guessed which applied would be wrong for half the site's customers.</para>
///
/// <para><b>A business that runs events for a living</b> — a haunted property, an events company —
/// is on the flat business plan and its events are governed by a cap on the tier
/// (<see cref="SubscriptionLimit.ActiveHostedEvents"/>). No row means no cap, which is the
/// fail-open default every limit here uses: a forgotten row must never close a door.</para>
///
/// <para><b>Everybody else buys a credit.</b> Ben, 2026-09-11: "one credit buys one event… they have
/// a year to have hosted the event otherwise they lose the credit." Metering an occasional weekend
/// monthly would have priced a hotel weekend as a walk round a block, and would have reached none of
/// the groups that run one fundraiser a year.</para>
///
/// <para><b>Publishing is the moment</b>, not creating. A draft has no page, takes no bookings and
/// sends no confirmations, so it costs nothing and can be abandoned freely; publishing is the act
/// that lets other people's answers start arriving. And an event that has been published once is
/// paid for for ever — taking it down and putting it back up must not charge twice, which is what
/// <see cref="Ben.Data.Source.Entities.HostedEvent.FirstPublishedUtc"/> is for.</para>
/// </remarks>
public sealed class HostedEventEntitlement
{
    private readonly SubscriptionLimitGuard _limits;

    public HostedEventEntitlement(SubscriptionLimitGuard limits) => _limits = limits;

    /// <summary>
    /// How an organization's next event will be paid for, and whether it can be.
    /// </summary>
    /// <param name="Kind">Which of the two answers applies.</param>
    /// <param name="Refusal">
    /// The sentence to show, or null when publishing may go ahead. Always a sentence a person can
    /// act on — what is in the way, and what would clear it.
    /// </param>
    /// <param name="LiveNow">Events already live and counting.</param>
    /// <param name="Ceiling">The cap, when there is one. Null means no cap.</param>
    /// <param name="CreditsAvailable">Unspent, unexpired credits held.</param>
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
        public bool SpendsACredit => Kind == EntitlementKind.Credit && Refusal is null;
    }

    /// <summary>Which way this organization pays for an event.</summary>
    public enum EntitlementKind
    {
        /// <summary>On the flat business plan: events are capped, not bought one at a time.</summary>
        Plan = 0,

        /// <summary>Not on a plan that includes hosting: one credit, one event.</summary>
        Credit = 1,
    }

    /// <summary>Events already live for this organization — the number a cap is measured against.</summary>
    /// <remarks>
    /// Live, not merely existing: published, not archived, not called off. A draft costs nothing
    /// and an event that finished last spring costs nothing, so neither belongs in this count.
    /// </remarks>
    public static Task<int> LiveEventsAsync(BenDataContext db, Guid organizationId, CancellationToken ct)
        => db.HostedEvents.CountAsync(
            e => e.OrganizationId == organizationId
              && e.IsPublished
              && e.ArchivedAtUtc == null
              && e.CancelledAtUtc == null, ct);

    /// <summary>
    /// Asks the question for one organization, without changing anything.
    /// </summary>
    /// <param name="excludingEventId">
    /// An event already published and being re-checked, so it is not counted against its own cap.
    /// </param>
    public async Task<Verdict> DescribeAsync(
        BenDataContext db, Guid organizationId, Guid? excludingEventId = null,
        CancellationToken ct = default)
    {
        var live = await db.HostedEvents.CountAsync(
            e => e.OrganizationId == organizationId
              && e.IsPublished
              && e.ArchivedAtUtc == null
              && e.CancelledAtUtc == null
              && (excludingEventId == null || e.Id != excludingEventId), ct);

        var (onAPlan, _) = await TierAreaResolution.HasCapabilityAsync(
            db, organizationId, TierCapability.HostEvents, ct);

        if (onAPlan)
        {
            // The cap and its wording both come from the guard, so an event's refusal reads like
            // every other refusal on the site and names the tier and the number.
            var refusal = await _limits.WhyNotOneMoreAsync(
                organizationId, SubscriptionLimit.ActiveHostedEvents, live, ct);

            var ceiling = await _limits.ValueOfAsync(
                organizationId, SubscriptionLimit.ActiveHostedEvents, ct);

            return new Verdict(EntitlementKind.Plan, refusal, live, ceiling, 0);
        }

        var credits = await EventCredits.SpendableCountAsync(
            db, organizationId, appUserId: null, DateTime.UtcNow, ct);

        return new Verdict(
            EntitlementKind.Credit,
            credits > 0 ? null : NoCreditSentence,
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
    /// <para>Returns the credit rather than saving it, because the caller owns the transaction.</para>
    /// </remarks>
    public async Task<(EventCredit? Spent, string? Refusal)> TakeForAsync(
        BenDataContext db, HostedEvent hostedEvent, Guid userId, CancellationToken ct)
    {
        var (onAPlan, _) = await TierAreaResolution.HasCapabilityAsync(
            db, hostedEvent.OrganizationId, TierCapability.HostEvents, ct);

        if (onAPlan)
        {
            var live = await db.HostedEvents.CountAsync(
                e => e.OrganizationId == hostedEvent.OrganizationId
                  && e.IsPublished && e.ArchivedAtUtc == null && e.CancelledAtUtc == null
                  && e.Id != hostedEvent.Id, ct);

            var refusal = await _limits.WhyNotOneMoreAsync(
                hostedEvent.OrganizationId, SubscriptionLimit.ActiveHostedEvents, live, ct);

            return (null, refusal);
        }

        var now = DateTime.UtcNow;
        var credit = await EventCredits.NextToSpendAsync(
            db, hostedEvent.OrganizationId, appUserId: null, now, ct);

        if (credit is null) return (null, NoCreditSentence);

        if (EventCredits.WhyItCannotCover(credit, hostedEvent, now) is { } why)
            return (null, why);

        EventCredits.Spend(credit, hostedEvent, userId, now);
        return (credit, null);
    }

    /// <summary>
    /// What somebody without a plan and without a credit is told.
    /// </summary>
    /// <remarks>
    /// It names the price, because a refusal that does not is a refusal somebody has to go and
    /// research. Nothing they have built is lost: the event stays a draft with its dates, its
    /// programme and its page intact, and publishing is the only thing waiting.
    /// </remarks>
    public const string NoCreditSentence =
        "Publishing an event needs an event credit, and your group has none. "
      + "A credit is $99 and covers one event from the day you publish it — everything you have "
      + "built here stays exactly as it is until then.";
}
