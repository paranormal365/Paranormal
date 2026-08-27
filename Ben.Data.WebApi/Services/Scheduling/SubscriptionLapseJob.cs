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


    /// <summary>Everyone on the client's side of a case: the primary client and any co-clients.</summary>
    /// <remarks>
    /// <para><b>The primary client has no <c>CaseClientAccess</c> row.</b> They reach their case
    /// through <c>Case.ClientRequest.AppUserId</c> — the request they submitted — and the access
    /// table holds only co-clients, who are added by invitation. <c>MyCaseController.IsCaseClient</c>
    /// checks both, which is why the client side works everywhere it is asked properly.</para>
    ///
    /// <para><b>Both notices here asked only the access table</b>, so the one person who most needed
    /// telling — the client whose home is being investigated, who opened the case — was never told
    /// their case had been paused, and was never offered the reassignment that the thirty-day
    /// notice exists to offer. Only invited co-clients heard anything, and a case with no
    /// co-clients (the common shape) notified nobody at all while the job reported success.</para>
    ///
    /// <para>Found on Ben's question, 2026-08-26: "if they have a case and their paid subscription
    /// expires, is that still handled by pausing everything and notifying the client?" The pausing
    /// was. The notifying was not.</para>
    /// </remarks>
    private static async Task<List<Guid>> ClientsOfCaseAsync(
        BenDataContext db, Guid caseId, CancellationToken ct)
    {
        var recipients = await db.CaseClientAccesses.AsNoTracking()
            .Where(a => a.CaseId == caseId)
            .Select(a => a.AppUserId)
            .ToListAsync(ct);

        var primary = await db.Cases.AsNoTracking()
            .Where(c => c.Id == caseId && c.ClientRequest != null)
            .Select(c => (Guid?)c.ClientRequest!.AppUserId)
            .FirstOrDefaultAsync(ct);

        // Front of the list, and never twice — a primary client may also hold an access row.
        if (primary is { } id && id != Guid.Empty && !recipients.Contains(id))
            recipients.Insert(0, id);

        return recipients;
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

            // ── Is this a renewal, or the end of a free ride? (item 195) ──────
            // The same date means two different things to two different groups, and the notice
            // was written for only one of them. Telling somebody who has never been charged that
            // renewing "keeps everything exactly as it is" is not a small inaccuracy: they read
            // reassurance and then meet a first invoice, which is being misled by us rather than
            // surprised by circumstance. Ben called this "the moment the relationship is won or
            // lost", so it is worth the extra query to say the true thing.
            var trialEnding = await db.CouponRedemptions.AsNoTracking()
                .Where(r => r.OrganizationId == sub.OrganizationId)
                .OrderByDescending(r => r.RedeemedAtUtc)
                .FirstOrDefaultAsync(ct) is { } redemption
                && CouponMath.IsLastFreePeriod(redemption);

            // What they will actually pay. Named, because "your trial is ending" without a number
            // makes the reader go looking, and a price found by hunting feels like one that was
            // hidden.
            var priceLine = await NextPriceLineAsync(db, sub, ct);

            if (sub.TwoWeekNoticeSentForPeriodEnd != end)
            {
                var subject = trialEnding
                    ? $"{sub.Organization.Name}: your free trial ends {end:MM/dd/yyyy}"
                    : $"{sub.Organization.Name}: your subscription period ends {end:MM/dd/yyyy}";

                var body = trialEnding
                    ? $"Your group's free trial ends on {end:MM/dd/yyyy}.\n\n"
                      + $"After that, keeping the plan you are on costs {priceLine}. Nothing "
                      + "changes about how the site works — the only change is that it starts "
                      + "being billed.\n\n"
                      + "If you would rather not continue, you do not have to do anything, and "
                      + "you will not be charged. Your group keeps read access to everything, but "
                      + "nothing new can be added and open cases are paused for your clients."
                    : $"Your group's paid period ends on {end:MM/dd/yyyy}.\n\n"
                      + "Renewing before then keeps everything exactly as it is. If the period "
                      + "ends without renewal, your group keeps read access to everything, but "
                      + "nothing new can be added and open cases are paused for your clients.";

                await _messages.SendAsync(subject, body + unpublishWarning,
                    recipients, sub.CreatedByAppUserId, ct);

                sub.TwoWeekNoticeSentForPeriodEnd = end;
            }

            if (end <= now.AddDays(7) && sub.OneWeekNoticeSentForPeriodEnd != end)
            {
                // The one-week message carries the obligation the design assigns to the GROUP:
                // their clients hear from the platform only when the date actually passes, so the
                // human conversation has to happen first, and this is the prompt for it.
                await _messages.SendAsync(
                    trialEnding
                        ? $"{sub.Organization.Name}: one week of your trial left"
                        : $"{sub.Organization.Name}: one week left — tell your clients",
                    (trialEnding
                        ? $"Your group's free trial ends on {end:MM/dd/yyyy} — one week from "
                          + $"now, after which the plan costs {priceLine}.\n\n"
                          + "If you are continuing, there is nothing to do. If you are not, "
                          + "please tell your clients directly: when the date passes their cases "
                          + "will be paused, and they will be offered the choice of another "
                          + "organization. That news should come from you before it comes from "
                          + "the platform."
                        : $"Your group's paid period ends on {end:MM/dd/yyyy} — one week from "
                          + "now.\n\n"
                          + "If you are not renewing, please tell your clients directly: when the "
                          + "date passes their cases will be paused, and they will be offered the "
                          + "choice of another organization. That news should come from you "
                          + "before it comes from the platform.")
                  + unpublishWarning,
                    recipients, sub.CreatedByAppUserId, ct);

                sub.OneWeekNoticeSentForPeriodEnd = end;
            }
        }

        await db.SaveChangesAsync(ct);
    }

    /// <summary>
    /// What the plan costs once the free ride ends, written the way a person says it.
    /// </summary>
    /// <remarks>
    /// <para>Falls back to a plain phrase rather than inventing a number. A notice that names the
    /// wrong price is worse than one that names none — the first is a broken promise and the
    /// second is a link to click — so an unresolvable price says so and points at the page that
    /// always knows.</para>
    ///
    /// <para>The frozen <c>ListPrice</c> on the redemption is deliberately NOT used: it was
    /// correct when the coupon was taken and the group may have grown bands since. What the
    /// reader needs is what they are about to be charged.</para>
    /// </remarks>
    private static async Task<string> NextPriceLineAsync(
        BenDataContext db, OrganizationSubscription sub, CancellationToken ct)
    {
        if (sub.SubscriptionTierId is not { } tierId) return "what your plan lists";

        var price = await db.SubscriptionTierPrices.AsNoTracking()
            .Where(p => p.SubscriptionTierId == tierId && p.Interval == sub.Interval && p.IsActive)
            .Select(p => (decimal?)p.Price)
            .FirstOrDefaultAsync(ct);

        if (price is not { } amount) return "what your plan lists";

        var per = sub.Interval == BillingInterval.Yearly ? "year" : "month";
        return $"{amount:C} per {per}";
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
                var clients = await ClientsOfCaseAsync(db, c.Id, ct);

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
                var clients = await ClientsOfCaseAsync(db, c.Id, ct);
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
