using Ben.Data.Common;
using Ben.Data.Common.Mail;
using Ben.Data.Common.Enums;
using Ben.Data.Common.Interfaces;
using Ben.Data.Source.Context;
using Ben.Data.Source.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Net;

namespace Ben.Data.WebApi.Services;

/// <summary>
/// Mails a case's clients when the case changes state or a visit is scheduled (item 206).
/// </summary>
/// <remarks>
/// <para>The mail says exactly what the site says: the status label and the client sentence come
/// from <see cref="CaseStatusWording"/>, the one source the case page draws from. A visit mail
/// names the date as the group entered it and sends the client to the case page, where it is
/// shown in their own clock.</para>
///
/// <para><b>Best effort, observable, never in the way.</b> A send that fails must not fail the
/// change that caused it — the status has already been saved — so every send is logged at
/// Information when it goes and at Error when it does not, which is what the mail diagnostics
/// page and the error log read. When mail is not configured the mailer says so once per call at
/// Debug and does nothing. Recipients are the case's clients with a confirmed address; an
/// unconfirmed address is skipped and logged, never mailed.</para>
/// </remarks>
public sealed class ClientStatusMailer
{
    private readonly IEmailService _email;
    private readonly SiteIdentity _site;
    private readonly ILogger<ClientStatusMailer> _log;

    public ClientStatusMailer(IEmailService email, IOptions<SiteIdentity> site, ILogger<ClientStatusMailer> log)
    {
        _email = email; _site = site.Value; _log = log;
    }

    /// <summary>The case's status changed. No-op when it did not.</summary>
    public async Task CaseStatusChangedAsync(BenDataContext db, Case c, CaseStatus previous, CancellationToken ct)
    {
        if (previous == c.Status) return;
        var label = CaseStatusWording.Label(c.Status);
        var subject = $"Your case {Reference(c)} is now {label}";
        var body = $"<p>Your case <strong>{WebUtility.HtmlEncode(c.Title)}</strong> ({Reference(c)}) is now "
                 + $"<strong>{WebUtility.HtmlEncode(label)}</strong>.</p>"
                 + $"<p>{WebUtility.HtmlEncode(CaseStatusWording.ClientSentence(c.Status))}</p>";
        await SendToClientsAsync(db, c, $"status {previous} → {c.Status}", subject, $"Your case is now {label}", body, ct,
            MailKinds.CaseStatusChanged.Key);
    }

    /// <summary>A visit has been put on the calendar for the case.</summary>
    public Task VisitScheduledAsync(BenDataContext db, Case c, Investigation visit, CancellationToken ct)
        => VisitAsync(db, c, visit, "scheduled", "A visit is scheduled for your case",
            $"<p>The group has scheduled a visit for your case <strong>{WebUtility.HtmlEncode(c.Title)}</strong> ({Reference(c)}).</p>", ct,
            MailKinds.VisitScheduled.Key);

    /// <summary>A scheduled visit moved to another time.</summary>
    public Task VisitRescheduledAsync(BenDataContext db, Case c, Investigation visit, DateTime previouslyAt, CancellationToken ct)
        => VisitAsync(db, c, visit, "rescheduled", "A visit to your case has been rescheduled",
            $"<p>The visit to your case <strong>{WebUtility.HtmlEncode(c.Title)}</strong> ({Reference(c)}) that was set for "
            + $"{When(previouslyAt)} has moved.</p>", ct,
            MailKinds.VisitRescheduled.Key);

    /// <summary>A scheduled visit will not happen.</summary>
    public async Task VisitCancelledAsync(BenDataContext db, Case c, Investigation visit, CancellationToken ct)
    {
        var body = $"<p>The visit to your case <strong>{WebUtility.HtmlEncode(c.Title)}</strong> ({Reference(c)}) "
                 + $"that was set for {When(visit.ScheduledDateTime)} has been cancelled. The group will be in touch about what happens next.</p>";
        await SendToClientsAsync(db, c, "visit cancelled", $"A visit to your case {Reference(c)} was cancelled", "A visit was cancelled", body, ct,
            MailKinds.VisitCancelled.Key);
    }

