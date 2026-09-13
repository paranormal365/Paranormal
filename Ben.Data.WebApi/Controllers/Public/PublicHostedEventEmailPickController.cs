using Ben.Data.Common.Enums;
using Ben.Data.Source.Context;
using Ben.Data.Source.Entities;
using Ben.Data.WebApi.Services;
using Ben.Data.WebApi.Services.Events;
using Ben.Service.Models.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;

namespace Ben.Data.WebApi.Controllers.Public;

/// <summary>
/// Picking places on a hosted event without signing in, proved by an emailed link
/// (item 235 slice 11d).
/// </summary>
/// <remarks>
/// <para><b>Three steps, and only the last one is a booking.</b> The pick puts the squares out of
/// reach for fifteen minutes (<see cref="EmailPicks.PendingFor"/>) and sends a link. Pressing the
/// button behind the link proves the address, makes a passwordless account if there is none, and
/// turns the pick into an ordinary hold with the event's own deadline — through the same writer, the
/// same arbiter and the same letters as a signed-in guest's hold.</para>
///
/// <para><b>A button, not the click itself.</b> Mail scanners open links to check them. A page that
/// acted on being opened would hold seats for whoever's scanner got there, which is the opposite of
/// proving somebody is there.</para>
///
/// <para><b>The link goes on working</b> after it has been used, for a month: it is how somebody with
/// no password sees where their places stand and lets them go. Once the venue has confirmed, letting
/// go is a conversation with the venue and needs signing in.</para>
/// </remarks>
[AllowAnonymous]
[Route("api/public/hosted-events")]
public sealed class PublicHostedEventEmailPickController : BenControllerBase
{
    private readonly IDbContextFactory<BenDataContext> _db;
    private readonly HostedEventCalendarSync _sync;

    public PublicHostedEventEmailPickController(
        IDbContextFactory<BenDataContext> db, HostedEventCalendarSync sync)
    { _db = db; _sync = sync; }

    /// <summary>Picks places without signing in; they wait fifteen minutes for the emailed link.</summary>
    [HttpPost("{eventId:guid}/email-picks")]
    [EnableRateLimiting(RateLimiting.HostedEmailPickPolicy)]
    public async Task<ActionResult<HostedEventEmailPickPlacedRecord>> Pick(
        Guid eventId, [FromBody] PickHostedEventPlacesByEmailRequest request,
        [FromServices] EventGuestMailer mail, CancellationToken ct)
    {
        if (GetCurrentUserIdOrNull() is not null)
            return Conflict("You're signed in, so you can hold these straight from the plan.");

        var email = request.Email?.Trim().ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(email) || !email.Contains('@') || email.Length > 320)
            return BadRequest("Add your email address, so we can send you the link that holds your places.");

        if (BookingContact.WhyNotEnough(request.FirstName, request.LastName, request.Phone) is { } missing)
            return BadRequest(missing);

        await using var db = await _db.CreateDbContextAsync(ct);

        var ev = await PublicHostedEventBookingController.BookableEventAsync(db, eventId, ct);
        if (ev is null) return NotFound();

        if (ev.BookingMode != HostedEventBookingMode.Pick)
            return Conflict("Places at this event are asked for and the venue puts you somewhere, "
                          + "rather than picked on the plan.");
        if (!EventCapacity.IsOpenForRequests(ev, DateTime.UtcNow))
            return Conflict("This event has stopped taking bookings.");

        var chosen = Distinct(request.Nights ?? []);
        if (chosen.Count == 0)
            return BadRequest("Pick at least one place before holding anything.");
        if (await PublicHostedEventBookingController.WhyTheseNightsAreNotRealAsync(db, eventId, chosen, ct) is { } bad)
            return BadRequest(bad);
        if (await PublicHostedEventBookingController.WhatIsNotOnOfferAsync(db, eventId, chosen, ct) is { } withheld)
            return Conflict(withheld);

        var now = DateTime.UtcNow;
        await EmailPicks.RetireLapsedAsync(db, now, eventId, ct);

        // PICKING AGAIN REPLACES THE FIRST. Somebody whose letter went to spam, or who changed their
        // mind about row C, is one person — and the database allows them one live pick here.
        var earlier = await db.HostedEventEmailPicks
            .Include(p => p.Places)
            .FirstOrDefaultAsync(p => p.HostedEventId == eventId && p.Email == email && p.IsLive, ct);
        if (earlier is not null)
        {
            EmailPicks.Retire(earlier, now);
            // Saved on its own: the new pick may want the same squares, and the arbiter must see these
            // released before it sees those taken.
            await db.SaveChangesAsync(ct);
        }

