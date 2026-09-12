using Ben.Data.Common.Enums;
using Ben.Data.Common.Helpers;
using Ben.Data.Common.Interfaces;
using Ben.Data.Source.Context;
using Ben.Data.Source.Entities;
using Microsoft.EntityFrameworkCore;

namespace Ben.Data.WebApi.Services.Events;

/// <summary>
/// The letters a hosted event sends a guest: the answer, the pass, and the nights for their diary
/// (item 235 phase 2.3).
/// </summary>
/// <remarks>
/// <para>Ben, 2026-09-12: <i>"We should probably send a confirmation e-mail or offer it. Generate
/// a QR code for the confirmation the event organizer can scan to check them in when they arrive
/// so check in is smoother."</i></para>
///
/// <para><b>Beside <see cref="Tours.TourGuestMailer"/>, not inside it.</b> A walk's welcome talks
/// about a meeting point, a guide's face and how long you will be on your feet; a weekend at a
/// hotel has a room, a set of nights and a dinner. Bending one template around both would have
/// produced a letter that was wrong for each.</para>
///
/// <para><b>Best effort, always.</b> Nothing here may undo a decision: a guest who is confirmed
/// but whose mail bounced is a confirmed guest. Every failure is logged and swallowed, which is
/// the bargain every other mail on this site already makes.</para>
///
/// <para><b>The pass travels as inline bytes, and as a link.</b> Most mail clients block remote
/// pictures until somebody clicks, and a guest at a door whose pass never loaded has no pass — so
/// the code is drawn into the letter itself. The link is there too, for the client that strips
/// data URIs instead.</para>
/// </remarks>
public sealed class EventGuestMailer
{
    private readonly IEmailService _email;
    private readonly Ben.Data.Common.SiteIdentity _site;
    private readonly ILogger<EventGuestMailer> _log;

    public EventGuestMailer(
        IEmailService email,
        Microsoft.Extensions.Options.IOptions<Ben.Data.Common.SiteIdentity> site,
        ILogger<EventGuestMailer> log)
    { _email = email; _site = site.Value; _log = log; }

    /// <summary>Whether a letter could go at all.</summary>
    public bool IsConfigured => _email.IsConfigured;

    /// <summary>
    /// Tells a guest what the venue decided, and sends the pass when the answer is yes.
    /// </summary>
    /// <remarks>
    /// <para>One method for all three answers, because a guest who is turned down must be told
    /// exactly as reliably as one who is taken — and two methods is how the unhappy one quietly
    /// stops being sent.</para>
    ///
    /// <para><b>The calendar file carries one entry per booked night</b>, each with the room. A
    /// single entry spanning the weekend would sit across it as one block and tell a guest nothing
    /// about where they are sleeping on Saturday.</para>
    /// </remarks>
    /// <returns>True when a letter was sent.</returns>
    public async Task<bool> SendDecisionAsync(
        BenDataContext db, Guid bookingId, CancellationToken ct)
    {
        if (!_email.IsConfigured) return false;

        try
        {
            var booking = await LoadAsync(db, bookingId, ct);
            if (booking is null) return false;

            var to = booking.LeadAppUser?.Email;
            if (string.IsNullOrWhiteSpace(to)) return false;

            var ev = booking.HostedEvent;
            var confirmed = booking.Status == HostedEventBookingStatus.Confirmed;

            var pass = confirmed
                ? await db.HostedEventPasses.AsNoTracking()
                    .Where(p => p.HostedEventBookingId == booking.Id && p.RevokedUtc == null)
                    .OrderByDescending(p => p.IssuedUtc)
                    .FirstOrDefaultAsync(ct)
                : null;

            var (subject, body) = booking.Status switch
            {
                HostedEventBookingStatus.Confirmed  => Confirmation(booking, ev, pass),
                HostedEventBookingStatus.TurnedDown => TurnedDown(booking, ev),
                _                                   => Released(booking, ev),
            };

            var attachments = new List<EmailAttachment>();
            if (confirmed && CalendarFor(booking, ev) is { Length: > 0 } calendar)
                attachments.Add(new EmailAttachment("event.ics", IcsBuilder.ContentType, calendar));

            await _email.SendAsync(new EmailMessage(
                to, subject, body,
                Attachments: attachments,
                // A guest hitting reply means to reach the venue whose spare room they are sleeping
                // in, not our support address.
                ReplyTo: ev?.Organization?.PublicEmail), ct);

            // Recorded on the pass rather than the booking, so a reissue starts unsent and a host
            // can see at a glance whose replacement has not gone out yet.
            if (pass is not null)
            {
                var tracked = await db.HostedEventPasses.FirstOrDefaultAsync(p => p.Id == pass.Id, ct);
                if (tracked is not null)
                {
                    tracked.EmailedUtc = DateTime.UtcNow;
                    await db.SaveChangesAsync(ct);
                }
            }

            return true;
        }
        catch (Exception ex)
        {
            // A guest who is confirmed but whose mail bounced is a confirmed guest. Nothing here
            // may undo a decision the venue has made.
            _log.LogWarning(ex,
                "Could not send the decision letter for booking {BookingId}; the decision stands.",
                bookingId);
            return false;
        }
    }

