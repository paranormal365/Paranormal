using Ben.Data.WebApi.Services.Mail;
using Ben.Data.Common.Mail;
using Ben.Data.Common;
using Ben.Data.Common.Interfaces;
using Ben.Data.Source.Context;
using Ben.Data.Source.Entities;
using Ben.Data.WebApi.Services.Events;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Ben.Data.WebApi.Services.Scheduling;

/// <summary>
/// Warns the holder of an event credit thirty days before it lapses (item 235, phase 1B.4).
/// </summary>
/// <remarks>
/// <para>Ben, 2026-09-11: "30-day warning sounds reasonable." It costs nothing to send and it saves
/// the support ticket that arrives when somebody finds out a credit expired last week — which is a
/// conversation about ninety-nine dollars, and one nobody can win.</para>
///
/// <para><b>Once per credit</b>, and the marker is <see cref="EventCredit.ExpiryWarningSentUtc"/>
/// on the credit itself rather than a separate table: there is exactly one warning per credit for
/// the life of the credit, so the row that expires is the natural place to record that its warning
/// went. It is stamped after the send, so a failure is retried next pass and the residual risk is a
/// duplicate rather than a silence.</para>
///
/// <para><b>Email where there is an address, the bell where there is not.</b> A credit somebody
/// paid for must not lapse unannounced because their group has no reachable billing contact, or
/// because this deployment has no mail server — so the platform message is not a nicety, it is the
/// path that always exists. On a machine with no SMTP configured every warning goes that way, which
/// is correct rather than a degradation.</para>
///
/// <para><b>Nothing here expires anything.</b> An unspent credit past its date is simply gone —
/// <see cref="EventCredit.IsSpendable"/> reads the clock, so there is no state to flip and no job
/// that could get it wrong. This one only speaks.</para>
/// </remarks>
public sealed class EventCreditExpiryJob : IScheduledJob
{
    public string Name => "event-credit-expiry";

    private readonly IDbContextFactory<BenDataContext> _dbFactory;
    private readonly IEmailService _email;
    private readonly PlatformMessageService _messages;
    private readonly SiteIdentity _site;
    private readonly ILogger<EventCreditExpiryJob> _logger;

    public EventCreditExpiryJob(
        IDbContextFactory<BenDataContext> dbFactory,
        IEmailService email,
        PlatformMessageService messages,
        IOptions<SiteIdentity> site,
        ILogger<EventCreditExpiryJob> logger)
    {
        _dbFactory = dbFactory;
        _email     = email;
        _messages  = messages;
        _site      = site.Value;
        _logger    = logger;
    }