    private async Task VisitAsync(BenDataContext db, Case c, Investigation visit, string kind, string title, string lead, CancellationToken ct,
                                  string? mailKind = null)
    {
        var body = lead
                 + $"<p><strong>{When(visit.ScheduledDateTime)}</strong>"
                 + (visit.EndDateTime is { } end ? $" until {When(end)}" : "")
                 + (string.IsNullOrWhiteSpace(visit.Location) ? "" : $"<br/>{WebUtility.HtmlEncode(visit.Location)}")
                 + "</p><p>Open your case to see it in your own time zone and to message the group.</p>";
        await SendToClientsAsync(db, c, $"visit {kind}", $"{title}: {Reference(c)}", title, body, ct, mailKind);
    }

    private async Task SendToClientsAsync(BenDataContext db, Case c, string what, string subject, string title, string bodyHtml, CancellationToken ct,
                                          string? kind = null)
    {
        if (!_email.IsConfigured)
        {
            _log.LogDebug("Client status mail skipped for case {CaseId} ({What}): mail is not configured.", c.Id, what);
            return;
        }

        // ── who "the client" is ───────────────────────────────────────────────
        //
        // The PRIMARY client has no CaseClientAccess row. Those exist only for CO-clients — the
        // people a client invites — and the client who asked for the investigation reaches their
        // case through Case.ClientRequest.AppUserId instead.
        //
        // So asking only the access table meant the one person who most needed telling was never
        // told, and for the common shape (a case with no co-clients) this logged "no client to
        // mail" and mailed nobody at all. Every status change — accepted, active, being written
        // up, closed, transferred — and every visit scheduled, moved or cancelled went nowhere,
        // against your-case.md's own promise: "an email goes to your confirmed address".
        //
        // SubscriptionLapseJob.ClientsOfCaseAsync had exactly this bug and was fixed with a
        // paragraph explaining it; this mailer was not (2026-09-17 audit).
        var clientIds = await db.CaseClientAccesses.AsNoTracking()
            .Where(a => a.CaseId == c.Id)
            .Select(a => a.AppUserId)
            .ToListAsync(ct);

        var primaryId = await db.Cases.AsNoTracking()
            .Where(x => x.Id == c.Id && x.ClientRequest != null)
            .Select(x => (Guid?)x.ClientRequest!.AppUserId)
            .FirstOrDefaultAsync(ct);

        // Front of the list, and never twice — a primary client may also hold an access row.
        if (primaryId is { } pid && pid != Guid.Empty && !clientIds.Contains(pid))
            clientIds.Insert(0, pid);

        var clients = await db.AppUsers.AsNoTracking()
            .Where(u => clientIds.Contains(u.Id))
            .Select(u => new { AppUserId = u.Id, u.Email, u.EmailConfirmed })
            .ToListAsync(ct);

        if (clients.Count == 0)
        {
            _log.LogInformation("Client status mail: case {CaseId} ({What}) has no client to mail.", c.Id, what);
            return;
        }

        var html = BenEmailLayout.Wrap(_site, title, bodyHtml, "Open your case", _site.AbsoluteUrl($"/my-cases/{c.Id}"));
        foreach (var client in clients)
        {
            if (string.IsNullOrWhiteSpace(client.Email) || !client.EmailConfirmed)
            {
                _log.LogInformation("Client status mail: case {CaseId} ({What}) — client {AppUserId} has no confirmed address; not mailed.", c.Id, what, client.AppUserId);
                continue;
            }
            try
            {
                await _email.SendAsync(new EmailMessage(client.Email, subject, html, Kind: kind), ct);
                _log.LogInformation("Client status mail sent to {Recipient} for case {CaseId}: {What}.", client.Email, c.Id, what);
            }
            catch (Exception ex)
            {
                // Error, so it survives in the Logs table and the mail diagnostics page can show it.
                _log.LogError(ex, "Client status mail to {Recipient} for case {CaseId} ({What}) failed.", client.Email, c.Id, what);
            }
        }
    }

    private static string Reference(Case c) => $"#{c.CaseYear}-{c.OrgCaseNumber:000}";
    private static string When(DateTime utc) => $"{utc:dddd, MMMM d, yyyy 'at' h:mm tt} UTC";
}
