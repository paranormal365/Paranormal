using Ben.Data.Common.Enums;
using Ben.Data.Source.Context;
using Microsoft.EntityFrameworkCore;

namespace Ben.Data.WebApi.Services.Events;

/// <summary>
/// Who a host's letter to the guests reaches, and when it may be sent (item 235 phase 17a, audit finding A4).
/// </summary>
/// <remarks>
/// <para><b>The party's lead, once.</b> A booking is one party with one address; a letter to four people in the Blue
/// Room is one letter to the person who booked it, as every other letter about a booking is.</para>
///
/// <para><b>"There that night"</b> means holding a place on that date, or holding a pass for the whole event — a day
/// pass with no nights is a pass for every one of them, as the door already reads it.</para>
///
/// <para><b>A limit, because it is mail.</b> Ten a day per event: enough for "the car park has flooded" and three
/// follow-ups, and a stop before a stuck button or a host in a temper becomes forty letters.</para>
/// </remarks>
public static class HostedEventAnnouncements
{
    public const int MaxPerDay = 10;
    public const int MaxSubject = 160;
    public const int MaxBody = 4000;

    public sealed record Recipient(Guid LeadAppUserId, string? Email, string? Name, int People);

    public static async Task<IReadOnlyList<Recipient>> RecipientsAsync(
        BenDataContext db, Guid eventId, Guid? nightId, bool includeUnconfirmed, CancellationToken ct)
    {
        var bookings = await db.HostedEventBookings.AsNoTracking()
            .Where(b => b.HostedEventId == eventId
                     && (b.Status == HostedEventBookingStatus.Confirmed
                         || (includeUnconfirmed && (b.Status == HostedEventBookingStatus.Requested
                                                    || b.Status == HostedEventBookingStatus.Held))))
            .Where(b => nightId == null
                     || b.Nights.Any(n => n.HostedEventNightId == nightId && n.ReleasedUtc == null)
                     || b.Nights.All(n => n.ReleasedUtc != null))
            .Select(b => new { b.LeadAppUserId, b.LeadAppUser.Email, b.LeadAppUser.DisplayName, b.PartySize })
            .ToListAsync(ct);

        return [.. bookings
            .GroupBy(b => b.LeadAppUserId)
            .Select(g => new Recipient(g.Key, g.First().Email, g.First().DisplayName, g.Sum(b => b.PartySize)))];
    }

    /// <summary>Why a letter can't go now, in words for the host; null when it can.</summary>
    public static async Task<string?> WhyNotAsync(
        BenDataContext db, Guid eventId, SendHostedEventAnnouncementLimits request, DateTime now, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.Subject)) return "Give the letter a subject — it's the line people see first.";
        if (request.Subject.Trim().Length > MaxSubject) return $"Keep the subject under {MaxSubject} characters.";
        if (string.IsNullOrWhiteSpace(request.Body)) return "Write something to send.";
        if (request.Body.Trim().Length > MaxBody) return $"Keep the letter under {MaxBody:N0} characters.";

        var state = await db.HostedEvents.AsNoTracking().Where(e => e.Id == eventId).Select(e => e.LifecycleState).FirstAsync(ct);
        if (HostedEventStates.CalledOff.Contains(state))
            return "This event was called off, and everybody with a place was told. There's no one left to write to.";
        if (!HostedEventStates.OnThePublicSite.Contains(state))
            return "Publish the event first — until then nobody has a place to be written to about.";

        var today = await db.HostedEventAnnouncements.CountAsync(a => a.HostedEventId == eventId && a.SentUtc > now.AddDays(-1), ct);
        if (today >= MaxPerDay)
            return $"You've written to your guests {MaxPerDay} times in the last day. Wait a little, or put the rest in one letter.";

        return null;
    }

    /// <summary>The parts of the request the limits read.</summary>
    public sealed record SendHostedEventAnnouncementLimits(string? Subject, string? Body);
}
