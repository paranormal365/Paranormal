using Ben.Data.Common.Enums;
using Ben.Data.Source.Context;
using Ben.Data.WebApi.Services.Billing;
using Microsoft.EntityFrameworkCore;

namespace Ben.Data.WebApi.Services.Scheduling;

/// <summary>
/// Item 84's clock: the escalating warnings as a period end approaches, and the lapse when it
/// passes.
/// </summary>
/// <remarks>
/// <para><b>The three moments, per the design:</b> two weeks out, the people who handle the
/// group's money hear that billing is ending; one week out, the group is told it must warn its
/// clients that cases will need reassigning; when the date passes, the subscription lapses, the
/// group's open cases pause, and each case's clients are told directly.</para>
///
/// <para><b>Idempotency is date-keyed, not boolean.</b> Each warning records the period end it
/// was sent for, so a renewal — which is a new end date — re-arms both warnings with no clearing
/// code anywhere. The lapse itself is idempotent because it only touches subscriptions still
/// marked Active.</para>
///
/// <para><b>Pausing records the way back.</b> Every case paused here keeps its prior status in
/// <c>StatusBeforePause</c>, and reactivation (the manual provider, later the payment webhook)
/// restores it exactly — an Active investigation resumes Active. The pause is a suspension, not
/// an ending, and the data model should make un-suspending trivial and lossless.</para>
/// </remarks>
public sealed class SubscriptionLapseJob : IScheduledJob
{
    public string Name => "subscription-lapse";

    /// <summary>Open cases — the ones a lapse pauses. Everything past Summarized is finished work.</summary>
    private static readonly CaseStatus[] OpenStatuses =
        [CaseStatus.Proposed, CaseStatus.Accepted, CaseStatus.Active, CaseStatus.Summarized];

    private readonly IDbContextFactory<BenDataContext> _dbFactory;
    private readonly PlatformMessageService _messages;
    private readonly ILogger<SubscriptionLapseJob> _logger;

    public SubscriptionLapseJob(
        IDbContextFactory<BenDataContext> dbFactory,
        PlatformMessageService messages,
        ILogger<SubscriptionLapseJob> logger)
    {
        _dbFactory = dbFactory;
        _messages  = messages;
        _logger    = logger;
    }

    public async Task RunAsync(CancellationToken ct)
    {
        var now = DateTime.UtcNow;

        await SendApproachWarningsAsync(now, ct);
        await LapseExpiredAsync(now, ct);
        await OfferReassignmentToStrandedClientsAsync(now, ct);
    }

    // ── the two warnings ──────────────────────────────────────────────────────

