using Ben.Data.Source.Context;
using Ben.Data.WebApi.Services;
using Ben.Data.WebApi.Services.Billing;
using Microsoft.EntityFrameworkCore;

namespace Ben.Data.WebApi.Services.Scheduling;

/// <summary>
/// Delivers the queued plan-reduction notices when their moment arrives.
/// </summary>
/// <remarks>
/// <para>The queue is <c>TierChangeNotice</c> rows that are due and undelivered. Each becomes one
/// platform message to the group's billing people, then is marked delivered — in that order, so a
/// crash between the two risks a duplicate message rather than a silent never-sent, which for a
/// billing notice is the right side to err on.</para>
///
/// <para>The frozen sentences are sent as frozen. A tier edited twice queues two notices, and each
/// describes the edit that created it — recomputing at delivery would describe an edit the notice
/// was never about.</para>
/// </remarks>
public sealed class TierChangeNoticeJob : IScheduledJob
{
    public string Name => "tier-change-notices";

    private readonly IDbContextFactory<BenDataContext> _dbFactory;
    private readonly PlatformMessageService _messages;
    private readonly ILogger<TierChangeNoticeJob> _logger;

    public TierChangeNoticeJob(
        IDbContextFactory<BenDataContext> dbFactory,
        PlatformMessageService messages,
        ILogger<TierChangeNoticeJob> logger)
    {
        _dbFactory = dbFactory;
        _messages  = messages;
        _logger    = logger;
    }

    public async Task RunAsync(CancellationToken ct)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);

        var now = DateTime.UtcNow;
        var due = await db.TierChangeNotices
            .Include(n => n.SubscriptionTier)
            .Where(n => n.DeliveredAtUtc == null && n.DeliverAtUtc <= now)
            .OrderBy(n => n.DeliverAtUtc)
            .Take(200)
            .ToListAsync(ct);

        foreach (var notice in due)
        {
            var recipients = await _messages.BillingRecipientsAsync(notice.OrganizationId, ct);

            // A notice whose change is already live (free-band area removals arrive here after
            // a short grace) must not promise a renewal that protects nothing.
            var alreadyLive = notice.EffectiveAtUtc <= notice.DeliverAtUtc;

            var lines = string.Join('\n', notice.Sentences.Split('\n').Select(s => "• " + s));
            var body = alreadyLive
                ? $"Your group's plan, {notice.SubscriptionTier.Name}, has changed:\n\n"
                  + lines
                  + "\n\nThese changes are already in effect."
                : $"Your group's plan, {notice.SubscriptionTier.Name}, is changing when your "
                  + $"current period ends on {notice.EffectiveAtUtc:MM/dd/yyyy}:\n\n"
                  + lines
                  + "\n\nNothing changes before then — you keep the terms you signed up for "
                  + "until your renewal.";

            var sent = await _messages.SendAsync(
                alreadyLive
                    ? $"Your {notice.SubscriptionTier.Name} plan has changed"
                    : $"Upcoming change to your {notice.SubscriptionTier.Name} plan",
                body, recipients, notice.CreatedByAppUserId, ct);

            notice.DeliveredAtUtc = DateTime.UtcNow;
            notice.DateUpdated    = notice.DeliveredAtUtc;
            await db.SaveChangesAsync(ct);

            _logger.LogInformation(
                "Delivered tier-change notice {NoticeId} for organization {OrgId} to {Count} people.",
                notice.Id, notice.OrganizationId, sent);
        }
    }
}
