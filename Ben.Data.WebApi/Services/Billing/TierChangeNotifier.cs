using Ben.Data.Common.Enums;
using Ben.Data.Source.Context;
using Ben.Data.Source.Entities;
using Microsoft.EntityFrameworkCore;

namespace Ben.Data.WebApi.Services.Billing;

/// <summary>What one tier edit will do — or did — to the groups on it.</summary>
/// <param name="Changes">Every change, classified. Empty means the edit touched no terms.</param>
/// <param name="GroupsMessagedNow">Groups told immediately: free-band groups, and paid ones when every change is an improvement.</param>
/// <param name="PaidGroupsNoticed">Paid groups whose reductions were queued for delivery before their renewal.</param>
public sealed record TierImpact(
    IReadOnlyList<TierChange> Changes,
    int GroupsMessagedNow,
    int PaidGroupsNoticed);

/// <summary>
/// The fan-out from a tier edit: who hears now, who hears before renewal, and what they hear.
/// </summary>
/// <remarks>
/// <para>The delivery rules are Ben's contract semantics restated as messaging:</para>
/// <list type="bullet">
/// <item><b>Improvements go out immediately</b> — the resolver applies them immediately, and the
/// message and the experience must change together.</item>
/// <item><b>Reductions for paid groups are queued</b>, to arrive two weeks before the renewal that
/// applies them — floored at now, because a monthly period may already be inside the window.</item>
/// <item><b>Free-band groups hear everything immediately</b>: nothing was paid, nothing is held,
/// so the change is already live for them whichever direction it goes.</item>
/// </list>
///
/// <para><see cref="PreviewAsync"/> and <see cref="ApplyAsync"/> share the classification so the
/// preview a SuperAdmin confirms is exactly the fan-out that then happens — a preview computed by
/// different code is a promise nobody is keeping.</para>
/// </remarks>
public sealed class TierChangeNotifier
{
    /// <summary>The notice window: how far before renewal a reduction is announced.</summary>
    public static readonly TimeSpan NoticeWindow = TimeSpan.FromDays(14);

    private readonly IDbContextFactory<BenDataContext> _dbFactory;
    private readonly PlatformMessageService _messages;

    public TierChangeNotifier(IDbContextFactory<BenDataContext> dbFactory, PlatformMessageService messages)
    {
        _dbFactory = dbFactory;
        _messages  = messages;
    }

    /// <summary>The impact this edit would have, sending nothing.</summary>
    public async Task<TierImpact> PreviewAsync(
        Guid tierId, string tierName, IReadOnlyList<TierChange> changes, CancellationToken ct)
    {
        var (free, paid) = await AffectedAsync(tierId, ct);

        var improvements = changes.Where(c => c.IsImprovement).ToList();
        var reductions   = changes.Where(c => !c.IsImprovement).ToList();

        return new TierImpact(
            changes,
            GroupsMessagedNow: changes.Count == 0 ? 0 : free.Count + (improvements.Count > 0 ? paid.Count : 0),
            PaidGroupsNoticed: reductions.Count > 0 ? paid.Count : 0);
    }

    /// <summary>Sends the immediate messages and queues the notices. Call after the edit is saved.</summary>
    public async Task<TierImpact> ApplyAsync(
        Guid tierId, string tierName, IReadOnlyList<TierChange> changes,
        Guid editorUserId, CancellationToken ct)
    {
        if (changes.Count == 0) return new TierImpact(changes, 0, 0);

        var (free, paid) = await AffectedAsync(tierId, ct);

        var improvements = changes.Where(c => c.IsImprovement).ToList();
        var reductions   = changes.Where(c => !c.IsImprovement).ToList();
        var now          = DateTime.UtcNow;

        var messagedNow = 0;

        // Free-band groups: everything, immediately — the change is already live for them.
        foreach (var sub in free)
        {
            await SendNowAsync(sub.OrganizationId, tierName, changes, editorUserId, ct);
            messagedNow++;
        }

        // Paid groups: the good news now…
        if (improvements.Count > 0)
            foreach (var sub in paid)
            {
                await SendNowAsync(sub.OrganizationId, tierName, improvements, editorUserId, ct);
                messagedNow++;
            }

        // …and the rest queued against each group's own renewal date.
        if (reductions.Count > 0 && paid.Count > 0)
        {
            await using var db = await _dbFactory.CreateDbContextAsync(ct);

            foreach (var sub in paid)
            {
                // A paid group with no period end has been set up by hand and incompletely; the
                // safe reading is "renewal could be any time", which means telling them now.
                var effective = sub.CurrentPeriodEnd ?? now;
                var deliverAt = effective - NoticeWindow < now ? now : effective - NoticeWindow;

                db.TierChangeNotices.Add(new TierChangeNotice
                {
                    Id                 = Guid.NewGuid(),
                    OrganizationId     = sub.OrganizationId,
                    SubscriptionTierId = tierId,
                    Sentences          = string.Join('\n', reductions.Select(r => r.Sentence)),
                    EffectiveAtUtc     = effective,
                    DeliverAtUtc       = deliverAt,
                    DateCreated        = now,
                    CreatedByAppUserId = editorUserId,
                });
            }

            await db.SaveChangesAsync(ct);
        }

        return new TierImpact(changes, messagedNow,
            reductions.Count > 0 ? paid.Count : 0);
    }

    private async Task SendNowAsync(
        Guid organizationId, string tierName, IReadOnlyList<TierChange> changes,
        Guid senderId, CancellationToken ct)
    {
        var recipients = await _messages.BillingRecipientsAsync(organizationId, ct);

        // Free-band groups receive reductions through this immediate path too, so the subject is
        // earned, not assumed: "improved" only when every line is an improvement.
        var allGood = changes.All(c => c.IsImprovement);
        var subject = allGood
            ? $"Your {tierName} plan has improved"
            : $"Your {tierName} plan has changed";

        var body = $"Your group's plan, {tierName}, has changed:\n\n"
                 + string.Join('\n', changes.Select(c => "• " + c.Sentence))
                 + "\n\nThese changes are already in effect.";

        await _messages.SendAsync(subject, body, recipients, senderId, ct);
    }

    private async Task<(List<OrganizationSubscription> Free, List<OrganizationSubscription> Paid)>
        AffectedAsync(Guid tierId, CancellationToken ct)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);

        var subs = await db.OrganizationSubscriptions.AsNoTracking()
            .Where(s => s.SubscriptionTierId == tierId)
            .ToListAsync(ct);

        return (
            [.. subs.Where(s => s.Status == SubscriptionStatus.Free)],
            [.. subs.Where(s => s.Status != SubscriptionStatus.Free)]);
    }
}
