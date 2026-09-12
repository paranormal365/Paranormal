using Ben.Data.Common.Enums;
using Ben.Data.Source.Context;
using Ben.Data.Source.Entities;
using Microsoft.EntityFrameworkCore;

namespace Ben.Data.WebApi.Services.Events;

/// <summary>
/// The email door into a hosted event: one path for a stranger who asks, and for a host who asks
/// on their behalf (item 235 phase 2).
/// </summary>
/// <remarks>
/// <para><b>One door, not a second one.</b> Somebody with no account gets to a hosted event the
/// same way they get to any other public event here — they give an address, a link is sent to it,
/// and clicking it proves the address and makes a passwordless account. Building a second token,
/// a second expiry and a second "are you really you" for events would have given this site two
/// answers to the same question, and the older one would have gone on being the one that was
/// maintained.</para>
///
/// <para><b>What is different is what the click lands as.</b> On an ordinary public event,
/// confirming means you are coming. On a hosted event nobody is coming until the venue says so
/// (DECISION 7), so the click writes a day-pass BOOKING in the Requested pile and an umbrella
/// attendee row that holds nothing — exactly the shape a walk's requested seat has. The host
/// confirms it from their own board, and that is the moment it starts counting.</para>
///
/// <para><b>The email door only ever sells a day pass.</b> Sleeping somewhere means choosing
/// rooms night by night, and an emailed link is not a booking form — a stranger cannot be asked
/// to pick the Blue Room in a hyperlink. A venue that sells no day passes says so plainly and
/// names the page where a room can be asked for, rather than accepting an address it will have
/// nothing to do with.</para>
/// </remarks>
public static class HostedEventGuestDoor
{
    /// <summary>The hosted event an umbrella calendar row stands for, or null for anything else.</summary>
    /// <remarks>
    /// Loaded rather than passed, because every caller has the calendar row in hand and none of
    /// them should have to know that the umbrella is what a hosted event looks like from outside.
    /// </remarks>
    public static Task<HostedEvent?> BehindAsync(
        BenDataContext db, OrgCalendarEvent ev, CancellationToken ct)
        => ev.HostedEventId is not { } id
            ? Task.FromResult<HostedEvent?>(null)
            : db.HostedEvents.FirstOrDefaultAsync(e => e.Id == id, ct);

    /// <summary>
    /// Why this event cannot be asked for by email, or null when it can.
    /// </summary>
    /// <remarks>
    /// <para>Each refusal names what to do instead. "No" on its own sends somebody to look for a
    /// phone number the page may not carry.</para>
    /// </remarks>
    /// <param name="theHostSentThisLink">
    /// Relaxes the deadline, and nothing else. A host who wrote to somebody after bookings closed
    /// made that call themselves, and refusing their own link at the moment it is used would be
    /// the site overruling the venue about its own weekend. It does not relax the rest: an event
    /// that is called off, unpublished or selling no day passes takes nobody, whoever asked.
    /// </param>
    public static string? WhyTheEmailDoorIsClosed(
        HostedEvent hosted, DateTime utcNow, bool theHostSentThisLink = false)
    {
        if (hosted.CancelledAtUtc is not null)
            return "This event has been called off.";
        if (!hosted.IsPublished || hosted.ArchivedAtUtc is not null)
            return "This event isn't taking bookings.";
        if (!theHostSentThisLink && !EventCapacity.IsOpenForRequests(hosted, utcNow))
            return "This event has stopped taking bookings.";
        if (hosted.DayPassCapacity is 0)
            return "This event doesn't sell day passes. Open its page and ask the venue for a room.";

        return null;
    }

    /// <summary>
    /// Writes the day-pass request this person's confirmed link stands for, unless they have one.
    /// </summary>
    /// <remarks>
    /// <para>Idempotent on the one rule the guest's own door keeps: <b>one live booking per person
    /// per event</b>. Somebody who was sent two links, or who asked and was then invited by the
    /// host, is one party either way — and a second row would have the venue decide twice on the
    /// same people.</para>
    ///
    /// <para>A party size is carried from the invitation rather than asked at the link, because
    /// the number was part of what was asked for and the person reading the email is not looking
    /// at the form they typed it into.</para>
    ///
    /// <para>Nothing is saved here. The caller owns the transaction, so an account created without
    /// the booking it was created for cannot happen.</para>
    /// </remarks>
    public static async Task<HostedEventBooking?> AddDayPassRequestAsync(
        BenDataContext db, HostedEvent hosted, Guid leadAppUserId, int? partySize,
        string? note, CancellationToken ct)
    {
        var existing = await db.HostedEventBookings
            .FirstOrDefaultAsync(b => b.HostedEventId == hosted.Id
                                   && b.LeadAppUserId == leadAppUserId
                                   && b.Status != HostedEventBookingStatus.Cancelled, ct);
        if (existing is not null) return null;

        var booking = new HostedEventBooking
        {
            Id = Guid.NewGuid(),
            HostedEventId = hosted.Id,
            LeadAppUserId = leadAppUserId,
            PartySize = EventCapacity.ClampPartySize(partySize),
            Kind = HostedEventBookingKind.DayPass,
            Status = HostedEventBookingStatus.Requested,
            Note = note?.Trim() is { Length: > 0 } n ? n : null,
            DateCreated = DateTime.UtcNow,
            CreatedByAppUserId = leadAppUserId,
        };
        db.HostedEventBookings.Add(booking);
        return booking;
    }
}
