using Ben.Data.Common.Enums;
using Ben.Data.Source.Entities;

namespace Ben.Data.WebApi.Services.Events;

/// <summary>
/// What the staff room says when bookings arrive, and what its thread is called (item 238C).
/// </summary>
/// <remarks>
/// <para><b>The same news as the letters, in the place people already talk.</b> A venue that wants
/// to discuss a request has nowhere to do it that is attached to the request; and a member with no
/// email address, or one who turned the letters off, hears nothing at all. The thread answers both
/// without inventing a second notion of what "new" means — the timing comes from
/// <see cref="EventBookingAlerts"/>, which the letters use too.</para>
///
/// <para><b>Pure, so the words can be tested without a database.</b> Composing HTML is where a
/// guest's name slips into something it should not be in, and that is worth being able to assert
/// about directly.</para>
/// </remarks>
public static class EventStaffRoom
{
    /// <summary>How many parties a post lists before it says "and N more".</summary>
    /// <remarks>
    /// The same ten the letters use. A post that scrolls is a list somebody skims looking for the
    /// one they care about, which is what the booking board is for.
    /// </remarks>
    public const int ListedAtMost = 10;

    /// <summary>The subject the thread carries for the life of the event.</summary>
    /// <remarks>
    /// Named for the event, not for a booking: one thread per event is the whole point, so a
    /// weekend that sells out does not bury every other conversation the group is having.
    /// </remarks>
    public static string SubjectFor(HostedEvent ev) => $"Bookings — {ev.Name}";

    /// <summary>The opening post, written once when the thread is first needed.</summary>
    public static string Opening(HostedEvent ev)
        => $"<p>Requests and holds for <strong>{Escape(ev.Name)}</strong> are posted here as they "
         + "arrive, so anyone running this event can see them and talk about them in one place.</p>"
         + "<p>Names, addresses and dietary notes are deliberately not posted: they are on the "
         + "booking board, behind the permission the board checks.</p>";

    /// <summary>
    /// One reply, about bookings that have arrived.
    /// </summary>
    /// <param name="bookings">Oldest first.</param>
    /// <param name="summary">True when this covers a rush rather than a single arrival.</param>
    /// <returns>Sanitized-by-construction HTML, or null when there is nothing to say.</returns>
    public static string? Arrivals(
        HostedEvent ev, IReadOnlyList<HostedEventBooking> bookings, bool summary, string boardUrl)
    {
        if (bookings.Count == 0) return null;

        var body = new System.Text.StringBuilder();

        if (bookings.Count == 1)
        {
            body.Append($"<p>{Escape(EventOrganizerMailer.Describe(bookings[0], withWhere: true))}.</p>");
        }
        else
        {
            body.Append(summary
                ? $"<p>{bookings.Count} more bookings have arrived:</p>"
                : $"<p>{bookings.Count} bookings have arrived:</p>");

            body.Append("<ul>");
            foreach (var booking in bookings.Take(ListedAtMost))
                body.Append($"<li>{Escape(EventOrganizerMailer.Describe(booking, withWhere: true))}.</li>");
            body.Append("</ul>");

            if (bookings.Count > ListedAtMost)
                body.Append($"<p>…and {bookings.Count - ListedAtMost} more.</p>");
        }

        body.Append($"<p><a href=\"{Escape(boardUrl)}\">Open the booking board</a></p>");
        return body.ToString();
    }

    /// <summary>
    /// Everything that reaches the post goes through here.
    /// </summary>
    /// <remarks>
    /// An event's name is whatever somebody typed, and a unit can be named after a room somebody
    /// named. The sanitizer would catch a script tag on the way in, but this is composed HTML that
    /// never passes through it — so it escapes at the point of composition rather than trusting a
    /// step that is not in this path.
    /// </remarks>
    private static string Escape(string? value)
        => System.Net.WebUtility.HtmlEncode(value ?? string.Empty);
}
