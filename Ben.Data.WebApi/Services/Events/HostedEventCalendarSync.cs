using Ben.Data.Source.Context;
using Ben.Data.Source.Entities;
using Microsoft.EntityFrameworkCore;

namespace Ben.Data.WebApi.Services.Events;

/// <summary>
/// Keeps a hosted event's one umbrella calendar row saying what the event says (item 235).
/// </summary>
/// <remarks>
/// <para><b>Why an umbrella row exists at all.</b> A great deal of this site already understands a
/// public calendar event: the public list, the event page and its share tags, the twenty-four-hour
/// reminder, the calendar file, evidence submission, the walk-up invite, the
/// <c>/o/{org}/events/{slug}</c> address — and the app already in people's pockets, which cannot be
/// changed until Apple has finished reviewing it. Giving every hosted event one ordinary public
/// calendar row means all of that works on the first day, for nothing.</para>
///
/// <para><b>Why exactly one, and not one per night.</b> Three nights of one weekend are one thing
/// that happens. Three calendar rows would show a visitor the same event three times on the public
/// list and offer them three sign-ups to the same weekend. The nights are children of the event and
/// the umbrella spans the lot, from the first date's start to the last date's end.</para>
///
/// <para><b>The row is written here and nowhere else.</b> Its title, description, dates, place,
/// zone, slug and public flag are the event's, copied down. The calendar controller refuses a
/// direct edit with a sentence naming the event, because two screens that both believe they own a
/// row is how one of them ends up lying to somebody.</para>
/// </remarks>
public sealed class HostedEventCalendarSync
{
    /// <summary>Doors at seven in the evening, when nobody has said otherwise.</summary>
    public static readonly TimeSpan DefaultStart = new(19, 0, 0);

    /// <summary>And done at eleven. Only ever a fallback; every screen offers the real times.</summary>
    public static readonly TimeSpan DefaultEnd = new(23, 0, 0);

    /// <summary>
    /// What the public list shows: enough of the description to be worth reading, no more.
    /// </summary>
    /// <remarks>
    /// The calendar row's description is a plain summary, not the event's full markup. The rich
    /// version lives on the event and is what the page renders; duplicating it here would be two
    /// copies of the same prose drifting apart.
    /// </remarks>
    public const int SummaryLength = 500;

    /// <summary>
    /// Creates or updates the umbrella row for <paramref name="hostedEvent"/>, leaving it in the
    /// change tracker for the caller to save.
    /// </summary>
    /// <remarks>
    /// Does not save, on purpose: creating an event, generating its nights and writing its umbrella
    /// row are one act, and half of it landing is worse than none of it.
    /// </remarks>
    public async Task<OrgCalendarEvent> SyncAsync(
        BenDataContext db, HostedEvent hostedEvent, Guid userId, CancellationToken ct = default)
    {
        var nights = hostedEvent.Nights.Count > 0
            ? hostedEvent.Nights.OrderBy(n => n.Date).ToList()
            : await db.HostedEventNights
                .Where(n => n.HostedEventId == hostedEvent.Id)
                .OrderBy(n => n.Date)
                .ToListAsync(ct);

        var row = await db.OrgCalendarEvents
            .FirstOrDefaultAsync(e => e.HostedEventId == hostedEvent.Id, ct);

        var now = DateTime.UtcNow;
        if (row is null)
        {
            row = new OrgCalendarEvent
            {
                Id = Guid.NewGuid(),
                OrganizationId = hostedEvent.OrganizationId,
                HostedEventId = hostedEvent.Id,
                DateCreated = now,
                CreatedByAppUserId = userId,
            };
            db.OrgCalendarEvents.Add(row);
        }
        else
        {
            row.DateUpdated = now;
            row.UpdatedByAppUserId = userId;
        }

        row.Title = hostedEvent.CancelledAtUtc is null
            ? hostedEvent.Name
            // Said in the title because that is the one line every list, every share card and every
            // phone notification shows. A cancelled event that reads like a live one is the worst
            // thing on this screen.
            : $"CANCELLED — {hostedEvent.Name}";

        row.Description = Summarise(hostedEvent.Description ?? hostedEvent.Tagline);
        row.PlaceId = hostedEvent.PlaceId;
        row.HideExactLocation = hostedEvent.HideExactLocation;
        row.TimeZoneId = hostedEvent.TimeZoneId;
        row.UrlName = hostedEvent.UrlName;

        // Only a published event is public. An unpublished one keeps its row so nothing has to be
        // created and destroyed as somebody changes their mind, and the row simply does not show.
        row.IsPublic = hostedEvent.IsPublished && hostedEvent.ArchivedAtUtc is null;

        var (startsUtc, endsUtc) = Window(hostedEvent, nights);
        row.StartDateTime = startsUtc;
        row.EndDateTime = endsUtc;
        row.IsAllDay = false;

        return row;
    }

