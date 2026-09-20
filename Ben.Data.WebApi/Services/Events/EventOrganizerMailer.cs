using Ben.Data.Common.Mail;
using Ben.Data.Common;
using Ben.Data.Common.Enums;
using Ben.Data.Common.Interfaces;
using Ben.Data.Source.Entities;
using Microsoft.Extensions.Options;

namespace Ben.Data.WebApi.Services.Events;

/// <summary>
/// The letters a venue gets about its own bookings (item 235 phase 8).
/// </summary>
/// <remarks>
/// <para><b>What, when and where — never who.</b> A letter says a party of four asked for the Blue
/// Room on the Friday and when their hold lapses; the name, the address and what they cannot eat
/// are on the board, behind the permission the board checks. Mail is forwarded, printed and left
/// open on shared desks in a way a screen is not, and a letter that carried contact details would
/// be a copy of the booking outside every rule that protects it.</para>
///
/// <para><b>Separate from the guest's mailer</b> because they are written to different people about
/// different things, and a class that wrote both would sooner or later send one audience's letter
/// to the other.</para>
/// </remarks>
public sealed class EventOrganizerMailer
{
    /// <summary>How many parties a letter lists before it says "and N more".</summary>
    /// <remarks>
    /// Ten. Past that a letter stops being something to read and becomes a list somebody scrolls
    /// looking for the one they care about, which is exactly what the board is for.
    /// </remarks>
    internal const int ListedAtMost = 10;

    private readonly IEmailService _email;
    private readonly SiteIdentity _site;

    public EventOrganizerMailer(IEmailService email, IOptions<SiteIdentity> site)
    { _email = email; _site = site.Value; }

    public bool IsConfigured => _email.IsConfigured;

    /// <summary>
    /// Tells somebody about bookings that have arrived: the first of a rush, or its summary.
    /// </summary>
    /// <param name="bookings">Oldest first, with their nights and units loaded.</param>
    /// <returns>True when a letter went.</returns>
    public async Task<bool> SendArrivalsAsync(
        EventBookingRecipients.Recipient to, HostedEvent ev,
        IReadOnlyList<HostedEventBooking> bookings, bool summary, CancellationToken ct)
    {
        if (!_email.IsConfigured || bookings.Count == 0) return false;

        var name = Safe(ev.Name);
        var first = bookings[0];

        var subject = bookings.Count == 1
            ? $"{Describe(first, withWhere: false)} at {ev.Name}"
            : summary
                ? $"{bookings.Count} more bookings at {ev.Name}"
                : $"{bookings.Count} new bookings at {ev.Name}";

        var body = new System.Text.StringBuilder();
        body.Append($"<p>{Greeting(to)}</p>");

        if (bookings.Count == 1)
        {
            body.Append($"<p>{Safe(Describe(first, withWhere: true))} at <strong>{name}</strong>."
                      + $"{Safe(Lapses(first, ev))}</p>");
        }
        else
        {
            body.Append(summary
                ? $"<p>While you were busy, {bookings.Count} more bookings arrived at "
                  + $"<strong>{name}</strong>:</p>"
                : $"<p>{bookings.Count} bookings have arrived at <strong>{name}</strong>:</p>");

            body.Append("<ul>");
            foreach (var booking in bookings.Take(ListedAtMost))
                body.Append($"<li>{Safe(Describe(booking, withWhere: true))}.{Safe(Lapses(booking, ev))}</li>");
            body.Append("</ul>");

            if (bookings.Count > ListedAtMost)
                body.Append($"<p>…and {bookings.Count - ListedAtMost} more.</p>");
        }

        body.Append($"<p><a href=\"{Board(ev)}\">Open the booking board</a></p>");
        body.Append(Footer(ev));

        await _email.SendAsync(new EmailMessage(to.Email, subject, body.ToString(), Kind: MailKinds.BookingsArrived.Key), ct);
        return true;
    }