    // ── the letters ──────────────────────────────────────────────────────────

    private (string Subject, string Body) Confirmation(
        HostedEventBooking booking, HostedEvent? ev, HostedEventPass? pass)
    {
        var name = Safe(ev?.Name ?? "the event");
        var body = new System.Text.StringBuilder();

        body.Append($"<p>{Greeting(booking)}</p>");
        body.Append($"<p><strong>{name}</strong> has confirmed your place");
        body.Append(booking.PartySize > 1 ? $" for {booking.PartySize} people.</p>" : ".</p>");

        if (Where(booking) is { Count: > 0 } nights)
        {
            body.Append("<p>Your nights:</p><ul>");
            foreach (var line in nights) body.Append($"<li>{Safe(line)}</li>");
            body.Append("</ul>");
        }
        else if (booking.Kind == HostedEventBookingKind.DayPass)
        {
            body.Append("<p>This is a day pass, so there is no room with it.</p>");
        }

        if (Trimmed(booking.DecisionNote) is { } note)
            body.Append($"<p>From the venue: {Safe(note)}</p>");

        if (pass is not null)
        {
            var url = _site.AbsoluteUrl(
                Controllers.Entities.HostedEventBookingController.PassImageUrl(pass.Token));

            body.Append("<p><strong>Show this at the door.</strong> One code admits your whole "
                      + "party, so nobody else needs their own.</p>");
            // Inline first: a linked picture that a mail client blocked is a guest with no pass.
            body.Append($"<p><img src=\"{EventPasses.DataUri(pass.Token)}\" "
                      + "alt=\"Your entry pass\" width=\"180\" height=\"180\" /></p>");
            body.Append($"<p>If the code above did not load, <a href=\"{url}\">open it here</a>.</p>");
        }

        if (Trimmed(ev?.Organization?.PublicEmail) is { } reply)
            body.Append($"<p>Anything to ask before you come? Reply to this, or write to {Safe(reply)}.</p>");

        return ($"Your place at {ev?.Name ?? "the event"} is confirmed", body.ToString());
    }

    private (string Subject, string Body) TurnedDown(HostedEventBooking booking, HostedEvent? ev)
    {
        var name = Safe(ev?.Name ?? "the event");
        var body = new System.Text.StringBuilder();

        body.Append($"<p>{Greeting(booking)}</p>");
        body.Append($"<p>We're sorry — <strong>{name}</strong> could not take your booking.</p>");

        // The reason is the whole point of this letter. A refusal with none reads as arbitrary,
        // and the commonest reason is one the guest can act on.
        if (Trimmed(booking.DecisionNote) is { } note)
            body.Append($"<p>{Safe(note)}</p>");

        body.Append("<p>Nothing has been charged, and you are welcome to ask again if anything "
                  + "changes.</p>");

        return ($"About your booking at {ev?.Name ?? "the event"}", body.ToString());
    }

