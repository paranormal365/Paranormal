using Ben.Data.Common.Mail;
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
    /// <returns>True when a mail was queued.</returns>
    public Task<bool> SendSignUpAsync(
        BenDataContext db, Guid eventId, string toAddress, string? guestName, CancellationToken ct)
        => SendAsync(db, eventId, toAddress, guestName, reminder: false, ct);

    /// <summary>
    /// The same mail, the night before.
    /// </summary>
    /// <remarks>
    /// Throws when a tour's mail could not be sent, rather than answering false. False means "this
    /// date has no tour", and the reminder job falls back to its own wording on that answer — so
    /// swallowing a failed send here sent a tour guest a reminder with no meeting point and no
    /// calendar file, and then wrote the marker that stops it ever trying again.
    /// </remarks>
    public Task<bool> SendReminderAsync(
        BenDataContext db, Guid eventId, string toAddress, string? guestName, CancellationToken ct)
        => SendAsync(db, eventId, toAddress, guestName, reminder: true, ct, swallowFailures: false);

    private async Task<bool> SendAsync(
        BenDataContext db, Guid eventId, string toAddress, string? guestName,
        bool reminder, CancellationToken ct, bool swallowFailures = true)
    {
        // Not skipped when mail is not set up: the letter — and the pass in it — waits in the
        // outbox until it is, readable at /admin/mail meanwhile. The reminder job stops before
        // it gets here in that case, so its "no tour" fallback is never reached by mistake.
        if (string.IsNullOrWhiteSpace(toAddress)) return false;

        try
        {
            if (await GatherAsync(db, eventId, guestName, ct) is not { } gathered) return false;
            var (facts, tour, calendar) = gathered;

            var rendered = reminder
                ? TourMailRenderer.RenderReminder(tour.SubjectTemplate, tour.BodyTemplate, facts)
                : TourMailRenderer.Render(tour.SubjectTemplate, tour.BodyTemplate, facts);

            // The pass, when this guest has one (item 247).
            //
            // READ, not minted: the token is written where the seat is confirmed, so a guest whose
            // letter failed still has a pass for the place they hold. A seat with no token — an
            // ordinary calendar attendee, a seat still only requested — simply supplies nothing,
            // and a template that mentions the pass renders without it rather than refusing.
            var body = rendered.HtmlBody;
            var supplied = new Dictionary<string, MailSuppliedValue>(StringComparer.OrdinalIgnoreCase);

            if (await PassTokenAsync(db, eventId, toAddress, ct) is { Length: > 0 } pass)
            {
                var url = _site.AbsoluteUrl($"/api/public/tour-passes/{pass}.png");

                // Drawn into the letter FIRST, linked second. A linked picture a mail client
                // blocked is a guest at a meeting point with nothing to show — the same reason
                // the hosted event's confirmation does it this way round.
                var image = $"<img src=\"{Events.EventPasses.DataUri(pass)}\" width=\"180\" "
                          + "height=\"180\" alt=\"Your pass\" style=\"display:block;border:0;\" />";

                supplied["PassImage"] = new MailSuppliedValue(image, IsHtml: true);
                supplied["PassUrl"] = new MailSuppliedValue(url);

                body += $"""
                    <hr style="border:0;border-top:1px solid #e5e7eb;margin:20px 0;" />
                    <p style="margin:0 0 8px 0;"><strong>Show this when you arrive</strong></p>
                    {image}
                    <p style="margin:8px 0 0 0;font-size:12px;color:#6b7280;">
                      If the picture does not show, <a href="{System.Net.WebUtility.HtmlEncode(url)}">open your pass</a>.
                    </p>
                    """;
            }

            await _email.SendAsync(new EmailMessage(
                toAddress, rendered.Subject, body,
                Attachments: [new EmailAttachment("tour.ics", IcsBuilder.ContentType, calendar)],
                // A guest hitting reply means to reach the business walking them around a city at
                // night, not our support address — when the business gave one to reply to.
                // The letter says which it IS. One method sends both the sign-up and the
                // reminder, and it named every one of them a sign-up — so a reminder was filed
                // under the wrong kind in the outbox, and the tour-reminder template could never
                // apply to anything (item 246, found 2026-09-21).
                ReplyTo: tour.ReplyTo,
                Kind: reminder ? MailKinds.TourReminder.Key : MailKinds.TourSignUp.Key,
                Payload: supplied.Count > 0 ? new MailPayload(Supplied: supplied) : null), ct);

            return true;
        }
        catch (Exception ex) when (swallowFailures)
        {
            // A guest who is on the list but whose mail bounced is a guest on the list: nothing
            // here may undo a sign-up. The reminder path passes false, because there a failure
            // has to be told apart from "this is not a tour".
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
    /// <summary>This guest's pass for this walk, or null when they have none.</summary>
    /// <remarks>
    /// By address, because that is all a mailer is given — and the address is what the seat's
    /// account carries. A guest with no account, or one whose seat was never confirmed, has no
    /// token and gets a letter without a pass rather than no letter.
    /// </remarks>
    private static Task<string?> PassTokenAsync(
        BenDataContext db, Guid eventId, string toAddress, CancellationToken ct)
        => db.OrgCalendarEventAttendees.AsNoTracking()
            .Where(a => a.OrgCalendarEventId == eventId
                     && a.PassToken != null
                     && a.AppUser.Email == toAddress)
            .Select(a => a.PassToken)
            .FirstOrDefaultAsync(ct);

    private async Task<(TourMailRenderer.TourMailFacts Facts, TourWording Tour, byte[] Calendar)?> GatherAsync(
        BenDataContext db, Guid eventId, string? guestName, CancellationToken ct)
    {
        var row = await db.OrgCalendarEvents.AsNoTracking()
            .Where(e => e.Id == eventId && e.TourId != null)
            .Select(e => new
            {
                e.Id, e.Title, e.StartDateTime, e.EndDateTime, e.AttendeeCapacity, e.UrlName,
                e.DateCreated, e.DateUpdated,
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
        // Raised every time the date is edited. A calendar client accepts an update only when the
        // sequence is HIGHER than the one it holds; an equal one with a new start time is
        // discarded, so a rescheduled walk sat in the guest's diary at the old hour for ever.
        // Minutes since the date was created is monotonic, needs no column, and cannot go
        // backwards.
        var sequence = row.DateUpdated is { } edited
            ? (int)Math.Min(int.MaxValue, Math.Max(0, (edited - row.DateCreated).TotalMinutes))
            : 0;

        var calendar = IcsBuilder.BuildBytes(new IcsBuilder.IcsEvent(
            Uid: $"{row.Id}@ishaunted.com",
            Sequence: sequence,
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