    private async Task SendApproachWarningsAsync(DateTime now, CancellationToken ct)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);

        var approaching = await db.OrganizationSubscriptions
            .Include(s => s.Organization)
            .Where(s => s.Status == SubscriptionStatus.Active
                     && s.CurrentPeriodEnd != null
                     && s.CurrentPeriodEnd > now
                     && s.CurrentPeriodEnd <= now.AddDays(14))
            .ToListAsync(ct);

        foreach (var sub in approaching)
        {
            var end = sub.CurrentPeriodEnd!.Value;
            var recipients = await _messages.BillingRecipientsAsync(sub.OrganizationId, ct);

            // Item 184 Phase D: a lapse also unpublishes published private-engagement cases, and
            // that consequence belongs in the warning — nobody should learn it from the lapse.
            var publishedPrivate = await db.Cases.AsNoTracking()
                .CountAsync(c => c.OrganizationId == sub.OrganizationId
                              && c.IsPublic && c.IsPrivateEngagement, ct);
            var unpublishWarning = publishedPrivate == 0 ? "" :
                $"\n\nYour group has {publishedPrivate} published private-residence "
              + (publishedPrivate == 1 ? "case" : "cases")
              + " — if the period lapses, "
              + (publishedPrivate == 1 ? "it" : "they")
              + " will come off the public site until the plan is renewed. Nothing is deleted, "
              + "and republishing after renewal is one click.";

            if (sub.TwoWeekNoticeSentForPeriodEnd != end)
            {
                await _messages.SendAsync(
                    $"{sub.Organization.Name}: your subscription period ends {end:MM/dd/yyyy}",
                    $"Your group's paid period ends on {end:MM/dd/yyyy}.\n\n"
                  + "Renewing before then keeps everything exactly as it is. If the period ends "
                  + "without renewal, your group keeps read access to everything, but nothing new "
                  + "can be added and open cases are paused for your clients."
                  + unpublishWarning,
                    recipients, sub.CreatedByAppUserId, ct);

                sub.TwoWeekNoticeSentForPeriodEnd = end;
            }

            if (end <= now.AddDays(7) && sub.OneWeekNoticeSentForPeriodEnd != end)
            {
                // The one-week message carries the obligation the design assigns to the GROUP:
                // their clients hear from the platform only when the date actually passes, so the
                // human conversation has to happen first, and this is the prompt for it.
                await _messages.SendAsync(
                    $"{sub.Organization.Name}: one week left — tell your clients",
                    $"Your group's paid period ends on {end:MM/dd/yyyy} — one week from now.\n\n"
                  + "If you are not renewing, please tell your clients directly: when the date "
                  + "passes their cases will be paused, and they will be offered the choice of "
                  + "another organization. That news should come from you before it comes from "
                  + "the platform."
                  + unpublishWarning,
                    recipients, sub.CreatedByAppUserId, ct);

                sub.OneWeekNoticeSentForPeriodEnd = end;
            }
        }

        await db.SaveChangesAsync(ct);
    }

    // ── the lapse ─────────────────────────────────────────────────────────────

    private async Task LapseExpiredAsync(DateTime now, CancellationToken ct)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);

        var expired = await db.OrganizationSubscriptions
            .Include(s => s.Organization)
            .Where(s => s.Status == SubscriptionStatus.Active
                     && s.CurrentPeriodEnd != null
                     && s.CurrentPeriodEnd <= now)
            .ToListAsync(ct);

        foreach (var sub in expired)
        {
            sub.Status      = SubscriptionStatus.Lapsed;
            sub.LapsedAtUtc = now;

            var openCases = await db.Cases
                .Where(c => c.OrganizationId == sub.OrganizationId && OpenStatuses.Contains(c.Status))
                .ToListAsync(ct);

            foreach (var c in openCases)
            {
                c.StatusBeforePause = c.Status;
                c.Status            = CaseStatus.Paused;
                c.DateUpdated       = now;
            }

            // Item 184 Phase D: published private-engagement cases come off the public site —
            // ALL of them, not just open ones; a closed case is published just as publicly. The
            // way back is remembered per case (the StatusBeforePause pattern), and republishing
            // is a deliberate click after renewal, never automatic.
            var publishedPrivate = await db.Cases
                .Where(c => c.OrganizationId == sub.OrganizationId
                         && c.IsPublic && c.IsPrivateEngagement)
                .ToListAsync(ct);
            foreach (var c in publishedPrivate)
            {
                c.IsPublic              = false;
                c.WasPublicBeforeLapse  = true;
                c.DateUpdated           = now;
            }

            await db.SaveChangesAsync(ct);

            // Clients are told per case, after the pause is real — a message about a pause that
            // then failed to commit would be worse than a late one.
            foreach (var c in openCases)
            {
                var clients = await db.CaseClientAccesses
                    .Where(a => a.CaseId == c.Id)
                    .Select(a => a.AppUserId)
                    .ToListAsync(ct);

                if (clients.Count == 0) continue;

                await _messages.SendAsync(
                    $"Your case \"{c.Title}\" is paused",
                    $"{sub.Organization.Name}'s subscription has ended, so your case "
                  + $"\"{c.Title}\" is paused.\n\n"
                  + "Nothing has been lost: everything collected so far stays available to you. "
                  + "If the group renews, the case resumes exactly where it left off. You may "
                  + "also choose to move your case to a different organization — and if you do, "
                  + "you decide what carries over.",
                    clients, sub.CreatedByAppUserId, ct);
            }

            _logger.LogInformation(
                "Subscription for organization {OrgId} lapsed; paused {Count} open case(s), unpublished {Unpublished} private case(s).",
                sub.OrganizationId, openCases.Count, publishedPrivate.Count);
        }
    }

    // ── the stranded-client notice, thirty days in ────────────────────────────

    /// <summary>
    /// A month after a lapse, the clients still paused are told about their way out (item 184
    /// Phase D): the reassignment flow that has been theirs all along.
    /// </summary>
    /// <remarks>
    /// <para><b>Thirty days, not immediately:</b> the lapse-day message says the group may renew,
    /// and most lapses are late payments, not endings. A month of silence is the signal the
    /// group is not coming back soon — that is when pointing clients elsewhere is a service
    /// rather than an incitement.</para>
    ///
    /// <para>Stamped once per lapse via <c>StrandedClientNoticeSentAtUtc</c>; reactivation clears
    /// the stamp so a future lapse re-arms it.</para>
    /// </remarks>
    private async Task OfferReassignmentToStrandedClientsAsync(DateTime now, CancellationToken ct)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);

        var stranded = await db.OrganizationSubscriptions
            .Include(s => s.Organization)
            .Where(s => s.Status == SubscriptionStatus.Lapsed
                     && s.LapsedAtUtc != null
                     && s.LapsedAtUtc <= now.AddDays(-30)
                     && s.StrandedClientNoticeSentAtUtc == null)
            .ToListAsync(ct);

        foreach (var sub in stranded)
        {
            // Exactly the cases the lapse paused — StatusBeforePause is the marker, same as
            // reactivation's restore. A case paused for any other reason is not this story.
            var pausedCases = await db.Cases.AsNoTracking()
                .Where(c => c.OrganizationId == sub.OrganizationId
                         && c.Status == CaseStatus.Paused
                         && c.StatusBeforePause != null)
                .ToListAsync(ct);

            foreach (var c in pausedCases)
            {
                var clients = await db.CaseClientAccesses
                    .Where(a => a.CaseId == c.Id)
                    .Select(a => a.AppUserId)
                    .ToListAsync(ct);
                if (clients.Count == 0) continue;

                await _messages.SendAsync(
                    $"Your case \"{c.Title}\" — you can move it to another group",
                    $"Your case \"{c.Title}\" has been paused for a month, because "
                  + $"{sub.Organization.Name}'s subscription has not been renewed.\n\n"
                  + "You do not have to keep waiting. From your case page you can propose moving "
                  + "it to a different organization — you choose the group, and you choose what "
                  + "carries over. Nothing moves until the new group accepts, and if "
                  + $"{sub.Organization.Name} renews first, everything simply resumes here.",
                    clients, sub.CreatedByAppUserId, ct);
            }

            sub.StrandedClientNoticeSentAtUtc = now;
        }

        await db.SaveChangesAsync(ct);
    }
}
