using Ben.Data.Common.Enums;
using Ben.Data.Source.Context;
using Ben.Data.Source.Entities;
using Microsoft.EntityFrameworkCore;

namespace Ben.Data.WebApi.Services.Tours;

/// <summary>
/// The pass a guide scans at the meeting point (item 247).
/// </summary>
/// <remarks>
/// <para>Ben, 2026-09-20: <i>"qr codes for ghost tour tickets for the staff to scan when they
/// arrive for the tour."</i></para>
///
/// <para><b>The same shape a hosted event's pass has, and deliberately not the same table.</b> A
/// hosted event's pass belongs to a BOOKING — a party with rooms, a status that can be turned
/// down, a history of reissues somebody may have to explain — so it earns a table with its own
/// lifecycle. A tour seat is one person, and its row carries exactly one pass for its whole life,
/// so the token lives on the seat.</para>
///
/// <para><b>The rendering is borrowed, not copied.</b> <see cref="Events.EventPasses.Png"/> takes a
/// token and gives back a PNG; it knows nothing about bookings, and two QR renderers that could
/// drift apart is two answers to "why does my code not scan".</para>
/// </remarks>
public static class TourPasses
{
    /// <summary>
    /// Whether this seat entitles somebody to a pass at all.
    /// </summary>
    /// <remarks>
    /// Only a seat they actually hold. A pass against a request is a ticket to a walk nobody has
    /// agreed to give them, and they would turn up holding it — the same rule the hosted event's
    /// door keeps, for the same reason.
    /// </remarks>
    public static bool MayHaveAPass(OrgCalendarEventAttendee seat)
        // Reserved is the tour's "you have a place" — the business approved it and the seats are
        // held. Requested has not been looked at, and TurnedDown was refused. A seat with no tour
        // status at all is an ordinary calendar attendee, whose own acceptance is the answer.
        => seat.SeatStatus is TourSeatStatus.Reserved
        || (seat.SeatStatus is null && seat.RsvpStatus is RsvpStatus.Accepted);

    /// <summary>
    /// Gives this seat a pass, keeping the one it has.
    /// </summary>
    /// <remarks>
    /// <para>Idempotent. A seat confirmed twice, a letter resent, a guide pressing "send it again"
    /// — all of them mean one pass, and two codes for one seat is two codes at a meeting point,
    /// one of which is the wrong one.</para>
    ///
    /// <para>Nothing is saved here: the caller owns the transaction, so a pass cannot exist
    /// against a confirmation that was rolled back.</para>
    /// </remarks>
    public static string Ensure(OrgCalendarEventAttendee seat)
    {
        if (seat.PassToken is { Length: > 0 } already) return already;

        seat.PassToken = Events.EventPasses.NewToken();
        seat.PassIssuedUtc = DateTime.UtcNow;
        return seat.PassToken;
    }

    /// <summary>Replaces the pass, which is also how one is taken back.</summary>
    /// <remarks>
    /// For a guest who lost the letter, and for a seat that changed hands. The old token stops
    /// resolving the moment this is saved, so there is never a second live code for one seat.
    /// </remarks>
    public static string Reissue(OrgCalendarEventAttendee seat)
    {
        seat.PassToken = Events.EventPasses.NewToken();
        seat.PassIssuedUtc = DateTime.UtcNow;
        seat.CheckedInUtc = null;
        seat.CheckedInByAppUserId = null;
        return seat.PassToken;
    }

    /// <summary>The seat a scanned code belongs to, with what a guide needs to read out.</summary>
    public static Task<OrgCalendarEventAttendee?> ByTokenAsync(
        BenDataContext db, string token, CancellationToken ct)
        => db.OrgCalendarEventAttendees
            .Include(a => a.AppUser)
            .Include(a => a.OrgCalendarEvent)
            .FirstOrDefaultAsync(a => a.PassToken == token, ct);

    /// <summary>
    /// Why this scan is refused, in words a guide can say out loud, or null to let them in.
    /// </summary>
    /// <remarks>
    /// <para>Sentences rather than statuses, because the audience is somebody standing in the
    /// dark with a queue behind them. "We don't recognise that code" tells them what to do next;
    /// a 404 does not.</para>
    ///
    /// <para><b>The wrong-walk case is named specifically.</b> A guest holding a valid pass for
    /// next Friday is the most common honest mistake at a meeting point, and telling them "not
    /// recognised" sends them away believing they were never booked.</para>
    /// </remarks>
    public static string? WhyThisScanIsRefused(
        OrgCalendarEventAttendee? seat, Guid expectedEventId, string? otherWalkName)
    {
        if (seat is null)
            return "We don't recognise that code. Ask them to check the email, or look them up by name.";

        if (seat.OrgCalendarEventId != expectedEventId)
        {
            return otherWalkName is { Length: > 0 } name
                ? $"That pass is for {name}, not tonight's walk."
                : "That pass is for a different walk.";
        }

        if (!MayHaveAPass(seat))
            return "That seat is not confirmed, so the pass does not admit them yet.";

        return null;
    }

    /// <summary>
    /// Writes down that a guide let somebody in, and says whether this was the first time.
    /// </summary>
    /// <remarks>
    /// A second scan is not an error and is not a silent success: a guide needs to hear "already
    /// here, at 7.42" so they can tell a queue-jumper from somebody whose friend already scanned
    /// their code.
    /// </remarks>
    public static (bool WasAlreadyIn, DateTime At) CheckIn(OrgCalendarEventAttendee seat, Guid guideId)
    {
        if (seat.CheckedInUtc is { } already) return (true, already);

        var now = DateTime.UtcNow;
        seat.CheckedInUtc = now;
        seat.CheckedInByAppUserId = guideId;
        return (false, now);
    }
}