    /// <summary>
    /// The digest: what is waiting, what is about to lapse, and what is left.
    /// </summary>
    /// <returns>True when a letter went.</returns>
    public async Task<bool> SendDigestAsync(
        EventBookingRecipients.Recipient to, HostedEvent ev, EventBookingDigest.Contents digest,
        CancellationToken ct)
    {
        if (!_email.IsConfigured || digest.IsEmpty) return false;

        var name = Safe(ev.Name);
        var body = new System.Text.StringBuilder();
        body.Append($"<p>{Greeting(to)}</p>");
        body.Append($"<p>Where <strong>{name}</strong> stands:</p>");

        // OLDEST UNDECIDED FIRST. The party who has waited longest is the one most likely to have
        // given up and gone somewhere else, and the letter puts them where the eye lands.
        if (digest.Waiting.Count > 0)
        {
            body.Append($"<p><strong>Waiting on an answer ({digest.Waiting.Count})</strong></p><ul>");
            foreach (var booking in digest.Waiting.Take(ListedAtMost))
                body.Append($"<li>{Safe(Describe(booking, withWhere: true))} — waiting "
                          + $"{Safe(Age(booking.DateCreated, digest.AsOfUtc))}.</li>");
            body.Append("</ul>");
            if (digest.Waiting.Count > ListedAtMost)
                body.Append($"<p>…and {digest.Waiting.Count - ListedAtMost} more.</p>");
        }

        // HOLDS ABOUT TO LAPSE NEXT, because those are decisions the clock will take if nobody
        // does, and there is still time to take them.
        if (digest.Lapsing.Count > 0)
        {
            body.Append($"<p><strong>Holds running out in the next day ({digest.Lapsing.Count})</strong></p><ul>");
            foreach (var booking in digest.Lapsing.Take(ListedAtMost))
                body.Append($"<li>{Safe(Describe(booking, withWhere: true))}.{Safe(Lapses(booking, ev))}</li>");
            body.Append("</ul>");
        }

        if (digest.Left.Count > 0)
        {
            body.Append("<p><strong>What is left</strong></p><ul>");
            foreach (var (section, free) in digest.Left)
                body.Append($"<li>{Safe(section)}: {free} free</li>");
            body.Append("</ul>");
        }

        if (digest.ArrivedLastNight is { } arrived)
            body.Append($"<p>{arrived} {(arrived == 1 ? "person" : "people")} came through the door "
                      + "last night.</p>");

        body.Append($"<p><a href=\"{Board(ev)}\">Open the booking board</a></p>");
        body.Append(Footer(ev));

        await _email.SendAsync(new EmailMessage(
            to.Email, $"Where {ev.Name} stands", body.ToString(), Kind: MailKinds.BookingsDigest.Key), ct);
        return true;
    }

    // ── words ────────────────────────────────────────────────────────────────

    /// <summary>"A party of 4 has asked for the Blue Room on Fri 10/30" — never a name.</summary>
    internal static string Describe(HostedEventBooking booking, bool withWhere)
    {
        var party = booking.PartySize == 1 ? "One person" : $"A party of {booking.PartySize}";

        var verb = booking.Status == HostedEventBookingStatus.Held
            ? "is holding"
            : "has asked for";

        if (!withWhere) return booking.Status == HostedEventBookingStatus.Held
            ? $"{party} is holding places"
            : $"{party} asked for a place";

        var live = booking.Nights.Where(n => n.ReleasedUtc is null).ToList();

        if (booking.Kind == HostedEventBookingKind.DayPass || live.Count == 0)
            return $"{party} {verb} a day pass";

        var units = live
            .Select(n => EventCapacity.NameOf(n, booking.Kind))
            .Where(n => !string.IsNullOrWhiteSpace(n))
            .Distinct()
            .ToList();

        var dates = live
            .Where(n => n.HostedEventNight is not null)
            .Select(n => n.HostedEventNight!.Date)
            .Distinct()
            .OrderBy(d => d)
            .Select(d => d.ToString("ddd MM/dd"))
            .ToList();

        var what = units.Count == 0 ? "a place" : string.Join(", ", units);
        var when = dates.Count == 0 ? "" : $" on {string.Join(", ", dates)}";

        return $"{party} {verb} {what}{when}";
    }

    /// <summary>" Their hold lapses at 3:00 PM on 10/30/2026." — on the venue's clock.</summary>
    private static string Lapses(HostedEventBooking booking, HostedEvent ev)
    {
        if (booking.Status != HostedEventBookingStatus.Held || booking.HoldExpiresUtc is not { } at)
            return "";

        var zone = HostedEventCalendarSync.ZoneOf(ev.TimeZoneId);
        var local = TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(at, DateTimeKind.Utc), zone);

        return $" Their hold lapses at {local:h:mm tt} on {local:MM/dd/yyyy}.";
    }

    private static string Age(DateTime created, DateTime now)
    {
        var waited = now - created;
        return waited.TotalDays >= 2 ? $"{waited.TotalDays:0} days"
             : waited.TotalHours >= 2 ? $"{waited.TotalHours:0} hours"
             : "under two hours";
    }

    private string Board(HostedEvent ev)
        => _site.AbsoluteUrl($"/organizations/{ev.OrganizationId}/events/{ev.Id}/bookings");

    /// <summary>
    /// How to make these stop, in every letter.
    /// </summary>
    /// <remarks>
    /// A letter with no way out is a letter somebody reports as spam, and a venue whose manager has
    /// filtered the site's mail away is a venue nobody can tell anything.
    /// </remarks>
    private string Footer(HostedEvent ev)
        => $"<p style=\"color:#666;font-size:12px\">You get these because you decide bookings for "
         + $"{Safe(ev.Organization?.Name ?? "this group")}. "
         + $"<a href=\"{_site.AbsoluteUrl("/notifications")}\">Change how often</a>.</p>";

    private static string Greeting(EventBookingRecipients.Recipient to)
        => to.Name is { Length: > 0 } name ? $"Hello {Safe(name)}," : "Hello,";

    private static string Safe(string? value) => NotificationText.Safe(value ?? string.Empty);
}
