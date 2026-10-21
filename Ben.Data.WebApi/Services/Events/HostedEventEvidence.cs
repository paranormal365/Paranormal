using Ben.Data.Common.Enums;
using Ben.Data.Source.Context;
using Ben.Data.Source.Entities;
using Microsoft.EntityFrameworkCore;

namespace Ben.Data.WebApi.Services.Events;

/// <summary>
/// A hosted event's evidence after the night: who may offer it, until when, what the letters say
/// about it, and the order the accepted pieces are shown in (event-scenario-gaps, 10/21/2026).
/// </summary>
/// <remarks>
/// <para>Ben's scenario: <i>"The letter told them the event evidence would be available in about a
/// week and he was accepting evidence for three days."</i> and <i>"The evidence submitted he decided
/// to show becomes ranked by number of positive votes then by the time of the evidence."</i></para>
///
/// <para><b>Who came.</b> The door records arrivals per night (<see cref="HostedEventCheckIn"/>), and
/// older passes carry their own stamp. An event whose door recorded nobody at all cannot tell who came,
/// so every confirmed party counts as having come — the thank-you goes to all of them, as it always
/// did, and nobody is told they were missed.</para>
/// </remarks>
public static class HostedEventEvidence
{
    /// <summary>The longest evidence window an organizer may set.</summary>
    public const int MaxEvidenceDays = 60;

    /// <summary>The furthest out an organizer may promise the results.</summary>
    public const int MaxResultsInDays = 90;

    /// <summary>When evidence stops being taken, or null when the organizer set no deadline.</summary>
    public static DateTime? DeadlineUtc(HostedEvent ev, IReadOnlyList<HostedEventNight> nights)
        => ev.EvidenceDays is int days
            ? HostedEventCalendarSync.Window(ev, [.. nights.OrderBy(n => n.Date)]).EndUtc.AddDays(days)
            : null;

    /// <summary>Why the evidence door is shut now, or null while it is open.</summary>
    public static string? WhyClosed(HostedEvent ev, IReadOnlyList<HostedEventNight> nights, DateTime now)
    {
        if (!ev.CollectsEvidence) return "This event isn't taking evidence.";
        return DeadlineUtc(ev, nights) is { } deadline && now > deadline
            ? $"This event stopped taking evidence on {EventGuestMailer.AtTheVenue(deadline, ev)}."
            : null;
    }

    /// <summary>
    /// What the after-event letters say about the evidence, or null when there is nothing to say.
    /// </summary>
    /// <remarks>Plain words, no markup; the letter escapes them.</remarks>
    public static string? WindowSentence(HostedEvent ev, IReadOnlyList<HostedEventNight> nights)
    {
        if (!ev.CollectsEvidence) return null;

        var parts = new List<string>();
        if (DeadlineUtc(ev, nights) is { } deadline && ev.EvidenceDays is int days)
            parts.Add($"We're accepting evidence for {DaysInWords(days)}, until {EventGuestMailer.AtTheVenue(deadline, ev)}.");
        if (ev.ResultsInDays is int results)
            parts.Add($"The event's evidence will be ready to see in {AboutInWords(results)}.");

        return parts.Count == 0 ? null : string.Join(" ", parts);
    }

    /// <summary>"one day", "three days", "21 days".</summary>
    public static string DaysInWords(int days)
    {
        string[] words = ["zero", "one", "two", "three", "four", "five", "six", "seven", "eight", "nine", "ten",
                          "eleven", "twelve", "thirteen", "fourteen"];
        var number = days >= 0 && days < words.Length ? words[days] : days.ToString(System.Globalization.CultureInfo.InvariantCulture);
        return days == 1 ? "one day" : $"{number} days";
    }

    /// <summary>"about a week", "about two weeks", "about five days".</summary>
    public static string AboutInWords(int days) => days switch
    {
        7 => "about a week",
        14 => "about two weeks",
        21 => "about three weeks",
        28 or 30 or 31 => "about a month",
        _ => $"about {DaysInWords(days)}",
    };

    /// <summary>Whether this person had a confirmed place: the lead booker, or a guest they named.</summary>
    /// <remarks>
    /// The evidence door used to ask only for an emailed invitation or membership, so a guest who
    /// booked a hosted event while signed in — the ordinary way — was told they hadn't attended.
    /// </remarks>
    public static Task<bool> HadAPlaceAsync(BenDataContext db, Guid hostedEventId, Guid userId, CancellationToken ct)
        => db.HostedEventBookings.AnyAsync(b => b.HostedEventId == hostedEventId
                                             && b.Status == HostedEventBookingStatus.Confirmed
                                             && (b.LeadAppUserId == userId || b.Guests.Any(g => g.AppUserId == userId)), ct);

