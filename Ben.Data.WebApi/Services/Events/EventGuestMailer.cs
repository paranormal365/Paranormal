using Ben.Data.Common.Mail;
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
                ReplyTo: ev?.Organization?.PublicEmail, Kind: MailKinds.BookingDecided.Key), ct);

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

        // Before the pass, because it is what somebody plans the journey around (audit finding A7).
        if (Trimmed(ev?.AccessNotes) is { } access)
            body.Append($"<p><strong>Getting in and getting around:</strong> {Safe(access).Replace("\n", "<br />")}</p>");

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

    /// <summary>
    /// Tells a guest their hold ran out before the venue answered.
    /// </summary>
    /// <remarks>
    /// <para><b>Its own letter, not the released one.</b> "Your booking has been released" reads as
    /// a decision somebody made about them; this is the opposite — nobody decided anything, the
    /// clock ran out, and they are still on the list. Sending the wrong one of the two would have a
    /// guest believe they had been turned down.</para>
    ///
    /// <para>Says when it lapsed, in the venue's own time, because "when?" is the first thing
    /// anybody asks and the first thing the venue needs in order to answer them.</para>
    /// </remarks>
    /// <returns>True when a letter was sent.</returns>
    public async Task<bool> SendHoldLapsedAsync(
        BenDataContext db, HostedEventBooking booking, CancellationToken ct)
    {
        if (!_email.IsConfigured) return false;

        var loaded = await LoadAsync(db, booking.Id, ct);
        if (loaded is null) return false;

        var to = loaded.LeadAppUser?.Email;
        if (string.IsNullOrWhiteSpace(to)) return false;

        var ev = loaded.HostedEvent;
        var name = Safe(ev?.Name ?? "the event");

        var body = new System.Text.StringBuilder();
        body.Append($"<p>{Greeting(loaded)}</p>");
        body.Append($"<p>The places you chose at <strong>{name}</strong> were held for you until "
                  + $"{WhenItLapsed(loaded, ev)}, and the venue had not answered by then — so they "
                  + "have gone back and somebody else may take them.</p>");
        body.Append("<p><strong>You are still on the venue's list.</strong> They can still offer "
                  + "you a place, and you are welcome to choose again if what you wanted is still "
                  + "free.</p>");

        await _email.SendAsync(new EmailMessage(
            to,
            $"The places you chose at {ev?.Name ?? "the event"} have gone back",
            body.ToString(),
            ReplyTo: ev?.Organization?.PublicEmail, Kind: MailKinds.HoldLapsed.Key), ct);

        return true;
    }

    /// <summary>
    /// Tells a guest their ask arrived, and what it does not mean (item 235 phase 6).
    /// </summary>
    /// <remarks>
    /// <para><b>Because silence reads as a booking.</b> Somebody who filled in a form and heard
    /// nothing assumes it worked; a guest who assumed that about a request arrives at a hotel with
    /// a suitcase and no room. So the letter exists to say the opposite in as many words: this is
    /// a request, nothing is held, and the venue will answer.</para>
    ///
    /// <para>No calendar file, deliberately. A diary entry for a place nobody has agreed to is the
    /// same lie in a different form.</para>
    /// </remarks>
    /// <returns>True when a letter was sent.</returns>
    public async Task<bool> SendAskedAsync(BenDataContext db, Guid bookingId, CancellationToken ct)
    {
        if (!_email.IsConfigured) return false;

        var booking = await LoadAsync(db, bookingId, ct);
        if (booking?.LeadAppUser?.Email is not { Length: > 0 } to) return false;

        var ev = booking.HostedEvent;
        var name = Safe(ev?.Name ?? "the event");
        var venue = Safe(ev?.Organization?.Name ?? "the venue");

        var body = new System.Text.StringBuilder();
        body.Append($"<p>{Greeting(booking)}</p>");
        body.Append($"<p>We've passed your request for a place at <strong>{name}</strong> to "
                  + $"{venue}. They'll answer you, and you'll hear from us either way.</p>");
        body.Append(WhatTheyAskedFor(booking));
        body.Append("<p><strong>Nothing is held yet.</strong> A request joins the venue's list — "
                  + "they may put you somewhere other than you asked for, and they'll say so when "
                  + "they answer.</p>");
        body.Append("<p>Nothing is paid through this site.</p>");

        await _email.SendAsync(new EmailMessage(
            to,
            $"We've passed your request for {ev?.Name ?? "the event"} on",
            body.ToString(),
            ReplyTo: ev?.Organization?.PublicEmail, Kind: MailKinds.BookingAsked.Key), ct);

        return true;
    }

    /// <summary>
    /// Tells a guest the places they chose are being held, and until when (item 235 phase 6).
    /// </summary>
    /// <remarks>
    /// <para><b>The deadline is the letter.</b> A hold that runs out is a decision the clock takes
    /// instead of the venue, and a guest who was never told the time cannot act before it. It is
    /// written in the venue's own zone, because "six o'clock" means the clock on the wall where
    /// the seats are.</para>
    ///
    /// <para>Still no calendar file: held is not confirmed, and a diary entry would say it was.
    /// The confirmation letter carries the diary, and the pass.</para>
    /// </remarks>
    /// <returns>True when a letter was sent.</returns>
    public async Task<bool> SendHoldPlacedAsync(
        BenDataContext db, Guid bookingId, CancellationToken ct)
    {
        if (!_email.IsConfigured) return false;

        var booking = await LoadAsync(db, bookingId, ct);
        if (booking?.LeadAppUser?.Email is not { Length: > 0 } to) return false;

        var ev = booking.HostedEvent;
        var name = Safe(ev?.Name ?? "the event");
        var venue = Safe(ev?.Organization?.Name ?? "the venue");

        var body = new System.Text.StringBuilder();
        body.Append($"<p>{Greeting(booking)}</p>");
        body.Append($"<p>The places you chose at <strong>{name}</strong> are being held for you "
                  + $"until <strong>{WhenItLapsed(booking, ev)}</strong>, while {venue} answers "
                  + "you. Nobody else can take them in the meantime.</p>");
        body.Append(WhatTheyAskedFor(booking));
        body.Append("<p>If the venue hasn't answered by then, the places go back and you're "
                  + "welcome to choose again — you stay on their list either way.</p>");
        body.Append("<p>Nothing is paid through this site.</p>");

        await _email.SendAsync(new EmailMessage(
            to,
            $"Your places at {ev?.Name ?? "the event"} are held",
            body.ToString(),
            ReplyTo: ev?.Organization?.PublicEmail, Kind: MailKinds.HoldPlaced.Key), ct);

        return true;
    }

    /// <summary>
    /// Sends the link that turns places picked without signing in into a real hold (slice 11d).
    /// </summary>
    /// <remarks>
    /// <para><b>The deadline is in the subject's first sentence</b>, because fifteen minutes is short
    /// and a letter read at leisure is a letter read too late.</para>
    ///
    /// <para><b>"If this wasn't you, do nothing"</b> — the address has not been proved, so this may be
    /// going to somebody who never visited the site. Nothing is held for them beyond the fifteen
    /// minutes and no account exists until the button is pressed.</para>
    ///
    /// <para>When mail is not set up the link is logged instead, as the public sign-up link is, so a
    /// developer's machine and the browser tests can still follow it.</para>
    /// </remarks>
    /// <returns>True when a letter was written.</returns>
    public async Task<bool> SendEmailPickLinkAsync(
        HostedEventEmailPick pick, HostedEvent ev, IReadOnlyList<string> places, string token,
        CancellationToken ct)
    {
        var link = _site.AbsoluteUrl($"/event-picks/{token}");

        if (!_email.IsConfigured)
        {
            _log.LogInformation(
                "Email is not configured; the pick link for {Email} was not sent. Pick token: {Token}",
                pick.Email, token);
            return false;
        }

        var name = Safe(ev.Name);
        var org = Safe(ev.Organization?.Name ?? "the organizer");

        var body = new System.Text.StringBuilder();
        body.Append($"<p>Hello {Safe(pick.FirstName)},</p>");
        body.Append($"<p>You picked places at <strong>{name}</strong>. They are waiting for you until "
                  + $"<strong>{AtTheVenue(pick.ExpiresUtc, ev)}</strong> — press the button to hold them.</p>");
        if (places.Count > 0)
            body.Append($"<ul><li>{string.Join("</li><li>", places.Select(Safe))}</li></ul>");
        body.Append($"<p><a href=\"{link}\">Hold my places</a></p>");
        body.Append($"<p>Once they are held, {org} answers you, and nobody else can take them in the "
                  + "meantime. Nothing is paid through this site.</p>");
        body.Append($"<p>{Safe(BookingContact.Disclosure(ev.Organization?.Name))}</p>");
        body.Append("<p>If this wasn't you, do nothing: the places go back by themselves and no account "
                  + "is made.</p>");

        await _email.SendAsync(new EmailMessage(
            pick.Email,
            $"Hold your places at {ev.Name} within 15 minutes",
            body.ToString(),
            // This one prints a deadline through AtTheVenue, which labels nothing — so the footer
            // says which clock that deadline is on. Missing it by hours is the whole risk here.
            ReplyTo: ev.Organization?.PublicEmail, Kind: MailKinds.ChooseYourEmails.Key,
            TimesShownInZone: ZoneLabel(ev)), ct);

        return true;
    }

    /// <summary>
    /// Thanks a party for coming, the morning after the last night (item 235 phase 12).
    /// </summary>
    /// <remarks>
    /// <para>Ben, 2026-09-13: <i>"If we generate a gallery, we can send that link in a thank you link
    /// including any upcoming events hosted by the organizer."</i></para>
    ///
    /// <para>The organizer's own words first, then the pictures if there are any, then where to say how
    /// it was, then what the group has coming — the letter that closes one weekend is the one most
    /// likely to fill the next.</para>
    /// </remarks>
    public async Task<bool> SendThankYouAsync(
        HostedEventBooking booking, bool hasGallery, IReadOnlyList<HostedEvent> upcoming, CancellationToken ct)
    {
        if (!_email.IsConfigured) return false;
        if (booking.LeadAppUser?.Email is not { Length: > 0 } to) return false;

        var (subject, body) = ThankYouLetter(booking, hasGallery, upcoming, _site.AbsoluteUrl);
        await _email.SendAsync(new EmailMessage(to, subject, body,
            ReplyTo: booking.HostedEvent?.Organization?.PublicEmail, Kind: MailKinds.EventThankYou.Key), ct);
        return true;
    }

    /// <summary>The thank-you's words, separate so they can be read in a test without a mail server.</summary>
    internal static (string Subject, string Body) ThankYouLetter(
        HostedEventBooking booking, bool hasGallery, IReadOnlyList<HostedEvent> upcoming, Func<string, string> absolute)
    {
        var ev = booking.HostedEvent;
        var org = ev.Organization;
        var page = absolute($"/o/{org?.UrlName}/events/{ev.UrlName}");

        var body = new System.Text.StringBuilder();
        body.Append($"<p>{Greeting(booking)}</p>");
        body.Append($"<p>Thank you for coming to <strong>{Safe(ev.Name)}</strong>.</p>");

        if (ev.ThankYouNote is { Length: > 0 } note)
        {
            foreach (var paragraph in note.Split(["\r\n\r\n", "\n\n"], StringSplitOptions.RemoveEmptyEntries))
                body.Append($"<p>{Safe(paragraph).Replace("\n", "<br>")}</p>");
            body.Append($"<p>— {Safe(org?.Name)}</p>");
        }

        if (hasGallery)
            body.Append($"<p><a href=\"{page}#hosted-gallery\">See the pictures</a></p>");

        if (ev.AllowReviews)
            body.Append($"<p><a href=\"{absolute($"/my-events/{ev.Id}/review")}\">Say how it was</a> — "
                      + "a few words help the next people deciding whether to come.</p>");

        if (upcoming.Count > 0)
        {
            body.Append($"<p><strong>Coming up with {Safe(org?.Name)}</strong></p><ul>");
            foreach (var next in upcoming)
                body.Append($"<li><a href=\"{absolute($"/o/{org?.UrlName}/events/{next.UrlName}")}\">{Safe(next.Name)}</a>"
                          + $" · {next.StartsOn:ddd MM/dd/yyyy}</li>");
            body.Append("</ul>");
        }

        return ($"Thank you for coming to {ev.Name}", body.ToString());
    }

    /// <summary>
    /// Which clock this event's letters are written on, for the footnote — or null when there is
    /// nothing worth saying.
    /// </summary>
    /// <remarks>
    /// <para>Ben, 2026-09-20: say so when a letter only reports UTC. The hazard is specific, and it
    /// is <see cref="AtTheVenue"/>'s: an event with no <c>TimeZoneId</c> resolves to UTC, the
    /// conversion SUCCEEDS, and the time is printed with <b>no label at all</b> — so the reader
    /// sees "7:00 PM" and reads it as their own evening. A mail client, unlike a browser, tells us
    /// nothing about where the reader is, so the letter has to name the clock it means.</para>
    ///
    /// <para>An event WITH a zone still gets a note, worded more quietly: the venue's clock is the
    /// right one to print, and it is still not necessarily the reader's.</para>
    /// </remarks>
    internal static string ZoneLabel(HostedEvent? ev)
    {
        if (string.IsNullOrWhiteSpace(ev?.TimeZoneId)) return "UTC";

        try
        {
            var zone = TimeZoneInfo.FindSystemTimeZoneById(ev.TimeZoneId);
            if (zone == TimeZoneInfo.Utc) return "UTC";

            var name = zone.IsDaylightSavingTime(DateTime.UtcNow) ? zone.DaylightName : zone.StandardName;
            var words = name.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            return words.Length >= 2 ? new string([.. words.Select(w => w[0])]) : name;
        }
        catch (Exception e) when (e is TimeZoneNotFoundException or InvalidTimeZoneException)
        {
            // The same fallback AtTheVenue takes, so the footnote cannot disagree with the times.
            return "UTC";
        }
    }

    /// <summary>A moment on the venue's clock, as the letters write it.</summary>
    internal static string AtTheVenue(DateTime utc, HostedEvent? ev)
    {
        try
        {
            var zone = TimeZoneInfo.FindSystemTimeZoneById(ev?.TimeZoneId ?? "UTC");
            var local = TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(utc, DateTimeKind.Utc), zone);
            return local.ToString("h:mm tt on MM/dd/yyyy");
        }
        catch (Exception e) when (e is TimeZoneNotFoundException or InvalidTimeZoneException)
        {
            return utc.ToString("h:mm tt on MM/dd/yyyy") + " UTC";
        }
    }

    /// <summary>
    /// Tells everybody with a place that the event is off (item 235 phase 6).
    /// </summary>
    /// <remarks>
    /// <para><b>Because the organizer's screen has been claiming this for three phases.</b>
    /// Calling an event off answered "everybody with a place has been told", and nothing sent
    /// anything: the guests found out by opening the page, or by turning up. The sentence was
    /// written in phase 3 and this is the letter it was promising.</para>
    ///
    /// <para><b>Everybody still waiting counts</b>, not only the confirmed: somebody holding
    /// places or waiting on an answer has kept the date free just as hard, and is owed the same
    /// letter. A party the venue already turned down is not written to — they were told once and
    /// telling them again about an event they are not coming to is noise.</para>
    ///
    /// <para>Best effort, one letter at a time: a single address that bounces must not stop the
    /// rest of a weekend's guests being told.</para>
    /// </remarks>
    /// <returns>How many letters went.</returns>
    public async Task<int> SendCalledOffAsync(
        BenDataContext db, Guid hostedEventId, string? reason, CancellationToken ct)
    {
        if (!_email.IsConfigured) return 0;

        var sent = 0;

        foreach (var booking in await LiveBookingsAsync(db, hostedEventId, ct))
        {
            if (booking.LeadAppUser?.Email is not { Length: > 0 } to) continue;

            var ev = booking.HostedEvent;
            var name = Safe(ev?.Name ?? "the event");

            var body = new System.Text.StringBuilder();
            body.Append($"<p>{Greeting(booking)}</p>");
            body.Append($"<p><strong>{name} is not going ahead.</strong> Your places have gone "
                      + "back, and there is nothing left for you to do.</p>");

            if (Trimmed(reason) is { } why)
                body.Append($"<p>{Safe(ev?.Organization?.Name ?? "The venue")} said: “{Safe(why)}”</p>");

            body.Append("<p>Nothing was paid through this site, so there is nothing to refund "
                      + "here. Anything you arranged directly with the venue is between you and "
                      + "them.</p>");

            try
            {
                await _email.SendAsync(new EmailMessage(
                    to, $"{ev?.Name ?? "An event"} is not going ahead", body.ToString(),
                    ReplyTo: ev?.Organization?.PublicEmail, Kind: MailKinds.EventCalledOff.Key), ct);
                sent++;
            }
            catch (Exception e) when (e is not OperationCanceledException)
            {
                _log.LogWarning(e,
                    "Could not tell {BookingId} that its event was called off.", booking.Id);
            }
        }

        return sent;
    }

    /// <summary>
    /// Tells everybody with a place that the venue has withdrawn, and so the event is off
    /// (item 235 phase 9).
    /// </summary>
    /// <remarks>
    /// Its own letter rather than the called-off one, because who called it off is the whole of what
    /// a guest wants to know. "The Nashville Paranormal Society is not going ahead" when the society
    /// did nothing wrong would send forty cross emails to the wrong people.
    /// </remarks>
    /// <returns>How many letters went.</returns>
    public async Task<int> SendVenueWithdrewAsync(
        BenDataContext db, Guid hostedEventId, string venueName, string reason, CancellationToken ct)
    {
        if (!_email.IsConfigured) return 0;

        var sent = 0;

        foreach (var booking in await LiveBookingsAsync(db, hostedEventId, ct))
        {
            if (booking.LeadAppUser?.Email is not { Length: > 0 } to) continue;

            var ev = booking.HostedEvent;
            var name = Safe(ev?.Name ?? "the event");

            var body = new System.Text.StringBuilder();
            body.Append($"<p>{Greeting(booking)}</p>");
            body.Append($"<p><strong>{name} is not going ahead.</strong> {Safe(venueName)}, the venue, "
                      + "has withdrawn permission for it to be held there. Your places have gone, and "
                      + "your pass no longer admits anybody.</p>");
            body.Append($"<p>{Safe(venueName)} said: “{Safe(reason)}”</p>");
            body.Append($"<p>{Safe(ev?.Organization?.Name ?? "The organizer")} did not call this off, "
                      + "and may be able to tell you whether it is happening somewhere else. Nothing was "
                      + "paid through this site, so there is nothing to refund here.</p>");

            try
            {
                await _email.SendAsync(new EmailMessage(
                    to, $"{ev?.Name ?? "An event"} is not going ahead", body.ToString(),
                    ReplyTo: ev?.Organization?.PublicEmail, Kind: MailKinds.EventCalledOff.Key), ct);
                sent++;
            }
            catch (Exception e) when (e is not OperationCanceledException)
            {
                _log.LogWarning(e,
                    "Could not tell {BookingId} that the venue withdrew.", booking.Id);
            }
        }

        return sent;
    }

    /// <summary>
    /// Tells everybody waiting that the numbers were reached and it is definitely on.
    /// </summary>
    /// <remarks>
    /// The other half of a minimum number. A guest asked to keep a weekend free while a venue
    /// counts heads has been holding a date on a maybe; the decision is the moment that stops
    /// being true, and hearing it is what turns a maybe into a booked train.
    /// </remarks>
    /// <returns>How many letters went.</returns>
    public async Task<int> SendGoingAheadAsync(
        BenDataContext db, Guid hostedEventId, CancellationToken ct)
    {
        if (!_email.IsConfigured) return 0;

        var sent = 0;

        foreach (var booking in await LiveBookingsAsync(db, hostedEventId, ct))
        {
            if (booking.LeadAppUser?.Email is not { Length: > 0 } to) continue;

            var ev = booking.HostedEvent;
            var name = Safe(ev?.Name ?? "the event");

            var body = new System.Text.StringBuilder();
            body.Append($"<p>{Greeting(booking)}</p>");
            body.Append($"<p><strong>{name} has the numbers it needed and is going ahead.</strong>"
                      + "</p>");
            body.Append(WhatTheyAskedFor(booking));
            body.Append(booking.Status == HostedEventBookingStatus.Confirmed
                ? "<p>Your place is already confirmed — nothing more to do.</p>"
                : "<p>The venue will answer your booking as usual; this only says the event "
                + "itself is definitely happening.</p>");

            try
            {
                await _email.SendAsync(new EmailMessage(
                    to, $"{ev?.Name ?? "An event"} is going ahead", body.ToString(),
                    ReplyTo: ev?.Organization?.PublicEmail, Kind: MailKinds.EventGoingAhead.Key), ct);
                sent++;
            }
            catch (Exception e) when (e is not OperationCanceledException)
            {
                _log.LogWarning(e,
                    "Could not tell {BookingId} that its event is going ahead.", booking.Id);
            }
        }

        return sent;
    }

    /// <summary>
    /// Asks somebody to help at an event (item 235 phase 7).
    /// </summary>
    /// <remarks>
    /// <para><b>Written for a person who may never have heard of this site.</b> A hotel's weekend
    /// steward is invited by a venue they work for, not by us: the letter says which event, which
    /// venue, which group, what they are being asked to be able to do, and nothing about
    /// accounts.</para>
    ///
    /// <para><b>It lists the permissions in words.</b> Somebody accepting an invitation is
    /// accepting access to other people's names and, sometimes, their dietary notes — that is a
    /// thing to be told before clicking rather than after.</para>
    ///
    /// <para>Here rather than in a mailer of its own because this class already knows how to write
    /// a letter about an event: the venue's name, the reply-to, the safe encoding. A second class
    /// would be a second set of those to keep true.</para>
    /// </remarks>
    /// <returns>True when a letter was sent.</returns>
    public async Task<bool> SendStaffInviteAsync(
        BenDataContext db, Guid staffId, CancellationToken ct)
    {
        if (!_email.IsConfigured) return false;

        var staff = await db.HostedEventStaff.AsNoTracking()
            .Include(s => s.AppUser)
            .Include(s => s.HostedEvent).ThenInclude(e => e.Organization)
            .Include(s => s.HostedEvent).ThenInclude(e => e.Place)
            .FirstOrDefaultAsync(s => s.Id == staffId, ct);

        if (staff?.Token is not { Length: > 0 } token) return false;

        var to = staff.Email ?? staff.AppUser?.Email;
        if (string.IsNullOrWhiteSpace(to)) return false;

        var ev = staff.HostedEvent;
        var name = Safe(ev?.Name ?? "an event");
        var venue = Safe(ev?.Organization?.Name ?? "A group");
        var url = _site.AbsoluteUrl($"/helping/{token}");

        var body = new System.Text.StringBuilder();
        body.Append($"<p>{Safe(staff.DisplayName ?? staff.AppUser?.DisplayName ?? "Hello")},</p>");
        body.Append($"<p><strong>{venue}</strong> has asked you to help at <strong>{name}</strong>"
                  + $"{Place(ev)}{When(ev)}.</p>");

        if (staff.RoleLabel is { Length: > 0 } role)
            body.Append($"<p>They have you down as <strong>{Safe(role)}</strong>.</p>");

        body.Append("<p>Accepting lets you:</p><ul><li>"
                  + string.Join("</li><li>", WhatTheyCanDo(staff)) + "</li></ul>");

        body.Append($"<p><a href=\"{url}\">Say yes and see what you need</a></p>");
        body.Append("<p>The link works once and lasts a fortnight. If you were not expecting this, "
                  + "ignore it — nothing happens until you click.</p>");

        await _email.SendAsync(new EmailMessage(
            to,
            $"Can you help at {ev?.Name ?? "an event"}?",
            body.ToString(),
            ReplyTo: ev?.Organization?.PublicEmail, Kind: MailKinds.StaffInvite.Key), ct);

        return true;
    }

    /// <summary>
    /// What a staff row lets somebody do, in words a person can weigh before accepting.
    /// </summary>
    /// <remarks>
    /// Public because the acceptance page shows the same list, from the same method: a letter and
    /// a page that described the same permissions differently would be two promises.
    /// </remarks>
    public static IReadOnlyList<string> WhatTheyCanDo(HostedEventStaff staff)
    {
        var can = new List<string>();

        if (staff.RunsTheDoor)
            can.Add("scan passes at the door and mark people in");
        if (staff.SeesBookings || staff.Decides)
            can.Add("see who is coming, including their names and anything they cannot eat");
        if (staff.Decides)
            can.Add("say yes or no to bookings");
        if (staff.SeesMenus)
            can.Add("read and write the menus, and read the kitchen's sheet");
        if (staff.SeesFiles)
            can.Add("read the event's files");

        // Never an empty list: the endpoint refuses a helper who may do nothing, and a letter
        // saying "accepting lets you:" followed by nothing would be worse than not sending it.
        if (can.Count == 0) can.Add("help with this event");

        return can;
    }

    private static string Place(Source.Entities.HostedEvent? ev)
        => ev?.Place?.Name is { Length: > 0 } venue ? $" at {Safe(venue)}" : "";

    private static string When(Source.Entities.HostedEvent? ev)
        => ev is null ? "" : $", {ev.StartsOn:dddd, MMMM d}";

    /// <summary>Everybody with a place or waiting for one — the people an event's news is for.</summary>
    private static async Task<List<HostedEventBooking>> LiveBookingsAsync(
        BenDataContext db, Guid hostedEventId, CancellationToken ct)
        => await db.HostedEventBookings.AsNoTracking()
            .Include(b => b.LeadAppUser)
            .Include(b => b.HostedEvent).ThenInclude(e => e.Organization)
            .Include(b => b.Nights).ThenInclude(n => n.HostedEventNight)
            .Include(b => b.Nights).ThenInclude(n => n.HostedEventLayoutUnit).ThenInclude(u => u!.PlaceRoom)
            .Where(b => b.HostedEventId == hostedEventId
                     && (b.Status == HostedEventBookingStatus.Requested
                      || b.Status == HostedEventBookingStatus.Held
                      || b.Status == HostedEventBookingStatus.Confirmed))
            .ToListAsync(ct);

    /// <summary>The nights and rooms, or the day, in the words the guest used.</summary>
    private static string WhatTheyAskedFor(HostedEventBooking booking)
    {
        var people = $"{booking.PartySize} {(booking.PartySize == 1 ? "person" : "people")}";

        if (booking.Kind == HostedEventBookingKind.DayPass && booking.Nights.Count == 0)
            return $"<p>For the day · {people}.</p>";

        var where = Where(booking);
        if (where.Count == 0) return $"<p>{people}.</p>";

        return $"<p>{people}:</p><ul><li>{string.Join("</li><li>", where.Select(Safe))}</li></ul>";
    }

    /// <summary>When the hold ran out, on the venue's clock rather than the server's.</summary>
    private static string WhenItLapsed(HostedEventBooking booking, HostedEvent? ev)
    {
        if (booking.HoldExpiresUtc is not { } at) return "the deadline";

        try
        {
            var zone = TimeZoneInfo.FindSystemTimeZoneById(ev?.TimeZoneId ?? "UTC");
            var local = TimeZoneInfo.ConvertTimeFromUtc(
                DateTime.SpecifyKind(at, DateTimeKind.Utc), zone);
            return local.ToString("h:mm tt on MM/dd/yyyy");
        }
        catch (Exception e) when (e is TimeZoneNotFoundException or InvalidTimeZoneException)
        {
            return at.ToString("h:mm tt on MM/dd/yyyy") + " UTC";
        }
    }

    // ── the diary ────────────────────────────────────────────────────────────

    /// <summary>Six in the evening at the venue: when "you are staying here tonight" begins.</summary>
    private static readonly TimeSpan NightBegins = new(18, 0, 0);

    /// <summary>Ten the next morning at the venue: when it ends.</summary>
    private static readonly TimeSpan NightEnds = new(10, 0, 0);

    /// <summary>Two in the morning after the last date, for a day pass that runs late.</summary>
    private static readonly TimeSpan DayPassEnds = new(2, 0, 0);

    /// <summary>
    /// One calendar entry per booked night, or one for the whole event on a day pass.
    /// </summary>
    /// <remarks>
    /// <para>Each night keeps its own uid, so a later letter updates each entry rather than every
    /// night overwriting the last and leaving a guest with one entry for a three-night stay.</para>
    ///
    /// <para><b>Every time here is a wall-clock time at the venue</b>, converted to UTC through the
    /// event's own zone — the same zone <see cref="HostedEventCalendarSync"/> uses for the public
    /// list, so the diary and the list can never disagree about where the Thomas House is. The
    /// first version of this wrote <c>Date.AddHours(18)</c> and handed it to a builder that stamps
    /// everything as UTC, which put a Nashville dinner in a guest's calendar at one in the
    /// afternoon (item 235 phase 1). A night is also converted at each end rather than as a start
    /// plus sixteen hours: on the weekend the clocks go back, the Saturday night is an hour longer
    /// and a fixed span would have put the Sunday check-out at nine.</para>
    /// </remarks>
    private byte[] CalendarFor(HostedEventBooking booking, HostedEvent? ev)
    {
        if (ev is null) return [];

        var zone = HostedEventCalendarSync.ZoneOf(ev.TimeZoneId);
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
                StartUtc: AtVenueUtc(n.HostedEventNight.Date, NightBegins, zone),
                EndUtc: AtVenueUtc(n.HostedEventNight.Date.AddDays(1), NightEnds, zone),
                Summary: $"{ev.Name} — {EventCapacity.NameOf(n, booking.Kind)}",
                Description: null,
                Location: venue,
                Url: absolute,
                OrganizerName: ev.Organization?.Name,
                OrganizerEmail: ev.Organization?.PublicEmail)).ToList()
            : [new IcsBuilder.IcsEvent(
                Uid: $"{booking.Id}@ishaunted.com",
                StartUtc: AtVenueUtc(ev.StartsOn, NightBegins, zone),
                EndUtc: AtVenueUtc(ev.EndsOn.AddDays(1), DayPassEnds, zone),
                Summary: ev.Name,
                Description: null,
                Location: venue,
                Url: absolute,
                OrganizerName: ev.Organization?.Name,
                OrganizerEmail: ev.Organization?.PublicEmail)];

        return IcsBuilder.BuildBytes(entries);
    }

    /// <summary>A wall-clock time on a date at the venue, as the instant it actually is.</summary>
    /// <remarks>
    /// The zone is <see cref="HostedEventCalendarSync.ZoneOf"/>'s, falling back to UTC for an id
    /// this machine has never heard of, so a strange zone costs an offset and never the letter.
    /// Six in the evening and ten in the morning never fall inside the hour a clock skips, but the
    /// conversion throws if one ever did, and a thrown exception here loses the whole diary for
    /// the sake of one entry — so it is nudged an hour, the way the umbrella row's times are.
    /// </remarks>
    private static DateTime AtVenueUtc(DateTime date, TimeSpan timeOfDay, TimeZoneInfo zone)
    {
        var local = DateTime.SpecifyKind(date.Date + timeOfDay, DateTimeKind.Unspecified);
        if (zone.IsInvalidTime(local)) local = local.AddHours(1);
        return TimeZoneInfo.ConvertTimeToUtc(local, zone);
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
            .Select(n => $"{n.HostedEventNight.Date:dddd, MMMM d} — {EventCapacity.NameOf(n, booking.Kind)}")
            .ToList();

    // ── the programme (phase 10) ─────────────────────────────────────────────

    /// <summary>
    /// Tells guests who were waiting that a place in a session came free and is now theirs.
    /// </summary>
    /// <remarks>
    /// The whole point of a queue. Moving somebody up without telling them leaves an empty chair in
    /// the Ovilus class and a guest who thinks they are still waiting.
    /// </remarks>
    public Task<int> SendSessionPromotedAsync(BenDataContext db, IReadOnlyList<Guid> signUpIds, CancellationToken ct)
        => SendToSignUpsAsync(db, s => signUpIds.Contains(s.Id),
            (session, when) => ($"You're in: {session.Title}",
                $"<p>A place came free in <strong>{Safe(session.Title)}</strong> at "
                + $"{Safe(session.HostedEvent.Name)}, and it's yours — {Safe(when)}.</p>"
                + "<p>If you can't come after all, leave it from the programme so the next person gets it.</p>"),
            ct);

    /// <summary>Tells everybody signed up — or waiting — that a session is off.</summary>
    public Task<int> SendSessionCancelledAsync(BenDataContext db, Guid sessionId, string? reason, CancellationToken ct)
        => SendToSignUpsAsync(db, s => s.HostedEventSessionId == sessionId,
            (session, when) => ($"Cancelled: {session.Title}",
                $"<p><strong>{Safe(session.Title)}</strong> at {Safe(session.HostedEvent.Name)} ({Safe(when)}) "
                + "has been cancelled.</p>"
                + (Trimmed(reason) is { } why ? $"<p>{Safe(session.HostedEvent.Organization?.Name ?? "The organizers")} said: “{Safe(why)}”</p>" : "")
                + "<p>Your place at the event itself is unchanged.</p>"),
            ct);

    /// <summary>Tells everybody signed up that a session's time or place has changed.</summary>
    public Task<int> SendSessionMovedAsync(BenDataContext db, Guid sessionId, CancellationToken ct)
        => SendToSignUpsAsync(db, s => s.HostedEventSessionId == sessionId && s.WaitlistedUtc == null,
            (session, when) => ($"Changed: {session.Title}",
                $"<p><strong>{Safe(session.Title)}</strong> at {Safe(session.HostedEvent.Name)} has moved. "
                + $"It is now {Safe(when)}.</p><p>You still have your place.</p>"),
            ct);

    /// <summary>
    /// A host's letter to the people coming (phase 17a, audit finding A4). One letter per party's lead; returns how many
    /// left. The words are the host's, escaped and with their line breaks kept; the footer says why the guest got it.
    /// </summary>
    public async Task<int> SendAnnouncementAsync(
        Source.Entities.HostedEvent ev, string organizationName, string? replyTo, string subject, string text,
        IReadOnlyList<HostedEventAnnouncements.Recipient> recipients, CancellationToken ct)
    {
        if (!_email.IsConfigured) return 0;

        var page = _site.AbsoluteUrl($"/o/{ev.Organization?.UrlName}/events/{ev.UrlName}");
        var sent = 0;
        foreach (var recipient in recipients)
        {
            if (recipient.Email is not { Length: > 0 } to) continue;
            var greeting = recipient.Name is { Length: > 0 } name ? $"<p>Hello {Safe(name)},</p>" : "<p>Hello,</p>";
            var body = greeting
                     + $"<p>{Safe(text.Trim()).Replace("\n", "<br />")}</p>"
                     + $"<p>— {Safe(organizationName)}</p>"
                     + $"<p style=\"color:#666;font-size:13px\">You're getting this because you have a place at "
                     + $"<a href=\"{page}\">{Safe(ev.Name)}</a>. Reply to write back to {Safe(organizationName)}.</p>";
            try
            {
                await _email.SendAsync(new EmailMessage(to, $"{ev.Name}: {subject.Trim()}", body, ReplyTo: replyTo, Kind: MailKinds.EventAnnouncement.Key), ct);
                sent++;
            }
            catch (Exception e) when (e is not OperationCanceledException)
            {
                _log.LogWarning(e, "Could not send an announcement for event {EventId} to {LeadId}.", ev.Id, recipient.LeadAppUserId);
            }
        }
        return sent;
    }

    /// <summary>
    /// Tells the organizer's side that IsHaunted removed their event, with the way to appeal (item 235 phase 17b).
    /// </summary>
    /// <remarks>
    /// Generic on purpose. What the reviewer noted stays on the removal: a letter that argues the case invites an argument
    /// by reply, and a specific reason can name a person who complained. The appeal is where the organizer makes their
    /// case, and a person reads it.
    /// </remarks>
    public async Task<int> SendRemovedAsync(
        Source.Entities.HostedEvent ev, string organizationName, bool creditReturned,
        IReadOnlyList<(Guid Id, string? Email, string? Name)> recipients, CancellationToken ct)
    {
        if (!_email.IsConfigured) return 0;

        var appeal = _site.AbsoluteUrl($"/organizations/{ev.OrganizationId}/events/{ev.Id}#event-removed");
        var sent = 0;
        foreach (var (id, email, name) in recipients)
        {
            if (email is not { Length: > 0 } to) continue;
            var body = (name is { Length: > 0 } n ? $"<p>Hello {Safe(n)},</p>" : "<p>Hello,</p>")
                     + $"<p><strong>{Safe(ev.Name)}</strong>, hosted by {Safe(organizationName)}, has been removed from "
                     + $"{Safe(_site.Name)} because it doesn't meet our guidelines for hosted events.</p>"
                     + "<p>It is no longer on the site and can't take bookings. Anybody who had a place or had asked for one "
                     + "has been told it is not going ahead.</p>"
                     + (creditReturned
                         ? "<p>The event credit spent on it has been returned, and can be used for another event.</p>"
                         : "")
                     + "<p><strong>If you think this was a mistake</strong>, you can appeal. Tell us what the event is and "
                     + "anything you've changed, and a person will review it. If the appeal is upheld the event comes back "
                     + "as a draft, ready for you to publish again.</p>"
                     + $"<p><a href=\"{appeal}\">Appeal this decision</a></p>";
            try
            {
                await _email.SendAsync(new EmailMessage(to, $"{ev.Name} was removed from {_site.Name}", body, Kind: MailKinds.GuestRemoved.Key), ct);
                sent++;
            }
            catch (Exception e) when (e is not OperationCanceledException)
            {
                _log.LogWarning(e, "Could not tell {PersonId} that event {EventId} was removed.", id, ev.Id);
            }
        }
        return sent;
    }

    /// <summary>Tells the organizer's side how their appeal was answered (item 235 phase 17b).</summary>
    public async Task<int> SendAppealAnsweredAsync(
        Source.Entities.HostedEvent ev, bool upheld, string? note,
        IReadOnlyList<(Guid Id, string? Email, string? Name)> recipients, CancellationToken ct)
    {
        if (!_email.IsConfigured) return 0;

        var page = _site.AbsoluteUrl($"/organizations/{ev.OrganizationId}/events/{ev.Id}");
        var sent = 0;
        foreach (var (id, email, name) in recipients)
        {
            if (email is not { Length: > 0 } to) continue;
            var body = (name is { Length: > 0 } n ? $"<p>Hello {Safe(n)},</p>" : "<p>Hello,</p>")
                     + (upheld
                         ? $"<p>Your appeal was upheld. <strong>{Safe(ev.Name)}</strong> is back as a draft. Check it over "
                           + "and publish it when you're ready; publishing spends an event credit or a slot, as before.</p>"
                         : $"<p>Your appeal about <strong>{Safe(ev.Name)}</strong> was reviewed, and the event stays removed.</p>")
                     + (note is { Length: > 0 } said ? $"<p>The reviewer said: “{Safe(said)}”</p>" : "")
                     + $"<p><a href=\"{page}\">Open the event</a></p>";
            try
            {
                await _email.SendAsync(new EmailMessage(to,
                    upheld ? $"{ev.Name} is back as a draft" : $"Your appeal about {ev.Name}", body, Kind: MailKinds.AppealAnswered.Key), ct);
                sent++;
            }
            catch (Exception e) when (e is not OperationCanceledException)
            {
                _log.LogWarning(e, "Could not tell {PersonId} about the appeal on event {EventId}.", id, ev.Id);
            }
        }
        return sent;
    }

    private async Task<int> SendToSignUpsAsync(
        BenDataContext db, System.Linq.Expressions.Expression<Func<HostedEventSessionSignUp, bool>> which,
        Func<HostedEventSession, string, (string Subject, string Body)> write, CancellationToken ct)
    {
        if (!_email.IsConfigured) return 0;

        var signUps = await db.HostedEventSessionSignUps.AsNoTracking()
            .Include(s => s.AppUser)
            .Include(s => s.HostedEventSession).ThenInclude(x => x.HostedEvent).ThenInclude(e => e.Organization)
            .Include(s => s.HostedEventSession).ThenInclude(x => x.PlaceRoom)
            .Where(which)
            .ToListAsync(ct);

        var sent = 0;
        foreach (var signUp in signUps)
        {
            if (signUp.AppUser?.Email is not { Length: > 0 } to) continue;
            var session = signUp.HostedEventSession;
            var (subject, body) = write(session, WhenAndWhere(session));
            var greeting = signUp.AppUser.DisplayName is { Length: > 0 } name ? $"<p>Hello {Safe(name)},</p>" : "<p>Hello,</p>";

            try
            {
                // WhenAndWhere writes the session's times with no zone beside them, so the footer
                // has to name the clock — see ZoneLabel.
                await _email.SendAsync(new EmailMessage(to, subject, greeting + body,
                    ReplyTo: session.HostedEvent.Organization?.PublicEmail, Kind: MailKinds.AppealAnswered.Key,
                    TimesShownInZone: ZoneLabel(session.HostedEvent)), ct);
                sent++;
            }
            catch (Exception e) when (e is not OperationCanceledException)
            {
                _log.LogWarning(e, "Could not write to sign-up {SignUpId}.", signUp.Id);
            }
        }

        return sent;
    }

    /// <summary>"Sat 10/31 9:00 PM–10:00 PM, in the Ballroom" — on the venue's clock.</summary>
    internal static string WhenAndWhere(HostedEventSession session)
    {
        var zone = HostedEventCalendarSync.ZoneOf(session.HostedEvent?.TimeZoneId);
        var start = TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(session.StartsAtUtc, DateTimeKind.Utc), zone);
        var end = TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(session.EndsAtUtc, DateTimeKind.Utc), zone);
        var where = session.PlaceRoom?.Name ?? session.LocationText;
        return $"{start:ddd MM/dd h:mm tt}–{end:h:mm tt}" + (where is { Length: > 0 } w ? $", in {w}" : "");
    }

    private static string Greeting(HostedEventBooking booking)
        => booking.LeadAppUser?.DisplayName is { Length: > 0 } name
            ? $"Hello {Safe(name)},"
            : "Hello,";

    private static string Safe(string? value) => NotificationText.Safe(value ?? string.Empty);

    private static string? Trimmed(string? value)
        => value?.Trim() is { Length: > 0 } v ? v : null;
}