    private (string Subject, string Body) Released(HostedEventBooking booking, HostedEvent? ev)
    {
        var name = Safe(ev?.Name ?? "the event");
        var body = new System.Text.StringBuilder();

        body.Append($"<p>{Greeting(booking)}</p>");
        body.Append($"<p>Your booking at <strong>{name}</strong> has been released, and any pass "
                  + "you were sent no longer works.</p>");

        if (Trimmed(booking.DecisionNote) is { } note)
            body.Append($"<p>{Safe(note)}</p>");

        body.Append("<p>If this is a surprise, reply and the venue will sort it out.</p>");

        return ($"Your booking at {ev?.Name ?? "the event"} has been released", body.ToString());
    }

    // ── the diary ────────────────────────────────────────────────────────────

    /// <summary>
    /// One calendar entry per booked night, or one for the whole event on a day pass.
    /// </summary>
    /// <remarks>
    /// Each night keeps its own uid, so a later letter updates each entry rather than every night
    /// overwriting the last and leaving a guest with one entry for a three-night stay.
    /// </remarks>
    private byte[] CalendarFor(HostedEventBooking booking, HostedEvent? ev)
    {
        if (ev is null) return [];

        var venue = ev.Place?.Name;
        var url = ev.UrlName is { Length: > 0 } slug && ev.Organization?.UrlName is { Length: > 0 } org
            ? _site.AbsoluteUrl($"/o/{org}/events/{slug}")
            : null;
        var absolute = url is { Length: > 0 } u
                    && u.StartsWith("http", StringComparison.OrdinalIgnoreCase) ? u : null;

        var nights = booking.Nights.OrderBy(n => n.HostedEventNight.Date).ToList();

        var entries = nights.Count > 0
            ? nights.Select(n => new IcsBuilder.IcsEvent(
                Uid: $"{booking.Id}-{n.HostedEventNightId}@ishaunted.com",
                // An overnight stay is not a timed appointment. Six in the evening to ten the next
                // morning is the honest span of "you are staying here tonight", and a calendar that
                // showed a night as a single point in time would tell a guest nothing.
                StartUtc: n.HostedEventNight.Date.Date.AddHours(18),
                EndUtc: n.HostedEventNight.Date.Date.AddDays(1).AddHours(10),
                Summary: $"{ev.Name} — {EventCapacity.NameOf(n) ?? "your room"}",
                Description: null,
                Location: venue,
                Url: absolute,
                OrganizerName: ev.Organization?.Name,
                OrganizerEmail: ev.Organization?.PublicEmail)).ToList()
            : [new IcsBuilder.IcsEvent(
                Uid: $"{booking.Id}@ishaunted.com",
                StartUtc: ev.StartsOn.Date.AddHours(18),
                EndUtc: ev.EndsOn.Date.AddDays(1).AddHours(2),
                Summary: ev.Name,
                Description: null,
                Location: venue,
                Url: absolute,
                OrganizerName: ev.Organization?.Name,
                OrganizerEmail: ev.Organization?.PublicEmail)];

        return IcsBuilder.BuildBytes(entries);
    }

    // ── plumbing ─────────────────────────────────────────────────────────────

    private static Task<HostedEventBooking?> LoadAsync(
        BenDataContext db, Guid bookingId, CancellationToken ct)
        => db.HostedEventBookings.AsNoTracking()
            .Include(b => b.LeadAppUser)
            .Include(b => b.HostedEvent).ThenInclude(e => e.Organization)
            .Include(b => b.HostedEvent).ThenInclude(e => e.Place)
            .Include(b => b.Nights).ThenInclude(n => n.HostedEventNight)
            .Include(b => b.Nights).ThenInclude(n => n.HostedEventLayoutUnit).ThenInclude(u => u!.PlaceRoom)
            .FirstOrDefaultAsync(b => b.Id == bookingId, ct);

    private static List<string> Where(HostedEventBooking booking)
        => booking.Nights
            .OrderBy(n => n.HostedEventNight.Date)
            .Select(n => $"{n.HostedEventNight.Date:dddd, MMMM d} — {EventCapacity.NameOf(n) ?? "your room"}")
            .ToList();

    private static string Greeting(HostedEventBooking booking)
        => booking.LeadAppUser?.DisplayName is { Length: > 0 } name
            ? $"Hello {Safe(name)},"
            : "Hello,";

    private static string Safe(string? value) => NotificationText.Safe(value ?? string.Empty);

    private static string? Trimmed(string? value)
        => value?.Trim() is { Length: > 0 } v ? v : null;
}
