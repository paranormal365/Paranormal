using Ben.Data.Common.Enums;
using Ben.Data.Source.Context;
using Ben.Data.Source.Entities;
using Microsoft.EntityFrameworkCore;

namespace Ben.Data.WebApi.Services.Events;

/// <summary>
/// The one place a booking's status, its held nights and its umbrella row are written
/// (item 235 phase 4).
/// </summary>
/// <remarks>
/// <para><b>Why one writer.</b> Five places wrote these three things and each combined them
/// differently: the organizer's board, the public booking endpoint, the email door, the phone's
/// RSVP path and the calendar's own attendee screen. The results disagreed — the phone wrote an
/// Accepted attendee with no booking behind it, and cancelling through the calendar left a
/// confirmed booking pointing at a row that had gone. Every one of those is the same bug, and a
/// single writer is the only shape that does not have it.</para>
///
/// <para><b>Three things move together or not at all.</b> A booking's status, whether its nights
/// are still held, and whether the umbrella carries an attendee are one fact expressed three ways.
/// Anything that writes one and not the others has made the site disagree with itself, which is why
/// this class writes all three on every transition and why <c>BookingWriterGuardTests</c> refuses
/// any other file from writing them.</para>
///
/// <para><b>Nothing here saves.</b> Holding a seat and writing the umbrella row are one act, and
/// half of it landing is the worst outcome available. The caller owns the transaction, catches the
/// unique-index violation, and turns it into the sentence a guest reads.</para>
/// </remarks>
public static class BookingTransitions
{
    /// <summary>The statuses that actually hold a place.</summary>
    /// <remarks>
    /// Held and Confirmed. A request holds nothing, which is what lets an Ask event keep taking
    /// requests after it is full and turn the overflow into a waiting list.
    /// </remarks>
    public static readonly HostedEventBookingStatus[] Holding =
    [
        HostedEventBookingStatus.Held,
        HostedEventBookingStatus.Confirmed,
    ];

    /// <summary>The statuses a person is still waiting on an answer for.</summary>
    public static readonly HostedEventBookingStatus[] Waiting =
    [
        HostedEventBookingStatus.Requested,
        HostedEventBookingStatus.Held,
    ];

    /// <summary>Whether this status holds its unit-nights against everybody else.</summary>
    public static bool Holds(HostedEventBookingStatus status) => Holding.Contains(status);

    // ── moving a booking ─────────────────────────────────────────────────────

    /// <summary>
    /// Puts a picked party into Held, with a deadline read from the event.
    /// </summary>
    /// <remarks>
    /// Only ever on a Pick event. On an Ask event the venue allocates, and a guest arriving with
    /// seats already taken would be helping themselves to the thing the venue meant to decide.
    /// </remarks>
    public static void Hold(
        HostedEventBooking booking, HostedEvent hosted, DateTime now)
    {
        if (hosted.BookingMode != HostedEventBookingMode.Pick)
            throw new InvalidOperationException(
                "A place can only be held on an event where guests pick their own.");

        booking.Status = HostedEventBookingStatus.Held;
        booking.HoldExpiresUtc = now.AddMinutes(hosted.HoldMinutes);
        booking.DecidedUtc = null;
        booking.DecisionNote = null;
        Touch(booking, now);
    }

    /// <summary>Puts a party into Requested, which holds nothing.</summary>
    public static void Request(HostedEventBooking booking, DateTime now)
    {
        booking.Status = HostedEventBookingStatus.Requested;
        booking.HoldExpiresUtc = null;
        booking.DecidedUtc = null;
        booking.DecisionNote = null;
        Touch(booking, now);
    }

    /// <summary>The venue agreed. The places are theirs and the hold no longer runs out.</summary>
    public static void Confirm(HostedEventBooking booking, Guid actorId, string? note, DateTime now)
    {
        booking.Status = HostedEventBookingStatus.Confirmed;
        // Cleared, not kept: a confirmed booking has no deadline, and leaving one would have the
        // expiry job take a place the venue has already promised.
        booking.HoldExpiresUtc = null;
        booking.DecidedUtc = now;
        booking.DecidedByAppUserId = actorId;
        booking.DecisionNote = Trimmed(note);
        Touch(booking, now, actorId);
    }

    /// <summary>The venue said no. The places go back.</summary>
    public static void TurnDown(
        HostedEventBooking booking, Guid actorId, string? note, DateTime now)
    {
        booking.Status = HostedEventBookingStatus.TurnedDown;
        booking.HoldExpiresUtc = null;
        booking.DecidedUtc = now;
        booking.DecidedByAppUserId = actorId;
        booking.DecisionNote = Trimmed(note);
        Release(booking, now);
        Touch(booking, now, actorId);
    }

    /// <summary>A confirmed booking let go, by either side. The places go back.</summary>
    public static void Cancel(
        HostedEventBooking booking, Guid? actorId, string? note, DateTime now)
    {
        booking.Status = HostedEventBookingStatus.Cancelled;
        booking.HoldExpiresUtc = null;
        booking.DecidedUtc = now;
        booking.DecidedByAppUserId = actorId;
        booking.DecisionNote = Trimmed(note) ?? booking.DecisionNote;
        Release(booking, now);
        Touch(booking, now, actorId);
    }