    public async Task RunAsync(CancellationToken ct)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);

        var now = DateTime.UtcNow;
        await WarnAboutHeldMonthsAsync(db, now, ct);
        var due = await EventCredits.DueAWarningAsync(db, now, ct);
        if (due.Count == 0) return;

        var warned = 0;
        foreach (var credit in due)
        {
            if (ct.IsCancellationRequested) break;

            try
            {
                var (holderName, recipients) = await HolderAsync(db, credit, ct);
                if (recipients.Count == 0)
                {
                    // Nobody to tell. The marker is still stamped: re-asking the same unanswerable
                    // question every five minutes for a month is noise, not diligence.
                    _logger.LogWarning(
                        "Event credit {CreditId} lapses on {Expires} and has nobody to warn.",
                        credit.Id, credit.ExpiresUtc);
                }
                else
                {
                    await WarnAsync(credit, holderName, recipients, ct);
                }

                credit.ExpiryWarningSentUtc = DateTime.UtcNow;
                credit.DateUpdated = credit.ExpiryWarningSentUtc;
                await db.SaveChangesAsync(ct);
                warned++;
            }
            catch (Exception ex)
            {
                // One unreachable holder must not stop the rest of the batch, and no marker is
                // written, so the next pass tries this credit again.
                _logger.LogWarning(ex,
                    "Could not warn the holder of event credit {CreditId}.", credit.Id);
            }
        }

        if (warned > 0) _logger.LogInformation("Warned about {Count} expiring event credit(s).", warned);
    }

    /// <summary>
    /// Held extra months for a show's run, lapsing within the lead (10/24/2026): one letter per holder, once.
    /// </summary>
    private async Task WarnAboutHeldMonthsAsync(BenDataContext db, DateTime now, CancellationToken ct)
    {
        var due = await EventRunMonths.DueAWarningAsync(db, now, ct);
        foreach (var holder in due.GroupBy(m => (m.OwnerOrganizationId, m.OwnerAppUserId)))
        {
            if (ct.IsCancellationRequested) break;
            try
            {
                var first = holder.OrderBy(m => m.ExpiresUtc).First();
                var asCredit = new EventCredit { OwnerOrganizationId = first.OwnerOrganizationId, OwnerAppUserId = first.OwnerAppUserId };
                var (holderName, recipients) = await HolderAsync(db, asCredit, ct);
                var count = holder.Count();
                var what = count == 1 ? "an extra month" : $"{count} extra months";
                var subject = $"{(count == 1 ? "An extra month for a show's run expires" : $"{count} extra months for a show's run expire")} on {first.ExpiresUtc:MM/dd/yyyy}";
                var text = $"{holderName} holds {what} for a show's run, which {(count == 1 ? "lapses" : "lapse")} on {first.ExpiresUtc:MM/dd/yyyy}. "
                         + (count == 1 ? "Use it on a published show's page to let its run go a month longer. After that date it's gone and isn't refunded."
                                     : "Use them on a published show's page to let its run go a month longer each. After that date they're gone and aren't refunded.");
                var unreachable = new List<Guid>();
                foreach (var person in recipients)
                {
                    if (_email.IsConfigured && !string.IsNullOrWhiteSpace(person.Email))
                        await _email.SendAsync(new EmailMessage(person.Email!, subject,
                            $"<p>{System.Net.WebUtility.HtmlEncode(text)}</p><p>— {System.Net.WebUtility.HtmlEncode(_site.Name)}</p>",
                            Kind: MailKinds.EventRunMonthExpiring.Key,
                            Payload: MailRows.For(MailKinds.EventRunMonthExpiring, MailRows.Person(person.Email, person.DisplayName))), ct);
                    else
                        unreachable.Add(person.Id);
                }
                if (unreachable.Count > 0)
                    await _messages.SendAsync(subject, text, unreachable,
                        first.CreatedByAppUserId == Guid.Empty ? unreachable[0] : first.CreatedByAppUserId, ct);

                foreach (var m in holder) { m.ExpiryWarningSentUtc = DateTime.UtcNow; m.DateUpdated = m.ExpiryWarningSentUtc; }
                await db.SaveChangesAsync(ct);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Could not warn about held extra months.");
            }
        }
    }

    /// <summary>Who holds this credit, and who should hear that it is about to lapse.</summary>
    /// <remarks>
    /// A group's credit goes to the people who already get its billing mail — the same set that
    /// hears about a plan change, because this is the same kind of news. A person's credit goes to
    /// that person.
    /// </remarks>
    private async Task<(string HolderName, List<Recipient> Recipients)> HolderAsync(
        BenDataContext db, EventCredit credit, CancellationToken ct)
    {
        if (credit.OwnerOrganizationId is { } orgId)
        {
            var name = await db.Organizations.AsNoTracking()
                .Where(o => o.Id == orgId).Select(o => o.Name).FirstOrDefaultAsync(ct) ?? "your group";

            var ids = await _messages.BillingRecipientsAsync(orgId, ct);
            var people = await db.AppUsers.AsNoTracking()
                .Where(u => ids.Contains(u.Id))
                .Select(u => new Recipient(u.Id, u.Email, u.DisplayName))
                .ToListAsync(ct);

            return (name, people);
        }

        var holder = await db.AppUsers.AsNoTracking()
            .Where(u => u.Id == credit.OwnerAppUserId)
            .Select(u => new Recipient(u.Id, u.Email, u.DisplayName))
            .FirstOrDefaultAsync(ct);

        return (holder?.DisplayName ?? "you", holder is null ? [] : [holder]);
    }

    private async Task WarnAsync(
        EventCredit credit, string holderName, List<Recipient> recipients, CancellationToken ct)
    {
        var subject = $"An event credit expires on {credit.ExpiresUtc:MM/dd/yyyy}";
        var unreachable = new List<Guid>();

        foreach (var person in recipients)
        {
            if (_email.IsConfigured && !string.IsNullOrWhiteSpace(person.Email))
            {
                await _email.SendAsync(new EmailMessage(person.Email!, subject,
                    MailBody(credit, holderName, person),
                    Kind: MailKinds.EventCreditExpiring.Key,
                    // The person, and for a group's credit the group, as a template reads them.
                    Payload: MailRows.For(MailKinds.EventCreditExpiring,
                        MailRows.Person(person.Email, person.DisplayName),
                        credit.OwnerOrganizationId is null ? null
                            : new MailRows.Manual("Organizations",
                                new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase) { ["Name"] = holderName }))), ct);
                continue;
            }

            unreachable.Add(person.Id);
        }

        if (unreachable.Count > 0)
            // The sender is the buyer, falling back to the recipient. A webhook that arrived
            // without a user in its metadata leaves the buyer empty, and an empty sender would
            // fail the message's own foreign key — losing the warning to save a byte of accuracy
            // about who sent it.
            await _messages.SendAsync(
                subject, MessageBody(credit, holderName), unreachable,
                credit.CreatedByAppUserId == Guid.Empty ? unreachable[0] : credit.CreatedByAppUserId,
                ct);
    }

    private string MailBody(EventCredit credit, string holderName, Recipient person)
    {
        static string Safe(string? value) => System.Net.WebUtility.HtmlEncode(value ?? string.Empty);

        var greeting = string.IsNullOrWhiteSpace(person.DisplayName)
            ? "Hello" : $"Hello {Safe(person.DisplayName)}";

        var body = $"<p>{greeting},</p>"
                 + $"<p>{Safe(holderName)} holds an <strong>event credit</strong> that runs out on "
                 + $"<strong>{credit.ExpiresUtc:MM/dd/yyyy}</strong> — {DaysLeft(credit)} from now.</p>"
                 + "<p>A credit is spent when an event is published, so publishing an event before "
                 + "that date uses it. After it, the credit is gone and is not refunded.</p>";

        if (LinkFor(credit) is { } link)
            body += $"<p><a href=\"{link}\">See the credits this group holds</a></p>";

        body += $"<p>— {Safe(_site.Name)}</p>";
        return body;
    }

    private string MessageBody(EventCredit credit, string holderName)
        => $"{holderName} holds an event credit that runs out on {credit.ExpiresUtc:MM/dd/yyyy} — "
         + $"{DaysLeft(credit)} from now.\n\n"
         + "A credit is spent when an event is published, so publishing an event before that date "
         + "uses it. After it, the credit is gone and is not refunded.";

    /// <summary>"30 days", said in whole days, never "0 days".</summary>
    private static string DaysLeft(EventCredit credit)
    {
        var days = (int)Math.Ceiling((credit.ExpiresUtc - DateTime.UtcNow).TotalDays);
        return days <= 1 ? "a day" : $"{days} days";
    }

    /// <summary>The billing page for the group that holds it, when there is one to link to.</summary>
    private string? LinkFor(EventCredit credit)
    {
        var baseUrl = _site.BaseUrl?.TrimEnd('/');
        if (string.IsNullOrWhiteSpace(baseUrl)) return null;
        if (credit.OwnerOrganizationId is not { } orgId) return $"{baseUrl}/pricing";
        return $"{baseUrl}/organizations/{orgId}/billing";
    }

    private sealed record Recipient(Guid Id, string? Email, string? DisplayName);
}