    /// <summary>
    /// The bookings that came: the door's scans, corrected by the organizer's own marks after the event
    /// (the organizer's word wins). Null when neither the door nor the organizer said anything at all.
    /// </summary>
    public static async Task<HashSet<Guid>?> CameAsync(BenDataContext db, Guid hostedEventId, CancellationToken ct)
    {
        var atTheDoor = await AtTheDoorAsync(db, hostedEventId, ct);
        var marks = await db.HostedEventBookings.AsNoTracking()
            .Where(b => b.HostedEventId == hostedEventId && b.OrganizerSaysCame != null)
            .Select(b => new { b.Id, Came = b.OrganizerSaysCame!.Value })
            .ToListAsync(ct);

        if (atTheDoor.Count == 0 && marks.Count == 0) return null;

        var came = atTheDoor;
        foreach (var mark in marks)
            if (mark.Came) came.Add(mark.Id); else came.Remove(mark.Id);
        return came;
    }

    /// <summary>The bookings the door checked in on any night.</summary>
    public static async Task<HashSet<Guid>> AtTheDoorAsync(BenDataContext db, Guid hostedEventId, CancellationToken ct)
    {
        var checkedIn = await db.HostedEventCheckIns.AsNoTracking()
            .Where(c => c.HostedEventBooking.HostedEventId == hostedEventId)
            .Select(c => c.HostedEventBookingId).Distinct().ToListAsync(ct);
        var passed = await db.HostedEventPasses.AsNoTracking()
            .Where(p => p.HostedEventBooking.HostedEventId == hostedEventId && p.CheckedInUtc != null)
            .Select(p => p.HostedEventBookingId).Distinct().ToListAsync(ct);
        return checkedIn.Concat(passed).ToHashSet();
    }

    /// <summary>
    /// The order accepted evidence is shown in: most positive votes first, then the earliest caught.
    /// </summary>
    /// <param name="positiveVotes">"Confirms" votes on the piece.</param>
    /// <param name="caughtUtc">When it was recorded, from the file itself; when the file says nothing, when it was offered.</param>
    public static IEnumerable<T> Rank<T>(IEnumerable<T> items, Func<T, int> positiveVotes, Func<T, DateTime> caughtUtc)
        => items.OrderByDescending(positiveVotes).ThenBy(caughtUtc);

    /// <summary>"Confirms" votes per file, for the files given.</summary>
    public static async Task<Dictionary<Guid, int>> PositiveVotesAsync(
        BenDataContext db, IReadOnlyCollection<Guid> uploadFileIds, CancellationToken ct)
    {
        if (uploadFileIds.Count == 0) return [];
        return await db.EvidenceVotes.AsNoTracking()
            .Where(v => uploadFileIds.Contains(v.UploadFileId) && v.VoteType == EvidenceVoteType.Confirms)
            .GroupBy(v => v.UploadFileId)
            .Select(g => new { g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.Key, x => x.Count, ct);
    }

    /// <summary>When each file was caught, where the file says.</summary>
    public static async Task<Dictionary<Guid, DateTime>> CaughtAsync(
        BenDataContext db, IReadOnlyCollection<Guid> uploadFileIds, CancellationToken ct)
    {
        if (uploadFileIds.Count == 0) return [];
        var rows = await db.UploadFileMetadata.AsNoTracking()
            .Where(m => uploadFileIds.Contains(m.UploadFileId) && m.CapturedAtUtc != null)
            .Select(m => new { m.UploadFileId, m.CapturedAtUtc })
            .ToListAsync(ct);
        return rows.GroupBy(r => r.UploadFileId).ToDictionary(g => g.Key, g => g.Min(r => r.CapturedAtUtc!.Value));
    }

    /// <summary>
    /// Where the results are: the organizer's own page, or the event's page at its evidence.
    /// </summary>
    public static string ResultsPath(HostedEvent ev, string orgUrlName)
        => ev.ResultsUrl is { Length: > 0 } url ? url : $"/o/{orgUrlName}/events/{ev.UrlName}#event-evidence";

    /// <summary>Why this results address can't be used, or null when it can.</summary>
    /// <remarks>A path on this site, or an https address. Nothing else goes in a letter to every guest.</remarks>
    public static string? WhyNotAResultsUrl(string? url)
    {
        if (string.IsNullOrWhiteSpace(url)) return null;
        url = url.Trim();
        if (url.Length > 500) return "That address is longer than 500 characters.";
        if (url.StartsWith('/') && !url.StartsWith("//")) return null;
        return Uri.TryCreate(url, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps
            ? null
            : "Give a page on this site (starting with /) or a full address starting with https://.";
    }
}