        var elsewhere = await db.HostedEventEmailPicks
            .CountAsync(p => p.Email == email && p.IsLive && p.ExpiresUtc > now, ct);
        if (elsewhere >= EmailPicks.MaxLivePerAddress)
            return Conflict($"This email address has places waiting to be confirmed at {EmailPicks.MaxLivePerAddress} "
                          + "events already. Use those links first, or wait a few minutes.");

        if (await EmailPicks.WhyTooMuchIsPendingAsync(db, eventId, chosen, ct) is { } swamped)
            return Conflict(swamped);

        // Squares a booking holds, or another pick is waiting on. The arbiter would refuse the second
        // pick anyway; asking first names the square, and a booking is invisible to that arbiter.
        var occupancy = await PlanOccupancy.ReadAsync(db, eventId, ct);
        if (chosen.Any(c => c.HostedEventLayoutUnitId is { } unit
                         && occupancy.TryGetValue((c.HostedEventNightId, unit), out var cell)
                         && cell.HeldAs is not null))
            return Conflict(await PublicHostedEventBookingController.WhoGotThereFirstAsync(_db, eventId, chosen, ct));

        var token = EmailPicks.NewToken();
        var pick = new HostedEventEmailPick
        {
            Id = Guid.NewGuid(),
            HostedEventId = eventId,
            FirstName = request.FirstName.Trim(),
            LastName = request.LastName.Trim(),
            Email = email,
            Phone = request.Phone.Trim(),
            PartySize = PublicHostedEventBookingController.PartyPicking(ev, chosen, request.PartySize),
            Note = PublicHostedEventBookingController.Trimmed(request.Note),
            TokenHash = EmailPicks.Hash(token),
            ExpiresUtc = now + EmailPicks.PendingFor,
            DateCreated = now,
            CreatedByAppUserId = Guid.Empty,
        };
        foreach (var choice in chosen)
        {
            pick.Places.Add(new HostedEventEmailPickPlace
            {
                Id = Guid.NewGuid(),
                HostedEventEmailPickId = pick.Id,
                HostedEventNightId = choice.HostedEventNightId,
                HostedEventLayoutUnitId = choice.HostedEventLayoutUnitId,
                People = choice.People,
            });
        }
        db.HostedEventEmailPicks.Add(pick);

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (PublicHostedEventBookingController.IsSomebodyGotThereFirst(ex))
        {
            return Conflict(await PublicHostedEventBookingController.WhoGotThereFirstAsync(_db, eventId, chosen, ct));
        }

        await db.Entry(ev).Reference(e => e.Organization).LoadAsync(ct);
        var names = await PlaceNamesAsync(db, pick.Id, ct);

        // After the save, and best effort: the places are pending whether or not the letter went, and
        // a letter about places that then failed to save would be a link to nothing.
        try
        {
            await mail.SendEmailPickLinkAsync(pick, ev, names, token, ct);
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            HttpContext.RequestServices.GetRequiredService<ILogger<PublicHostedEventEmailPickController>>()
                .LogWarning(e, "Could not send the pick link for {PickId}.", pick.Id);
        }

