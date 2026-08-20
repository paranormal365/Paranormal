using Ben.Data.Common;
using Ben.Data.Common.Enums;
using Ben.Data.Common.Interfaces;
using Ben.Data.Source.Context;
using Ben.Data.Source.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Ben.Data.WebApi.Services.Scheduling;

/// <summary>
/// Emails everyone who said they are coming to an event, the day before it happens.
/// </summary>
/// <remarks>
/// <para>Backlog item 87's tail, and Ben's argument for it was from experience: somebody who signed
/// up three weeks ago needs telling again, and a stranger who does not turn up is worse for the
/// organisation than one who never signed up at all.</para>
///
/// <para><b>Who gets one.</b> Attendees whose RSVP is <see cref="RsvpStatus.Accepted"/>. Not the
/// merely invited — an invitation nobody answered is not a commitment, and reminding somebody about
/// a thing they did not agree to is unsolicited mail. Not the tentative either, though that is the
/// closer call; a "maybe" three weeks ago is a real signal, and if this proves too narrow the fix
/// is one enum value, deliberately taken rather than defaulted into.</para>
///
/// <para><b>Sending twice is the failure to avoid</b>, and it is prevented by
/// <see cref="EventReminderSent"/>'s unique index rather than by this query being clever. The
/// marker is written after the send: a failed send is retried next pass, and the residual risk is a
/// duplicate rather than a silence.</para>
/// </remarks>
public sealed class EventReminderJob : IScheduledJob
{
    /// <summary>How far ahead to look.</summary>
    /// <remarks>
    /// A day. Long enough to change plans around, short enough that it is still the same piece of
    /// news — a reminder a week out is another announcement, not a reminder.
    /// </remarks>
    public static readonly TimeSpan LeadTime = TimeSpan.FromHours(24);

    private readonly IDbContextFactory<BenDataContext> _dbFactory;
    private readonly IEmailService _email;
    private readonly SiteIdentity _site;
    private readonly ILogger<EventReminderJob> _logger;

    public EventReminderJob(
        IDbContextFactory<BenDataContext> dbFactory,
        IEmailService email,
        IOptions<SiteIdentity> site,
        ILogger<EventReminderJob> logger)
    {
        _dbFactory = dbFactory;
        _email = email;
        _site = site.Value;
        _logger = logger;
    }

    public string Name => "event-reminders";

    public async Task RunAsync(CancellationToken ct)
    {
        // Nothing to do, and nothing to log about, when there is nowhere to send mail. Dev machines
        // run with no SMTP host configured and would otherwise produce a failure every five minutes.
        if (!_email.IsConfigured) return;

        await using var db = await _dbFactory.CreateDbContextAsync(ct);

        // The whole feature is behind the events switch: turning events off sitewide has to stop
        // the mail as well as the pages, or a disabled section keeps writing to people.
        if (!await SiteSettingsService.GetBoolAsync(db, SiteSettingKeys.FeatureEvents, whenUnset: true, ct))
            return;

        var now = DateTime.UtcNow;
        var cutoff = now.Add(LeadTime);

        // Events starting inside the window. The lower bound matters as much as the upper: without
        // it, an event that has already begun — or finished — would still qualify, and somebody
        // would be reminded to attend something that is over.
        var due = await db.OrgCalendarEventAttendees.AsNoTracking()
            .Where(a => a.RsvpStatus == RsvpStatus.Accepted
                        && a.OrgCalendarEvent.StartDateTime > now
                        && a.OrgCalendarEvent.StartDateTime <= cutoff
                        && !db.EventReminderSents.Any(r =>
                               r.OrgCalendarEventId == a.OrgCalendarEventId && r.AppUserId == a.AppUserId))
            .Select(a => new Due(
                a.OrgCalendarEventId,
                a.AppUserId,
                a.AppUser.Email,
                a.AppUser.DisplayName,
                a.OrgCalendarEvent.Title,
                a.OrgCalendarEvent.StartDateTime,
                a.OrgCalendarEvent.Location,
                a.OrgCalendarEvent.MeetingUrl,
                a.OrgCalendarEvent.UrlName,
                a.OrgCalendarEvent.Organization.Name,
                a.OrgCalendarEvent.Organization.UrlName))
            .ToListAsync(ct);

        if (due.Count == 0) return;

        var sent = 0;
        foreach (var item in due)
        {
            if (ct.IsCancellationRequested) break;
            if (string.IsNullOrWhiteSpace(item.Email)) continue;   // nowhere to send it

            try
            {
                await _email.SendAsync(item.Email, SubjectFor(item), BodyFor(item), ct);

                db.EventReminderSents.Add(new EventReminderSent
                {
                    Id = Guid.NewGuid(),
                    OrgCalendarEventId = item.EventId,
                    AppUserId = item.AppUserId,
                    SentUtc = DateTime.UtcNow,
                });
                await db.SaveChangesAsync(ct);
                sent++;
            }
            catch (Exception ex)
            {
                // One bad address must not stop the rest of the batch. No marker is written, so
                // the next pass will try this person again.
                _logger.LogWarning(ex,
                    "Could not send an event reminder to {UserId} for event {EventId}.",
                    item.AppUserId, item.EventId);
            }
        }

        if (sent > 0) _logger.LogInformation("Sent {Count} event reminder(s).", sent);
    }

