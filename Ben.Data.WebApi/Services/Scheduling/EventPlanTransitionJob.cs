using Ben.Data.Common.Enums;
using Ben.Data.Source.Context;
using Ben.Data.Source.Entities;
using Ben.Data.WebApi.Services.Events;
using Microsoft.EntityFrameworkCore;

namespace Ben.Data.WebApi.Services.Scheduling;

/// <summary>
/// The one-time handover when plans stopped including events (Ben, 10/21/2026).
/// </summary>
/// <remarks>
/// <para><b>Who it is for.</b> Until this change every paid plan that had not had hosting switched
/// off published events without a credit. Events such a group already PUBLISHED are untouched by the
/// change — an event published once is paid for for ever (<c>FirstPublishedUtc</c>), so it stays live
/// and can be taken down and put back up for nothing. What would break is the draft a group was
/// building on the understanding that its plan covered it: on the day this ships, that draft would
/// suddenly need a credit the group was never told to buy.</para>
///
/// <para><b>What it does.</b> Once, on its first pass after the deploy: for every never-published
/// draft whose first date is still ahead, in a group whose standing plan included hosting under the
/// old rule, it hands that group one credit with a reason saying why. The credit is a grant — nothing
/// reaches the ledger, exactly like the SuperAdmin's "give an event credit" action — and from then on
/// it behaves like any other: a year to use, spent at publish.</para>
///
/// <para><b>Once means once.</b> It records the moment it ran in a site setting nobody edits, and
/// does nothing ever again after that. A group that subscribes next month never relied on a plan
/// including events, so it is given nothing.</para>
/// </remarks>
public sealed class EventPlanTransitionJob : IScheduledJob
{
    /// <summary>The setting that records the handover has happened. Not in the settings page's list.</summary>
    public const string DoneKey = "events.plan-transition-done-utc";

    /// <summary>The reason written on each credit the handover grants.</summary>
    public const string Reason =
        "Plans no longer include events (10/21/2026). This credit covers an event you were already "
      + "preparing when your plan still did.";

    private readonly IDbContextFactory<BenDataContext> _dbFactory;
    private readonly ILogger<EventPlanTransitionJob> _log;

    public EventPlanTransitionJob(IDbContextFactory<BenDataContext> dbFactory, ILogger<EventPlanTransitionJob> log)
    {
        _dbFactory = dbFactory;
        _log = log;
    }

    public string Name => "event-plan-transition";

    public async Task RunAsync(CancellationToken ct)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        if (await SiteSettingsService.GetAsync(db, DoneKey, ct) is not null) return;

        // The marker row needs an author the database recognizes; the site's first account is as
        // good as any, and a database with no accounts has nothing to hand over.
        var author = await db.Users.AsNoTracking().OrderBy(u => u.DateCreated)
            .Select(u => (Guid?)u.Id).FirstOrDefaultAsync(ct);
        if (author is null) return;

        var now = DateTime.UtcNow;
        var granted = await GrantAsync(db, now, ct);

        db.SiteSettings.Add(new SiteSetting
        {
            Id = Guid.NewGuid(),
            Key = DoneKey,
            Value = now.ToString("O"),
            Description = "When the one-time handover from plan-included events to event credits ran.",
            DateCreated = now,
            CreatedByAppUserId = author.Value,
        });
        await db.SaveChangesAsync(ct);

        _log.LogInformation("Event plan transition: granted {Count} event credit(s) for drafts plans used to cover.", granted);
    }

    /// <summary>Grants the credits, without saving. Returns how many.</summary>
    internal static async Task<int> GrantAsync(BenDataContext db, DateTime now, CancellationToken ct)
    {
        // Groups whose standing plan included hosting: an active, paid-through subscription on a
        // tier that had not had HostEvents excluded.
        var standing = await db.OrganizationSubscriptions.AsNoTracking()
            .Where(s => s.Status == SubscriptionStatus.Active
                     && s.CurrentPeriodEnd != null && s.CurrentPeriodEnd > now
                     && s.SubscriptionTierId != null)
            .Select(s => new { s.OrganizationId, TierId = s.SubscriptionTierId!.Value })
            .ToListAsync(ct);
        if (standing.Count == 0) return 0;

        var tierIds = standing.Select(s => s.TierId).Distinct().ToList();
        var withoutHosting = await db.SubscriptionTierExcludedCapabilities.AsNoTracking()
            .Where(c => tierIds.Contains(c.SubscriptionTierId) && c.Capability == TierCapability.HostEvents)
            .Select(c => c.SubscriptionTierId)
            .ToListAsync(ct);

        var coveredOrgs = standing.Where(s => !withoutHosting.Contains(s.TierId))
                                  .Select(s => s.OrganizationId).Distinct().ToList();
        if (coveredOrgs.Count == 0) return 0;

        var today = now.Date;
        var drafts = await db.HostedEvents.AsNoTracking()
            .Where(e => coveredOrgs.Contains(e.OrganizationId)
                     && e.LifecycleState == HostedEventLifecycleState.Draft
                     && e.FirstPublishedUtc == null
                     && e.StartsOn >= today)
            .Select(e => new { e.OrganizationId, e.CreatedByAppUserId })
            .ToListAsync(ct);

        foreach (var draft in drafts)
        {
            db.EventCredits.Add(new EventCredit
            {
                Id = Guid.NewGuid(),
                OwnerOrganizationId = draft.OrganizationId,
                PriceAtPurchase = 0m,
                Currency = "USD",
                PurchasedUtc = now,
                ExpiresUtc = now.Add(EventCredits.Life),
                GrantedReason = Reason,
                DateCreated = now,
                CreatedByAppUserId = draft.CreatedByAppUserId,
            });
        }

        return drafts.Count;
    }
}
