using Ben.Data.Common.Enums;
using Ben.Data.Common.Helpers;
using Ben.Data.Common.Interfaces;
using Ben.Data.Source.Context;
using Microsoft.EntityFrameworkCore;

namespace Ben.Data.WebApi.Services.Tours;

/// <summary>
/// Sends one guest the mail about one tour date, with the walk attached as a calendar file.
/// </summary>
/// <remarks>
/// <para>Item 233. Four places send this — a guest confirming an emailed link, a signed-in guest
/// saying they are coming, an organiser signing somebody up on the pavement, and the reminder the
/// night before — so it lives here rather than being written out four times and drifting.</para>
///
/// <para><b>Best effort, always.</b> Nothing this class does may stop a sign-up: a guest who is on
/// the list but whose mail bounced is a guest on the list. Every failure is logged and swallowed,
/// which is the same bargain the confirmation mail already made.</para>
/// </remarks>
public sealed class TourGuestMailer
{
    private readonly IEmailService _email;
    private readonly Ben.Data.Common.SiteIdentity _site;
    private readonly ILogger<TourGuestMailer> _log;

    public TourGuestMailer(
        IEmailService email,
        Microsoft.Extensions.Options.IOptions<Ben.Data.Common.SiteIdentity> site,
        ILogger<TourGuestMailer> log)
    { _email = email; _site = site.Value; _log = log; }

    /// <summary>Whether a mail could go out at all.</summary>
    public bool IsConfigured => _email.IsConfigured;

    /// <summary>
    /// Sends the sign-up mail for a tour date, or does nothing when the date is not a tour's.
    /// </summary>
    /// <returns>True when a mail was sent.</returns>
    public Task<bool> SendSignUpAsync(
        BenDataContext db, Guid eventId, string toAddress, string? guestName, CancellationToken ct)
        => SendAsync(db, eventId, toAddress, guestName, reminder: false, ct);

    /// <summary>The same mail, the night before.</summary>
    public Task<bool> SendReminderAsync(
        BenDataContext db, Guid eventId, string toAddress, string? guestName, CancellationToken ct)
        => SendAsync(db, eventId, toAddress, guestName, reminder: true, ct);

