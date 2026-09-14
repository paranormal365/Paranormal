using Ben.Data.Common.Enums;
using Ben.Data.Source.Context;
using Ben.Data.Source.Entities;
using Microsoft.EntityFrameworkCore;

namespace Ben.Data.WebApi.Services.Events;

/// <summary>
/// Who may sign up for a session, whether they get a place or a place in the queue, and who moves up
/// when somebody leaves (item 235 phase 10).
/// </summary>
/// <remarks>
/// <para><b>First come, and a queue after that.</b> A full class is a class somebody may drop out
/// of; the guest who asked first gets that place without having to keep checking, and is told.</para>
///
/// <para><b>The counter is the arbiter.</b> <c>HostedEventSession.PlacesTaken</c> is a concurrency
/// token: every write that takes or gives back a place changes it, so two sign-ups racing for the
/// last place cannot both save. The loser's attempt is retried on a fresh read, which sees the class
/// full and queues them.</para>
///
/// <para><b>Who may sign up</b> is a guest with a confirmed place at the event — for as many of their
/// party as they like, up to its size — or somebody helping at it. A request still waiting on the
/// venue is not a place at the event, so it is not a place in its classes either.</para>
/// </remarks>
public static class SessionSignUps
{
    private const int Attempts = 5;

    /// <summary>What happened to a sign-up.</summary>
    /// <param name="Position">Where they are in the queue, when they are waiting. 1 is next.</param>
    public sealed record Outcome(Guid? SignUpId, bool Waiting, int? Position, string? Refusal);

    /// <summary>Why this session cannot be saved as described, or null when it can.</summary>
    public static string? WhyNotValid(
        HostedEvent hosted, IReadOnlyList<DateTime> nightDates, string? title,
        DateTime startsUtc, DateTime endsUtc, int? capacity, int placesTaken)
    {
        if (string.IsNullOrWhiteSpace(title)) return "Give it a name — \"Operating the Ovilus\", \"Dinner\".";
        if (endsUtc <= startsUtc) return "It has to end after it starts.";
        if (endsUtc - startsUtc > TimeSpan.FromHours(24)) return "A session can't run longer than a day. Split it into two.";
        if (capacity is < 1 or > 10000) return "How many it holds has to be at least one, or left empty for no limit.";
        if (capacity is int cap && cap < placesTaken)
            return $"{placesTaken} people already have places, so it can't hold fewer than {placesTaken}.";

        if (nightDates.Count == 0) return "Give the event its dates before planning its programme.";

        // On the venue's clock, and a session may start in the small hours after the last night —
        // a midnight séance on the Saturday is still the Saturday.
        var zone = HostedEventCalendarSync.ZoneOf(hosted.TimeZoneId);
        var localStart = TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(startsUtc, DateTimeKind.Utc), zone).Date;
        var first = nightDates.Min().Date;
        var last = nightDates.Max().Date.AddDays(1);
        if (localStart < first || localStart > last)
            return $"That's outside the event. It runs {first:MM/dd/yyyy} to {nightDates.Max():MM/dd/yyyy}.";