    private string SubjectFor(Due item)
        => $"Tomorrow: {item.EventTitle}";

    private string BodyFor(Due item)
    {
        static string Safe(string? value) => System.Net.WebUtility.HtmlEncode(value ?? string.Empty);

        var greeting = string.IsNullOrWhiteSpace(item.DisplayName) ? "Hello" : $"Hello {Safe(item.DisplayName)}";

        var body = $"<p>{greeting},</p>"
                 + $"<p>You said you would be coming to <strong>{Safe(item.EventTitle)}</strong>, "
                 + $"hosted by {Safe(item.OrganizationName)}.</p>"
                 + $"<p><strong>When:</strong> {item.StartUtc:dddd d MMMM yyyy, HH:mm} UTC</p>";

        if (!string.IsNullOrWhiteSpace(item.Location))
            body += $"<p><strong>Where:</strong> {Safe(item.Location)}</p>";

        if (!string.IsNullOrWhiteSpace(item.MeetingUrl))
            body += $"<p><strong>Joining online:</strong> <a href=\"{Safe(item.MeetingUrl)}\">{Safe(item.MeetingUrl)}</a></p>";

        // The event's own page, where the full details live — including the exact address, which
        // for a location-redacted event is only served to people who are actually coming.
        var link = PublicLinkFor(item);
        if (link is not null)
            body += $"<p><a href=\"{link}\">See the details, or let them know if you can no longer come</a></p>";

        body += $"<p>— {Safe(_site.Name)}</p>";
        return body;
    }

    /// <summary>
    /// The event's public page, when it has one.
    /// </summary>
    /// <remarks>
    /// Both slugs and a configured base URL are needed, and any of the three may be missing — a
    /// private event has no public page at all. A reminder without a link is still a useful
    /// reminder, so this returns null rather than composing a URL that would 404.
    /// </remarks>
    private string? PublicLinkFor(Due item)
    {
        var baseUrl = _site.BaseUrl?.TrimEnd('/');
        if (string.IsNullOrWhiteSpace(baseUrl)) return null;
        if (string.IsNullOrWhiteSpace(item.OrganizationUrlName)) return null;
        if (string.IsNullOrWhiteSpace(item.EventUrlName)) return null;

        return $"{baseUrl}/o/{Uri.EscapeDataString(item.OrganizationUrlName)}/events/{Uri.EscapeDataString(item.EventUrlName)}";
    }

    /// <summary>Everything one reminder needs, read in a single query.</summary>
    private sealed record Due(
        Guid EventId,
        Guid AppUserId,
        string? Email,
        string? DisplayName,
        string EventTitle,
        DateTime StartUtc,
        string? Location,
        string? MeetingUrl,
        string? EventUrlName,
        string OrganizationName,
        string? OrganizationUrlName);
}
