using Ben.Data.Common.Enums;
using Ben.Data.Source.Entities;

namespace Ben.Data.WebApi.Services.Tours;

/// <summary>
/// How many places a date has taken, and who may take one (item 234, Ben 2026-09-10).
/// </summary>
/// <remarks>
/// <para><b>Capacity counts places, not rows.</b> Ben: <i>"the seat or seats have been reserved for
/// the tour."</i> Before this, one sign-up was one person in four separate places, and each of them
/// counted rows. They now all ask here instead, because four copies of a counting rule is four
/// chances for them to disagree about whether a walk is full.</para>
///
/// <para>Every attendee row that already exists holds exactly one place, so the sum equals the old
/// count and nothing about an ordinary event moves.</para>
///
/// <para><b>A request holds nothing.</b> On a tour date a sign-up waits at
/// <see cref="TourSeatStatus.Requested"/> with <c>RsvpStatus.Invited</c>, and only approval makes
/// it <c>Accepted</c>. So the same "accepted" that every other part of this codebase already counts
/// goes on meaning <i>has a place</i>, and a queue of hopefuls cannot fill a walk.</para>
/// </remarks>
public static class TourSeats
{
    /// <summary>The most places one sign-up may ask for.</summary>
    /// <remarks>
    /// A guest booking for their family, not a coach party. A business that wants to seat forty
    /// people from one request is arranging that with them directly, which is the whole premise of
    /// approval — and a box with no ceiling is a box somebody types 100000 into.
    /// </remarks>
    public const int MaxSeatsPerRequest = 20;

    /// <summary>Whether this date belongs to a tour, which is what turns the seat rules on.</summary>
    public static bool IsTourDate(OrgCalendarEvent ev) => ev.TourId is not null;

    /// <summary>
    /// A number of places somebody may actually ask for.
    /// </summary>
    /// <remarks>
    /// Clamped rather than refused: "1" is what somebody who sent nothing meant, and a request for
    /// zero places is a mistake with an obvious reading rather than something to argue about.
    /// </remarks>
    public static int Clamp(int? seats) => Math.Clamp(seats ?? 1, 1, MaxSeatsPerRequest);

    /// <summary>
    /// The places already held on a date, optionally ignoring one person's own sign-up.
    /// </summary>
    /// <param name="attendees">Every attendee row for the date.</param>
    /// <param name="excludingAppUserId">
    /// Somebody re-signing-up after cancelling should not be refused by the place they are not
    /// occupying.
    /// </param>
    public static int PlacesTaken(
        IEnumerable<OrgCalendarEventAttendee> attendees, Guid? excludingAppUserId = null)
        => attendees
            .Where(a => a.RsvpStatus == RsvpStatus.Accepted)
            .Where(a => excludingAppUserId is not { } id || a.AppUserId != id)
            .Sum(a => Math.Max(1, a.Seats));

    /// <summary>Places still free, or null when the date has no stated capacity.</summary>
    public static int? PlacesLeft(int? capacity, int taken)
        => capacity is int cap ? Math.Max(0, cap - taken) : null;

    /// <summary>
    /// Why this many places cannot be approved, or null when they can.
    /// </summary>
    /// <remarks>
    /// <para>Said in places rather than in people, and it names the number left, because a business
    /// refused with "this event is full" cannot tell whether to approve a smaller party or none at
    /// all.</para>
    ///
    /// <para><b>A request is never refused for fullness</b> — only an approval is. A walk that fills
    /// up keeps taking requests, which makes the overflow a waiting list the business can work
    /// through rather than a closed door, and it is the business who decides.</para>
    /// </remarks>
    public static string? WhyTheseSeatsCannotBeApproved(int? capacity, int taken, int wanted)
    {
        if (PlacesLeft(capacity, taken) is not { } left) return null;
        if (wanted <= left) return null;

        return left == 0
            ? "This date is full. Turn a reserved seat down first if you want to make room."
            : $"Only {left} place{(left == 1 ? "" : "s")} left on this date, and this is a request "
              + $"for {wanted}.";
    }
}