        return null;
    }

    /// <summary>Turns a date and two times at the venue into UTC. An end before the start is the next day.</summary>
    public static (DateTime StartsUtc, DateTime EndsUtc) ToUtc(HostedEvent hosted, DateTime date, TimeSpan starts, TimeSpan ends)
    {
        var zone = HostedEventCalendarSync.ZoneOf(hosted.TimeZoneId);
        var localStart = DateTime.SpecifyKind(date.Date + starts, DateTimeKind.Unspecified);
        var localEnd = DateTime.SpecifyKind(date.Date + ends, DateTimeKind.Unspecified);
        if (localEnd <= localStart) localEnd = localEnd.AddDays(1);
        return (TimeZoneInfo.ConvertTimeToUtc(localStart, zone), TimeZoneInfo.ConvertTimeToUtc(localEnd, zone));
    }

    /// <summary>
    /// Whether this person may sign up at this event, for how many, and on which booking.
    /// </summary>
    public static async Task<(Guid? BookingId, int MaxPeople, string? Refusal)> EntitlementAsync(
        BenDataContext db, Guid hostedEventId, Guid userId, CancellationToken ct)
    {
        var booking = await db.HostedEventBookings.AsNoTracking()
            .Where(b => b.HostedEventId == hostedEventId && b.LeadAppUserId == userId
                     && b.Status == HostedEventBookingStatus.Confirmed)
            .Select(b => new { b.Id, b.PartySize })
            .FirstOrDefaultAsync(ct);
        if (booking is not null) return (booking.Id, Math.Max(1, booking.PartySize), null);

        var helping = await db.HostedEventStaff.AsNoTracking()
            .AnyAsync(s => s.HostedEventId == hostedEventId && s.AppUserId == userId && s.DateConfirmed != null, ct);
        if (helping) return (null, 1, null);

        var waiting = await db.HostedEventBookings.AsNoTracking()
            .AnyAsync(b => b.HostedEventId == hostedEventId && b.LeadAppUserId == userId
                        && (b.Status == HostedEventBookingStatus.Requested || b.Status == HostedEventBookingStatus.Held), ct);

        return (null, 0, waiting
            ? "Signing up opens once the venue confirms your place at the event."
            : "Signing up is for guests with a confirmed place at the event.");
    }

    /// <summary>Signs somebody up: a place when there is room, the queue when there is not.</summary>
    public static async Task<Outcome> SignUpAsync(
        IDbContextFactory<BenDataContext> factory, Guid sessionId, Guid userId, Guid? bookingId, int people,
        DateTime now, CancellationToken ct)
    {
        for (var attempt = 1; ; attempt++)
        {
            await using var db = await factory.CreateDbContextAsync(ct);
            var session = await db.HostedEventSessions
                .Include(s => s.SignUps)
                .FirstOrDefaultAsync(s => s.Id == sessionId, ct);
            if (session is null) return new(null, false, null, "That session isn't on the programme.");

            if (session.SignUps.FirstOrDefault(s => s.AppUserId == userId) is { } already)
                return new(already.Id, already.WaitlistedUtc is not null, PositionOf(session, already), null);

            if (session.CalledOffUtc is not null) return new(null, false, null, "That session has been cancelled.");
            if (!session.RequiresSignUp) return new(null, false, null, "There's no need to sign up for that one — just come.");
            if (session.StartsAtUtc <= now) return new(null, false, null, "That session has already started.");

            var fits = session.Capacity is not int cap || session.PlacesTaken + people <= cap;
            var signUp = new HostedEventSessionSignUp
            {
                Id = Guid.NewGuid(), HostedEventSessionId = session.Id, AppUserId = userId,
                HostedEventBookingId = bookingId, People = people,
                WaitlistedUtc = fits ? null : now,
                DateCreated = now, CreatedByAppUserId = userId,
            };
            db.HostedEventSessionSignUps.Add(signUp);
            if (fits)
            {
                session.PlacesTaken += people;
            }
            else
            {
                // Touch the counter anyway, so a place given back in the same instant cannot be handed
                // to somebody behind this guest in the queue.
                session.PlacesTaken = session.PlacesTaken;
                db.Entry(session).Property(s => s.PlacesTaken).IsModified = true;
            }

            try
            {
                await db.SaveChangesAsync(ct);
                session.SignUps.Add(signUp);
                return new(signUp.Id, !fits, fits ? null : PositionOf(session, signUp), null);
            }
            catch (DbUpdateConcurrencyException) when (attempt < Attempts)
            {
                // Somebody else took or gave back a place. Read it again.
            }
            catch (DbUpdateException) when (attempt < Attempts)
            {
                // The unique index: a second press from the same person. The next read finds it.
            }
        }
    }

    /// <summary>
    /// Takes somebody out of a session, and moves the queue up into whatever that frees.
    /// </summary>
    /// <returns>The sign-ups that were promoted, so they can be told.</returns>
    public static async Task<(bool Left, IReadOnlyList<Guid> Promoted)> LeaveAsync(
        IDbContextFactory<BenDataContext> factory, Guid sessionId, Guid signUpId, Guid actorId, DateTime now, CancellationToken ct)
    {
        for (var attempt = 1; ; attempt++)
        {
            await using var db = await factory.CreateDbContextAsync(ct);
            var session = await db.HostedEventSessions.Include(s => s.SignUps)
                .FirstOrDefaultAsync(s => s.Id == sessionId, ct);
            var signUp = session?.SignUps.FirstOrDefault(s => s.Id == signUpId);
            if (session is null || signUp is null) return (false, []);

            if (signUp.WaitlistedUtc is null) session.PlacesTaken -= signUp.People;
            db.HostedEventSessionSignUps.Remove(signUp);
            session.SignUps.Remove(signUp);

            var promoted = Promote(session, actorId, now);
            db.Entry(session).Property(s => s.PlacesTaken).IsModified = true;

            try
            {
                await db.SaveChangesAsync(ct);
                return (true, promoted);
            }
            catch (DbUpdateConcurrencyException) when (attempt < Attempts)
            {
            }
        }
    }

    /// <summary>
    /// Moves waiting guests into free places, in the order they asked. The caller saves.
    /// </summary>
    /// <remarks>
    /// <b>In order, and without skipping.</b> A party of four at the head of the queue is not jumped by
    /// a single person behind them when two places come free; the four wait for four. Skipping them
    /// would be fair to nobody and would read as the site losing their place.
    /// </remarks>
    public static IReadOnlyList<Guid> Promote(HostedEventSession session, Guid actorId, DateTime now)
    {
        var promoted = new List<Guid>();
        if (session.CalledOffUtc is not null) return promoted;

        foreach (var waiting in session.SignUps.Where(s => s.WaitlistedUtc is not null).OrderBy(s => s.WaitlistedUtc).ThenBy(s => s.DateCreated))
        {
            if (session.Capacity is int cap && session.PlacesTaken + waiting.People > cap) break;

            waiting.WaitlistedUtc = null;
            waiting.PromotedUtc = now;
            waiting.DateUpdated = now;
            waiting.UpdatedByAppUserId = actorId;
            session.PlacesTaken += waiting.People;
            promoted.Add(waiting.Id);
        }

        return promoted;
    }

    /// <summary>Where a waiting sign-up is in the queue. 1 is next. Null when they have a place.</summary>
    public static int? PositionOf(HostedEventSession session, HostedEventSessionSignUp signUp)
        => signUp.WaitlistedUtc is null
            ? null
            : session.SignUps.Where(s => s.WaitlistedUtc is not null)
                .OrderBy(s => s.WaitlistedUtc).ThenBy(s => s.DateCreated)
                .ToList().FindIndex(s => s.Id == signUp.Id) + 1;
}
