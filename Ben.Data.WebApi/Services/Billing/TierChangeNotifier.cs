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

    /// <summary>
    /// How long an area REMOVAL sits in the queue before free-band groups hear about it. Long
    /// enough to absorb an accidental toggle (the checklist saves per click), short enough that
    /// a real downgrade is still announced promptly. Paid groups keep the renewal-window rule,
    /// floored at this same grace.
    /// </summary>
    public static readonly TimeSpan AreaRemovalGrace = TimeSpan.FromMinutes(30);

    /// <summary>
    /// The fan-out from an included-areas edit (item 156 Phase E). Not <see cref="ApplyAsync"/>:
    /// the checklist saves on every toggle, so this path must net a removal against a re-add.
    /// A removal is QUEUED (free groups after <see cref="AreaRemovalGrace"/>, paid groups on the
    /// renewal-window rule); a re-add first cancels that area's pending sentence from any
    /// undelivered notice, and only the areas with nothing left to cancel are announced as
    /// improvements. An uncheck-then-recheck therefore sends nothing at all.
    /// </summary>
    public async Task ApplyAreaChangesAsync(
        Guid tierId, string tierName,
        IReadOnlySet<OrganizationPermissionArea> oldAreas,
        IReadOnlySet<OrganizationPermissionArea> newAreas,
        Guid editorUserId, CancellationToken ct)
    {
        var removed = oldAreas.Except(newAreas).OrderBy(a => (int)a).ToList();
        var added   = newAreas.Except(oldAreas).OrderBy(a => (int)a).ToList();
        if (removed.Count == 0 && added.Count == 0) return;

        var (free, paid) = await AffectedAsync(tierId, ct);
        var now = DateTime.UtcNow;

        await using var db = await _dbFactory.CreateDbContextAsync(ct);

        // ── Re-adds cancel their own pending removal, per org ───────────────────
        // Which added areas still need announcing differs per org: one org's removal notice may
        // already be delivered while another's is still pending.
        var toAnnounce = new Dictionary<Guid, List<OrganizationPermissionArea>>();
        if (added.Count > 0)
        {
            var orgIds = free.Concat(paid).Select(sub => sub.OrganizationId).ToList();
            var pending = await db.TierChangeNotices
                .Where(n => n.SubscriptionTierId == tierId
                         && n.DeliveredAtUtc == null
                         && orgIds.Contains(n.OrganizationId))
                .ToListAsync(ct);

            foreach (var orgId in orgIds)
            {
                var announce = new List<OrganizationPermissionArea>();
                foreach (var area in added)
                {
                    var sentence = TierChangeAnalyzer.AreaReductionSentence(area);
                    var holder = pending.FirstOrDefault(n => n.OrganizationId == orgId
                        && n.Sentences.Split('\n').Contains(sentence));
                    if (holder is null)
                    {
                        announce.Add(area);
                        continue;
                    }
                    var rest = holder.Sentences.Split('\n').Where(l => l != sentence).ToList();
                    if (rest.Count == 0) db.TierChangeNotices.Remove(holder);
                    else { holder.Sentences = string.Join('\n', rest); holder.DateUpdated = now; }
                }
                if (announce.Count > 0) toAnnounce[orgId] = announce;
            }
        }

        // ── Removals queue one notice per org ───────────────────────────────────
        if (removed.Count > 0)
        {
            var sentences = string.Join('\n', removed.Select(TierChangeAnalyzer.AreaReductionSentence));

            foreach (var sub in free)
                db.TierChangeNotices.Add(new TierChangeNotice
                {
                    Id = Guid.NewGuid(), OrganizationId = sub.OrganizationId,
                    SubscriptionTierId = tierId, Sentences = sentences,
                    EffectiveAtUtc = now,                     // live already — the tier row changed
                    DeliverAtUtc   = now + AreaRemovalGrace,  // …but the grace absorbs a mis-click
                    DateCreated = now, CreatedByAppUserId = editorUserId,
                });

            foreach (var sub in paid)
            {
                var effective = sub.CurrentPeriodEnd ?? now;
                var deliverAt = effective - NoticeWindow < now + AreaRemovalGrace
                    ? now + AreaRemovalGrace
                    : effective - NoticeWindow;
                db.TierChangeNotices.Add(new TierChangeNotice
                {
                    Id = Guid.NewGuid(), OrganizationId = sub.OrganizationId,
                    SubscriptionTierId = tierId, Sentences = sentences,
                    EffectiveAtUtc = effective, DeliverAtUtc = deliverAt,
                    DateCreated = now, CreatedByAppUserId = editorUserId,
                });
            }
        }

        await db.SaveChangesAsync(ct);

        // ── Only the un-netted additions are announced, immediately ─────────────
        foreach (var (orgId, areas) in toAnnounce)
            await SendNowAsync(orgId, tierName,
                [.. areas.Select(a => new TierChange(true, TierChangeAnalyzer.AreaImprovementSentence(a)))],
                editorUserId, ct);
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