    private async Task<bool> SendAsync(
        BenDataContext db, Guid eventId, string toAddress, string? guestName,
        bool reminder, CancellationToken ct)
    {
        if (!_email.IsConfigured || string.IsNullOrWhiteSpace(toAddress)) return false;

        try
        {
            if (await GatherAsync(db, eventId, guestName, ct) is not { } gathered) return false;
            var (facts, tour, calendar) = gathered;

            var rendered = reminder
                ? TourMailRenderer.RenderReminder(tour.SubjectTemplate, tour.BodyTemplate, facts)
                : TourMailRenderer.Render(tour.SubjectTemplate, tour.BodyTemplate, facts);

            await _email.SendAsync(new EmailMessage(
                toAddress, rendered.Subject, rendered.HtmlBody,
                Attachments: [new EmailAttachment("tour.ics", IcsBuilder.ContentType, calendar)],
                // A guest hitting reply means to reach the business walking them around a city at
                // night, not our support address — when the business gave one to reply to.
                ReplyTo: tour.ReplyTo), ct);

            return true;
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex,
                "Could not send the tour mail for event {EventId} to {Address}; the sign-up stands.",
                eventId, toAddress);
            return false;
        }
    }

    /// <summary>The tour's own wording, and where a reply should go.</summary>
    private sealed record TourWording(string? SubjectTemplate, string? BodyTemplate, string? ReplyTo);

    /// <summary>
    /// Everything the mail needs, in one read — or null when this date is not a tour's.
    /// </summary>
    /// <remarks>
    /// Null rather than a default mail: a group's ordinary public event keeps the wording it has
    /// always had, and this class must not quietly take over every event on the site.
    /// </remarks>
    private async Task<(TourMailRenderer.TourMailFacts Facts, TourWording Tour, byte[] Calendar)?> GatherAsync(
        BenDataContext db, Guid eventId, string? guestName, CancellationToken ct)
    {
        var row = await db.OrgCalendarEvents.AsNoTracking()
            .Where(e => e.Id == eventId && e.TourId != null)
            .Select(e => new
            {
                e.Id, e.Title, e.StartDateTime, e.EndDateTime, e.AttendeeCapacity, e.UrlName,
                Attending = e.Attendees.Count(a => a.RsvpStatus == RsvpStatus.Accepted),
                OrgName = e.Organization.Name,
                OrgUrlName = e.Organization.UrlName,
                OrgEmail = e.Organization.PublicEmail,
                Tour = e.Tour!,
                Start = e.Tour!.StartOrganizationAddress,
                Guides = e.Guides.OrderBy(g => g.SortOrder)
                    .Select(g => new
                    {
                        Name = g.AppUser.DisplayName ?? g.AppUser.UserName ?? "your guide",
                        PhotoId = db.AppUserPhotos
                            .Where(p => p.AppUserId == g.AppUserId && p.IsPublic && p.IsActive)
                            .Select(p => (Guid?)p.UploadFileId).FirstOrDefault(),
                    })
                    .ToList(),
            })
            .FirstOrDefaultAsync(ct);

        if (row is null) return null;

        var meetingPoint = string.Join(", ", new[]
        {
            row.Start.StreetAddress1, row.Start.StreetAddress2,
            row.Start.City, row.Start.State, row.Start.ZipCode,
        }.Where(p => !string.IsNullOrWhiteSpace(p)));

        var mapUrl = row.Start.Latitude is { } lat && row.Start.Longitude is { } lon
            ? $"https://maps.apple.com/?ll={Number(lat)},{Number(lon)}"
              + $"&q={Uri.EscapeDataString(row.Tour.Name)}"
            : null;

        var dateUrl = row.UrlName is { Length: > 0 } slug
            ? _site.AbsoluteUrl($"/o/{row.OrgUrlName}/events/{slug}")
            : null;

        var facts = new TourMailRenderer.TourMailFacts(
            TourName: row.Tour.Name,
            TourDescriptionHtml: row.Tour.Description,
            MeetingPoint: meetingPoint,
            MeetingPointMapUrl: mapUrl,
            DurationMinutes: row.Tour.DurationMinutes,
            StartUtc: row.StartDateTime,
            EndUtc: row.EndDateTime,
            TimeZoneId: row.Tour.TimeZoneId,
            Capacity: row.AttendeeCapacity,
            SpacesLeft: row.AttendeeCapacity is { } cap ? Math.Max(0, cap - row.Attending) : null,
            DateTitle: row.Title,
            DateUrl: dateUrl,
            GuideNames: [.. row.Guides.Select(g => g.Name)],
            GuidePhotos:
            [
                .. row.Guides.Where(g => g.PhotoId is not null)
                     .Select(g => (g.Name, _site.AbsoluteUrl($"/media/guide-photo/{g.PhotoId}"))),
            ],
            GuestName: guestName,
            BusinessName: row.OrgName,
            BusinessUrl: _site.AbsoluteUrl($"/o/{row.OrgUrlName}"),
            ContactLine: row.Tour.ContactLine,
            SiteName: _site.Name);

        // The calendar entry carries the date's id, so a reminder updates the guest's diary rather
        // than adding a second copy of the same walk.
        var calendar = IcsBuilder.BuildBytes(new IcsBuilder.IcsEvent(
            Uid: $"{row.Id}@ishaunted.com",
            StartUtc: row.StartDateTime,
            EndUtc: row.EndDateTime,
            Summary: row.Tour.Name,
            Description: row.Tour.ContactLine,
            Location: meetingPoint,
            // Only when it is absolute. A relative path in a calendar file is a link that goes
            // nowhere from inside a calendar app, and the site's base URL is empty in development.
            Url: dateUrl is { Length: > 0 } u && u.StartsWith("http", StringComparison.OrdinalIgnoreCase)
                ? u : null,
            OrganizerName: row.OrgName,
            OrganizerEmail: row.OrgEmail,
            Latitude: row.Start.Latitude,
            Longitude: row.Start.Longitude));

        return (facts, new TourWording(
            row.Tour.MailSubjectTemplate, row.Tour.MailBodyTemplate, row.OrgEmail), calendar);
    }

    private static string Number(decimal value)
        => value.ToString("0.######", System.Globalization.CultureInfo.InvariantCulture);
}