    /// <summary>
    /// The whole event as one span in UTC: the first date's start to the last date's end.
    /// </summary>
    /// <remarks>
    /// <para>Computed in the event's own zone and converted, never taken from the server's clock.
    /// An event at the Thomas House starts at seven Central whatever the rack in the data centre
    /// thinks the time is.</para>
    ///
    /// <para>A run of separate dates spans its whole run, which is right for a list entry — "on
    /// until November" — and is why each date takes its own bookings rather than the umbrella
    /// taking them.</para>
    ///
    /// <para>An end before its start is nonsense a reader would see, so the fallback is an hour
    /// after the start rather than a negative span.</para>
    /// </remarks>
    public static (DateTime StartUtc, DateTime EndUtc) Window(
        HostedEvent hostedEvent, IReadOnlyList<HostedEventNight> nights)
    {
        var zone = ZoneOf(hostedEvent.TimeZoneId);

        var firstDate = nights.Count > 0 ? nights[0].Date.Date : hostedEvent.StartsOn.Date;
        var lastDate = nights.Count > 0 ? nights[^1].Date.Date : hostedEvent.EndsOn.Date;

        var startLocal = nights.Count > 0 ? nights[0].StartLocal : null;
        var endLocal = nights.Count > 0 ? nights[^1].EndLocal : null;

        var startsAt = firstDate + (startLocal ?? hostedEvent.DefaultStartLocal ?? DefaultStart);
        var endsAt = lastDate + (endLocal ?? hostedEvent.DefaultEndLocal ?? DefaultEnd);

        var startUtc = ToUtc(startsAt, zone);
        var endUtc = ToUtc(endsAt, zone);

        if (endUtc <= startUtc) endUtc = startUtc.AddHours(1);
        return (startUtc, endUtc);
    }

    /// <summary>The event's zone, or UTC when the id is one this machine has never heard of.</summary>
    /// <remarks>
    /// Falling back rather than throwing: a zone database that disagrees with the one the row was
    /// written on must not take a page down. The times are then wrong by an offset, which is
    /// visible and fixable, rather than absent, which is not.
    /// </remarks>
    public static TimeZoneInfo ZoneOf(string? timeZoneId)
    {
        if (string.IsNullOrWhiteSpace(timeZoneId)) return TimeZoneInfo.Utc;
        try { return TimeZoneInfo.FindSystemTimeZoneById(timeZoneId); }
        catch (TimeZoneNotFoundException) { return TimeZoneInfo.Utc; }
        catch (InvalidTimeZoneException) { return TimeZoneInfo.Utc; }
    }

    private static DateTime ToUtc(DateTime local, TimeZoneInfo zone)
    {
        var unspecified = DateTime.SpecifyKind(local, DateTimeKind.Unspecified);

        // The hour a clock skips forward does not exist, and asking for it throws. An event booked
        // into it is somebody's typing mistake, so it is nudged an hour rather than refused at the
        // point where nobody can see what went wrong.
        if (zone.IsInvalidTime(unspecified)) unspecified = unspecified.AddHours(1);

        return TimeZoneInfo.ConvertTimeToUtc(unspecified, zone);
    }

    /// <summary>Plain text, shortened at a word, for a list entry.</summary>
    private static string? Summarise(string? markup)
    {
        if (string.IsNullOrWhiteSpace(markup)) return null;

        var text = System.Text.RegularExpressions.Regex
            .Replace(markup, "<[^>]+>", " ")
            .Replace("&nbsp;", " ");
        text = System.Text.RegularExpressions.Regex.Replace(text, @"\s+", " ").Trim();

        if (text.Length <= SummaryLength) return text.Length == 0 ? null : text;

        var cut = text[..SummaryLength];
        var lastSpace = cut.LastIndexOf(' ');
        if (lastSpace > SummaryLength / 2) cut = cut[..lastSpace];
        return cut.TrimEnd(',', '.', ';', ':') + "…";
    }

    /// <summary>
    /// The dates an event should have, given its span — one per day for a stay.
    /// </summary>
    /// <remarks>
    /// Only for a stay. A run's dates are chosen one at a time by the host, because a monthly show
    /// is not "every day from January to December" and generating three hundred of them would be a
    /// wrong answer delivered quickly.
    /// </remarks>
    public static IEnumerable<DateTime> DatesOfAStay(DateTime startsOn, DateTime endsOn)
    {
        for (var day = startsOn.Date; day <= endsOn.Date; day = day.AddDays(1))
            yield return day;
    }
}