    /// <summary>
    /// Nobody answered in time. The places go back and the guest is told what happened.
    /// </summary>
    /// <remarks>
    /// <b>The deadline is kept.</b> Once the status is Expired, <c>HoldExpiresUtc</c> is the record
    /// of WHEN it lapsed, which is the first thing the guest asks and the first thing the venue
    /// needs in order to answer them.
    /// </remarks>
    public static void Expire(HostedEventBooking booking, DateTime now)
    {
        booking.Status = HostedEventBookingStatus.Expired;
        booking.DecidedUtc = now;
        booking.DecisionNote = "The hold ran out before the venue answered.";
        Release(booking, now);
        Touch(booking, now);
    }

    // ── the nights ───────────────────────────────────────────────────────────

    /// <summary>
    /// Stamps every night of this booking as no longer held. Idempotent.
    /// </summary>
    /// <remarks>
    /// <para><b>Stamped, never deleted.</b> The row is what lets a venue see that this party once
    /// had this room, which is the whole of the conversation when somebody rings up about a weekend
    /// they thought they had.</para>
    ///
    /// <para>Only nulls are written, so releasing twice does not move the date somebody's room
    /// actually went back.</para>
    /// </remarks>
    public static void Release(HostedEventBooking booking, DateTime now)
    {
        foreach (var night in booking.Nights)
        {
            night.ReleasedUtc ??= now;
            night.IsHolding = false;
        }
    }

    // ── the umbrella row, which every other screen on the site reads ─────────

    /// <summary>
    /// Makes the umbrella attendee row say exactly what the booking says, or removes it.
    /// </summary>
    /// <remarks>
    /// <para>The umbrella is the ordinary calendar event behind a hosted one, and it is what the
    /// public list, the reminder mail, the share card and the phone all read. So it is not a copy
    /// that may drift: it is derived, here, from the booking's status, every time the status
    /// changes.</para>
    ///
    /// <list type="bullet">
    ///   <item><description>Confirmed → Accepted and Reserved, seats = the party.</description></item>
    ///   <item><description>Requested or Held → Invited and Requested, seats = the party. Somebody
    ///   waiting on an answer is on the list as waiting, not as coming.</description></item>
    ///   <item><description>Anything else → no row at all. A turned-down party is not an
    ///   attendee, and leaving the row would have them counted and reminded.</description></item>
    /// </list>
    /// </remarks>
    public static async Task ApplyUmbrellaAsync(
        BenDataContext db, HostedEventCalendarSync sync, HostedEvent hosted,
        HostedEventBooking booking, Guid actorId, DateTime now, CancellationToken ct)
    {
        var attendee = booking.UmbrellaAttendeeId is { } id
            ? await db.OrgCalendarEventAttendees.FirstOrDefaultAsync(a => a.Id == id, ct)
            : null;

        if (booking.Status is not (HostedEventBookingStatus.Confirmed
                                or HostedEventBookingStatus.Requested
                                or HostedEventBookingStatus.Held))
        {
            if (attendee is not null) db.OrgCalendarEventAttendees.Remove(attendee);
            booking.UmbrellaAttendeeId = null;
            return;
        }

        var umbrella = await sync.SyncAsync(db, hosted, actorId, ct);

        attendee ??= await db.OrgCalendarEventAttendees.FirstOrDefaultAsync(
            a => a.OrgCalendarEventId == umbrella.Id && a.AppUserId == booking.LeadAppUserId, ct);

        if (attendee is null)
        {
            attendee = new OrgCalendarEventAttendee
            {
                Id = Guid.NewGuid(),
                OrgCalendarEventId = umbrella.Id,
                AppUserId = booking.LeadAppUserId,
                DateCreated = now,
                CreatedByAppUserId = actorId,
            };
            db.OrgCalendarEventAttendees.Add(attendee);
        }

        var confirmed = booking.Status == HostedEventBookingStatus.Confirmed;

        attendee.RsvpStatus = confirmed ? RsvpStatus.Accepted : RsvpStatus.Invited;
        attendee.SeatStatus = confirmed ? TourSeatStatus.Reserved : TourSeatStatus.Requested;
        attendee.Seats = EventCapacity.ClampPartySize(booking.PartySize);
        attendee.DateRsvp = now;
        attendee.SeatDecidedUtc = confirmed ? now : null;
        attendee.SeatDecidedByAppUserId = confirmed ? actorId : null;

        booking.UmbrellaAttendeeId = attendee.Id;
    }

    // ── small things ─────────────────────────────────────────────────────────

    /// <summary>
    /// Stamps the booking, and makes every night row agree about whether it is holding anything.
    /// </summary>
    /// <remarks>
    /// The night rows carry the parent's status as a flag because the database's arbiter index
    /// cannot join to find it. Writing the two together, here and nowhere else, is what stops them
    /// disagreeing — and a disagreement would be either a room nobody can book or two parties in
    /// one bed.
    /// </remarks>
    private static void Touch(HostedEventBooking booking, DateTime now, Guid? actorId = null)
    {
        var holding = Holds(booking.Status);
        foreach (var night in booking.Nights)
            night.IsHolding = holding && night.ReleasedUtc is null;

        booking.DateUpdated = now;
        if (actorId is { } id) booking.UpdatedByAppUserId = id;
    }

    private static string? Trimmed(string? value)
        => value?.Trim() is { Length: > 0 } v ? v : null;
}