        return Ok(new HostedEventEmailPickPlacedRecord(pick.ExpiresUtc, names));
    }

    /// <summary>Where a pick made by email stands, for the page its link opens. Changes nothing.</summary>
    [HttpGet("email-picks/{token}")]
    public async Task<ActionResult<HostedEventEmailPickRecord>> Get(string token, CancellationToken ct)
    {
        await using var db = await _db.CreateDbContextAsync(ct);
        var pick = await LoadAsync(db, token, ct);
        if (pick is null) return NotFound();

        return Ok(await DescribeAsync(db, pick, ct));
    }

    /// <summary>
    /// Proves the address and turns the pick into a hold: an account if there is none, then the same
    /// hold a signed-in guest gets.
    /// </summary>
    [HttpPost("email-picks/{token}/confirm")]
    public async Task<ActionResult<HostedEventEmailPickRecord>> Confirm(
        string token, [FromServices] EmailLinkAccounts accounts, [FromServices] EventGuestMailer mail,
        CancellationToken ct)
    {
        await using var db = await _db.CreateDbContextAsync(ct);
        var pick = await LoadAsync(db, token, ct);
        if (pick is null) return NotFound();

        // Pressing it twice is the same statement.
        if (pick.HostedEventBookingId is not null) return Ok(await DescribeAsync(db, pick, ct));
        if (pick.RefusedSentence is { } refused) return Conflict(refused);

        var now = DateTime.UtcNow;
        if (!pick.IsLive || pick.ExpiresUtc <= now)
            return Conflict("The fifteen minutes ran out and those places went back. "
                          + "Pick again — they may well still be free.");

        var ev = pick.HostedEvent;
        if (!HostedEventStates.TakingBookings.Contains(ev.LifecycleState)
            || ev.BookingMode != HostedEventBookingMode.Pick
            || !EventCapacity.IsOpenForRequests(ev, now))
            return await RefuseAsync(db, pick, "This event has stopped taking bookings, so those places went back.", now, ct);

        var chosen = pick.Places
            .Select(p => new HostedEventBookingNightChoice(p.HostedEventNightId, p.HostedEventLayoutUnitId, p.People))
            .ToList();

        if (await PublicHostedEventBookingController.WhatIsNotOnOfferAsync(db, ev.Id, chosen, ct) is { } withheld)
            return await RefuseAsync(db, pick, withheld, now, ct);

        var user = await accounts.FindOrCreateAsync(pick.Email, null, pick.FirstName, pick.LastName, ct);
        if (user is null) return BadRequest("That account could not be created.");

        if (await db.HostedEventBookings.AnyAsync(
                b => b.HostedEventId == ev.Id && b.LeadAppUserId == user.Id
                  && (b.Status == HostedEventBookingStatus.Requested
                   || b.Status == HostedEventBookingStatus.Held
                   || b.Status == HostedEventBookingStatus.Confirmed), ct))
            return await RefuseAsync(db, pick,
                "You already have a booking at this event, so these places went back. "
                + "Sign in to see yours.", now, ct);

        if (await PublicHostedEventBookingController.CountMyLiveHoldsAsync(db, user.Id, ct)
            >= PublicHostedEventBookingController.MaximumLiveHolds)
            return await RefuseAsync(db, pick,
                $"You are holding places at {PublicHostedEventBookingController.MaximumLiveHolds} events already, "
                + "so these went back. Wait for a venue to answer, or let one go, before choosing more.", now, ct);

        var booking = new HostedEventBooking
        {
            Id = Guid.NewGuid(),
            HostedEventId = ev.Id,
            LeadAppUserId = user.Id,
            PartySize = pick.PartySize,
            Kind = HostedEventBookingKind.Overnight,
            Status = HostedEventBookingStatus.Requested,
            Note = pick.Note,
            ContactPhone = pick.Phone,
            DateCreated = now,
            CreatedByAppUserId = user.Id,
        };
        db.HostedEventBookings.Add(booking);
        PublicHostedEventBookingController.WriteNights(booking, chosen);

        BookingTransitions.Hold(booking, ev, now);
        await BookingTransitions.ApplyUmbrellaAsync(db, _sync, ev, booking, user.Id, now, ct);
        await BookingContact.FillEmptyNamesAsync(
            db, user.Id, new BookingContact.Details(pick.FirstName, pick.LastName, pick.Phone), ct);

        EmailPicks.Retire(pick, now);
        pick.ConfirmedUtc = now;
        pick.HostedEventBookingId = booking.Id;

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (PublicHostedEventBookingController.IsSomebodyGotThereFirst(ex))
        {
            // A signed-in guest or the venue took a square between the pick and the click; the pick's
            // own arbiter could not see them. Say which, and give the rest back.
            var lost = await PublicHostedEventBookingController.WhoGotThereFirstAsync(
                _db, ev.Id, chosen, ct, countEmailPicks: false);

            await using var fresh = await _db.CreateDbContextAsync(ct);
            var again = await fresh.HostedEventEmailPicks.Include(p => p.Places).FirstAsync(p => p.Id == pick.Id, ct);
            return await RefuseAsync(fresh, again, lost.Sentence + " The rest of your places went back too.", now, ct);
        }

        await mail.SendHoldPlacedAsync(db, booking.Id, ct);

        return Ok(await DescribeAsync(db, pick, ct));
    }

    /// <summary>
    /// Lets the places go: a pick still waiting, or the hold it became while the venue has not yet
    /// answered.
    /// </summary>
    [HttpDelete("email-picks/{token}")]
    public async Task<ActionResult<HostedEventEmailPickRecord>> LetGo(string token, CancellationToken ct)
    {
        await using var db = await _db.CreateDbContextAsync(ct);
        var pick = await LoadAsync(db, token, ct);
        if (pick is null) return NotFound();

        var now = DateTime.UtcNow;

        if (pick.HostedEventBookingId is { } bookingId)
        {
            var booking = await db.HostedEventBookings
                .Include(b => b.Nights)
                .FirstAsync(b => b.Id == bookingId, ct);

            if (booking.Status == HostedEventBookingStatus.Confirmed)
                return Conflict("The venue has already confirmed this booking. Sign in to ask them to release it.");

            if (booking.Status == HostedEventBookingStatus.Held)
            {
                BookingTransitions.Cancel(booking, booking.LeadAppUserId, "Let go by the guest.", now);
                await BookingTransitions.ApplyUmbrellaAsync(
                    db, _sync, pick.HostedEvent, booking, booking.LeadAppUserId, now, ct);
                await db.SaveChangesAsync(ct);
            }
        }
        else if (pick.IsLive)
        {
            EmailPicks.Retire(pick, now);
            await db.SaveChangesAsync(ct);
        }

        return Ok(await DescribeAsync(db, pick, ct));
    }

    // ── the work ─────────────────────────────────────────────────────────────

    private static Task<HostedEventEmailPick?> LoadAsync(BenDataContext db, string token, CancellationToken ct)
    {
        var hash = EmailPicks.Hash(token ?? string.Empty);
        return db.HostedEventEmailPicks
            .Include(p => p.Places)
            .Include(p => p.HostedEvent).ThenInclude(e => e.Organization)
            .FirstOrDefaultAsync(p => p.TokenHash == hash, ct);
    }

    /// <summary>Retires the pick with the reason it could not become a hold, and answers with it.</summary>
    private static async Task<ActionResult<HostedEventEmailPickRecord>> RefuseAsync(
        BenDataContext db, HostedEventEmailPick pick, string sentence, DateTime now, CancellationToken ct)
    {
        EmailPicks.Retire(pick, now);
        pick.RefusedSentence = sentence.Length > 500 ? sentence[..500] : sentence;
        await db.SaveChangesAsync(ct);
        return new ConflictObjectResult(sentence);
    }

    private static async Task<HostedEventEmailPickRecord> DescribeAsync(
        BenDataContext db, HostedEventEmailPick pick, CancellationToken ct)
    {
        var now = DateTime.UtcNow;
        var ev = pick.HostedEvent;
        var places = await PlaceNamesAsync(db, pick.Id, ct);

        MyHostedEventBookingRecord? booking = null;
        var noPassword = false;
        if (pick.HostedEventBookingId is { } bookingId
            && await db.HostedEventBookings.AsNoTracking()
                   .Where(b => b.Id == bookingId).Select(b => b.LeadAppUserId).FirstOrDefaultAsync(ct) is var lead
            && lead != Guid.Empty)
        {
            var row = await PublicHostedEventBookingController.MineQuery(db, lead, includeReleased: true)
                .FirstOrDefaultAsync(b => b.Id == bookingId, ct);
            if (row is not null) booking = PublicHostedEventBookingController.ToMine(row);

            var user = await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == lead, ct);
            noPassword = user is not null && user.PasswordHash is null;
        }

        var state = booking is not null ? "held"
            : pick.RefusedSentence is not null ? "refused"
            : pick.IsLive && pick.ExpiresUtc > now ? "waiting"
            : pick.ExpiresUtc <= now ? "lapsed"
            : "let-go";

        return new HostedEventEmailPickRecord(
            state,
            ev.Id,
            ev.Name,
            ev.UrlName,
            ev.Organization?.Name,
            ev.Organization?.UrlName,
            pick.Email,
            pick.PartySize,
            pick.ExpiresUtc,
            places,
            pick.RefusedSentence,
            booking,
            noPassword);
    }

    /// <summary>"Fri 10/16 — C4", in the plan's order.</summary>
    private static async Task<List<string>> PlaceNamesAsync(BenDataContext db, Guid pickId, CancellationToken ct)
    {
        var rows = await db.HostedEventEmailPickPlaces.AsNoTracking()
            .Include(p => p.HostedEventNight)
            .Include(p => p.HostedEventLayoutUnit).ThenInclude(u => u!.PlaceRoom)
            .Where(p => p.HostedEventEmailPickId == pickId)
            .ToListAsync(ct);

        return [.. rows
            .OrderBy(p => p.HostedEventNight.Date)
            .ThenBy(p => p.HostedEventLayoutUnit?.SortOrder)
            .Select(p => $"{p.HostedEventNight.Date:ddd MM/dd} — "
                       + (p.HostedEventLayoutUnit is { } unit ? EventCapacity.NameOf(unit) : "Just for the day"))];
    }

    /// <summary>One entry per square per night, whatever the page sent.</summary>
    private static List<HostedEventBookingNightChoice> Distinct(IReadOnlyList<HostedEventBookingNightChoice> nights)
        => [.. nights.GroupBy(n => (n.HostedEventNightId, n.HostedEventLayoutUnitId)).Select(g => g.Last())];
}
