using AutoMapper;
using Ben.Data.Common.Enums;
using Ben.Data.Source.Context;
using Ben.Data.Source.Entities;
using Ben.Data.WebApi.Controllers.Cms;
using Ben.Data.WebApi.Services.Events;
using Ben.Service.Models.Entities;
using Ben.Service.RepositoryService.GenericInterfaces;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Ben.Data.WebApi.Controllers.Entities;

/// <summary>
/// Who sits at which table, at each sitting of a hosted event (item 235 phase 13).
/// </summary>
/// <remarks>
/// <para><b>Confirmed parties only</b>, and only the ones there on the sitting's night: seating somebody still waiting
/// for an answer would be promising them dinner before the venue has promised them a place.</para>
///
/// <para><b>Whoever looks after the menus</b> — the event's editors, or a helper handed menus and dietary — because
/// seating is the kitchen's and the dining room's job, not billing's. The table list carries each party's dietary
/// notes, which is the reason that helper exists.</para>
///
/// <para><b>A party that stops being confirmed drops off the room by itself:</b> every read filters on the booking's
/// status, so a cancellation never leaves somebody's name on a table.</para>
/// </remarks>
[Route("api/organizations/{orgId:guid}/events/{eventId:guid}/dining")]
public sealed class HostedEventDiningController : OrgCmsControllerBase
{
    private readonly Services.Access.HostedEventAccess _access;

    public HostedEventDiningController(
        IDbContextFactory<BenDataContext> dbFactory, IMapper mapper, IOrganizationSecurityService security,
        Services.Access.HostedEventAccess access)
        : base(dbFactory, mapper, security) { _access = access; }

    [HttpGet]
    public async Task<ActionResult<HostedEventDiningRecord>> Get(Guid orgId, Guid eventId, [FromQuery] Guid? sitting, CancellationToken ct)
    {
        var userId = GetCurrentUserId();
        if (userId is null) return Unauthorized();

        await using var db = await DbFactory.CreateDbContextAsync(ct);
        if (!await _access.CanEditMenusAsync(userId.Value, orgId, eventId, db, ct)) return Forbid();
        if (!await db.HostedEvents.AnyAsync(e => e.Id == eventId && e.OrganizationId == orgId, ct)) return NotFound();

        return Ok(await DescribeAsync(db, eventId, sitting, null, ct));
    }

    /// <summary>
    /// Replaces the room's tables. A table left out goes, with whoever was seated at it; one sent back with its id
    /// keeps its seating, and may not be made smaller than the people already at it.
    /// </summary>
    [HttpPut("tables")]
    public async Task<ActionResult<HostedEventDiningRecord>> SetTables(
        Guid orgId, Guid eventId, [FromQuery] Guid? sitting, [FromBody] SetHostedEventDiningTablesRequest request, CancellationToken ct)
    {
        var userId = GetCurrentUserId();
        if (userId is null) return Unauthorized();

        await using var db = await DbFactory.CreateDbContextAsync(ct);
        if (!await _access.CanEditMenusAsync(userId.Value, orgId, eventId, db, ct)) return Forbid();
        if (!await db.HostedEvents.AnyAsync(e => e.Id == eventId && e.OrganizationId == orgId, ct)) return NotFound();

        var wanted = request.Tables ?? [];
        if (wanted.Count > 200) return BadRequest("That is more tables than one room holds.");
        if (wanted.FirstOrDefault(t => string.IsNullOrWhiteSpace(t.Name)) is not null) return BadRequest("Every table needs a name — \"Table 1\".");
        if (wanted.FirstOrDefault(t => t.Seats is < 1 or > HostedEventDiningTable.MaxSeats) is { } odd)
            return BadRequest($"{odd.Name.Trim()} has to seat between 1 and {HostedEventDiningTable.MaxSeats}.");
        if (wanted.GroupBy(t => t.Name.Trim(), StringComparer.OrdinalIgnoreCase).FirstOrDefault(g => g.Count() > 1) is { } twice)
            return BadRequest($"Two tables are called {twice.Key}. Give each its own name, so the kitchen can tell them apart.");

        var existing = await db.HostedEventDiningTables.Where(t => t.HostedEventId == eventId).ToListAsync(ct);
        var seats = await ConfirmedSeatsQuery(db, eventId).ToListAsync(ct);

        foreach (var input in wanted.Where(t => t.Id is not null))
        {
            if (existing.FirstOrDefault(t => t.Id == input.Id) is not { } table) continue;
            var busiest = seats.Where(s => s.HostedEventDiningTableId == table.Id)
                .GroupBy(s => s.HostedEventMenuId).Select(g => g.Sum(s => s.People)).DefaultIfEmpty(0).Max();
            if (input.Seats < busiest)
                return BadRequest($"{table.Name} has {busiest} people seated at one sitting. Move somebody before making it seat {input.Seats}.");
        }

        var kept = wanted.Where(t => t.Id is not null).Select(t => t.Id!.Value).ToHashSet();
        var going = existing.Where(t => !kept.Contains(t.Id)).Select(t => t.Id).ToList();
        if (going.Count > 0)
        {
            // Every seat at a table that goes, whatever the booking's status: the link is NoAction.
            await db.HostedEventDiningSeats.Where(s => going.Contains(s.HostedEventDiningTableId)).ExecuteDeleteAsync(ct);
            db.HostedEventDiningTables.RemoveRange(existing.Where(t => going.Contains(t.Id)));
        }

        var now = DateTime.UtcNow;
        for (var i = 0; i < wanted.Count; i++)
        {
            var input = wanted[i];
            var table = input.Id is { } id ? existing.FirstOrDefault(t => t.Id == id) : null;
            if (table is null)
            {
                table = new HostedEventDiningTable
                {
                    Id = Guid.NewGuid(), HostedEventId = eventId, DateCreated = now, CreatedByAppUserId = userId.Value,
                };
                db.HostedEventDiningTables.Add(table);
            }
            else
            {
                table.DateUpdated = now;
                table.UpdatedByAppUserId = userId.Value;
            }

            table.Name = input.Name.Trim().Length > 60 ? input.Name.Trim()[..60] : input.Name.Trim();
            table.Seats = input.Seats;
            table.SortOrder = i;
        }

        await db.SaveChangesAsync(ct);
        return Ok(await DescribeAsync(db, eventId, sitting, "Tables saved.", ct));
    }

    /// <summary>Seats some or all of a confirmed party at a table for one sitting.</summary>
    [HttpPost("sittings/{sittingId:guid}/seats")]
    public async Task<ActionResult<HostedEventDiningRecord>> Seat(
        Guid orgId, Guid eventId, Guid sittingId, [FromBody] SeatHostedEventPartyRequest request, CancellationToken ct)
    {
        var userId = GetCurrentUserId();
        if (userId is null) return Unauthorized();

        await using var db = await DbFactory.CreateDbContextAsync(ct);
        if (!await _access.CanEditMenusAsync(userId.Value, orgId, eventId, db, ct)) return Forbid();

        var menu = await SittingAsync(db, orgId, eventId, sittingId, ct);
        if (menu is null) return NotFound();

        var table = await db.HostedEventDiningTables.FirstOrDefaultAsync(t => t.Id == request.TableId && t.HostedEventId == eventId, ct);
        if (table is null) return BadRequest("That table isn't in this event's dining room.");

        var party = (await PartiesAsync(db, eventId, menu.HostedEventNightId, ct)).FirstOrDefault(b => b.Id == request.BookingId);
        if (party is null) return BadRequest("Only a confirmed party that is here that night can be seated.");

        var sittingSeats = await ConfirmedSeatsQuery(db, eventId).Where(s => s.HostedEventMenuId == sittingId).ToListAsync(ct);
        var name = party.LeadAppUser?.DisplayName ?? "This";
        var (people, refusal) = DiningSeating.HowMany(table, name, EventCapacity.ClampPartySize(party.PartySize), sittingSeats, party.Id, request.People);
        if (refusal is not null) return BadRequest(refusal);

        var now = DateTime.UtcNow;
        var already = await db.HostedEventDiningSeats.FirstOrDefaultAsync(
            s => s.HostedEventMenuId == sittingId && s.HostedEventBookingId == party.Id && s.HostedEventDiningTableId == table.Id, ct);
        if (already is not null)
        {
            already.People += people;
            already.DateUpdated = now;
            already.UpdatedByAppUserId = userId;
        }
        else
        {
            db.HostedEventDiningSeats.Add(new HostedEventDiningSeat
            {
                Id = Guid.NewGuid(), HostedEventMenuId = sittingId, HostedEventDiningTableId = table.Id,
                HostedEventBookingId = party.Id, People = people, DateCreated = now, CreatedByAppUserId = userId.Value,
            });
        }

        await db.SaveChangesAsync(ct);
        return Ok(await DescribeAsync(db, eventId, sittingId, $"{name} · {people} at {table.Name}.", ct));
    }

    [HttpDelete("sittings/{sittingId:guid}/seats/{seatId:guid}")]
    public async Task<ActionResult<HostedEventDiningRecord>> Unseat(Guid orgId, Guid eventId, Guid sittingId, Guid seatId, CancellationToken ct)
    {
        var userId = GetCurrentUserId();
        if (userId is null) return Unauthorized();

        await using var db = await DbFactory.CreateDbContextAsync(ct);
        if (!await _access.CanEditMenusAsync(userId.Value, orgId, eventId, db, ct)) return Forbid();
        if (await SittingAsync(db, orgId, eventId, sittingId, ct) is null) return NotFound();

        var seat = await db.HostedEventDiningSeats.FirstOrDefaultAsync(s => s.Id == seatId && s.HostedEventMenuId == sittingId, ct);
        if (seat is null) return NotFound();

        db.HostedEventDiningSeats.Remove(seat);
        await db.SaveChangesAsync(ct);
        return Ok(await DescribeAsync(db, eventId, sittingId, null, ct));
    }

    /// <summary>
    /// Seats this sitting the way another was seated: everybody there both times goes back to the same table, as far
    /// as the chairs allow.
    /// </summary>
    /// <remarks>
    /// Replaces this sitting's seating. Somebody who was at dinner and is not at breakfast is left out; a party that no
    /// longer fits where it sat is left unseated and named, rather than squeezed.
    /// </remarks>
    [HttpPost("sittings/{sittingId:guid}/same-as/{fromSittingId:guid}")]
    public async Task<ActionResult<HostedEventDiningRecord>> SameAs(
        Guid orgId, Guid eventId, Guid sittingId, Guid fromSittingId, CancellationToken ct)
    {
        var userId = GetCurrentUserId();
        if (userId is null) return Unauthorized();

        await using var db = await DbFactory.CreateDbContextAsync(ct);
        if (!await _access.CanEditMenusAsync(userId.Value, orgId, eventId, db, ct)) return Forbid();

        var menu = await SittingAsync(db, orgId, eventId, sittingId, ct);
        if (menu is null || await SittingAsync(db, orgId, eventId, fromSittingId, ct) is null) return NotFound();

        var here = (await PartiesAsync(db, eventId, menu.HostedEventNightId, ct)).ToDictionary(b => b.Id);
        var tables = await db.HostedEventDiningTables.AsNoTracking().Where(t => t.HostedEventId == eventId).ToDictionaryAsync(t => t.Id, ct);
        var from = await ConfirmedSeatsQuery(db, eventId).Where(s => s.HostedEventMenuId == fromSittingId)
            .OrderBy(s => s.DateCreated).ToListAsync(ct);

        await db.HostedEventDiningSeats.Where(s => s.HostedEventMenuId == sittingId).ExecuteDeleteAsync(ct);

        var now = DateTime.UtcNow;
        var placed = new List<HostedEventDiningSeat>();
        var leftOut = new List<string>();

        foreach (var seat in from)
        {
            if (!here.TryGetValue(seat.HostedEventBookingId, out var party) || !tables.TryGetValue(seat.HostedEventDiningTableId, out var table))
                continue;

            var name = party.LeadAppUser?.DisplayName ?? "A party";
            var (people, refusal) = DiningSeating.HowMany(table, name, EventCapacity.ClampPartySize(party.PartySize), placed, party.Id, seat.People);
            if (refusal is not null)
            {
                leftOut.Add(name);
                continue;
            }

            var copy = new HostedEventDiningSeat
            {
                Id = Guid.NewGuid(), HostedEventMenuId = sittingId, HostedEventDiningTableId = table.Id,
                HostedEventBookingId = party.Id, People = people, DateCreated = now, CreatedByAppUserId = userId.Value,
            };
            placed.Add(copy);
            db.HostedEventDiningSeats.Add(copy);
        }

        await db.SaveChangesAsync(ct);

        var sentence = leftOut.Count == 0
            ? $"Seated as before: {placed.Sum(p => p.People)} people."
            : $"Seated as before, except {string.Join(", ", leftOut.Distinct())}, who no longer fit where they sat.";
        return Ok(await DescribeAsync(db, eventId, sittingId, sentence, ct));
    }

    // ── the work ─────────────────────────────────────────────────────────────

    private static Task<HostedEventMenu?> SittingAsync(BenDataContext db, Guid orgId, Guid eventId, Guid sittingId, CancellationToken ct)
        => db.HostedEventMenus.AsNoTracking()
            .FirstOrDefaultAsync(m => m.Id == sittingId && m.HostedEventNight.HostedEventId == eventId
                                   && m.HostedEventNight.HostedEvent.OrganizationId == orgId, ct);

    /// <summary>Seats whose booking is still confirmed. Everything else has dropped off the room.</summary>
    private static IQueryable<HostedEventDiningSeat> ConfirmedSeatsQuery(BenDataContext db, Guid eventId)
        => db.HostedEventDiningSeats.AsNoTracking()
            .Where(s => s.HostedEventDiningTable.HostedEventId == eventId
                     && s.HostedEventBooking.Status == HostedEventBookingStatus.Confirmed);

    /// <summary>Confirmed parties there on a night, with their guests for the dietary notes.</summary>
    private static async Task<IReadOnlyList<HostedEventBooking>> PartiesAsync(BenDataContext db, Guid eventId, Guid nightId, CancellationToken ct)
    {
        var confirmed = await db.HostedEventBookings.AsNoTracking()
            .Include(b => b.LeadAppUser)
            .Include(b => b.Guests)
            .Include(b => b.Nights)
            .Where(b => b.HostedEventId == eventId && b.Status == HostedEventBookingStatus.Confirmed)
            .ToListAsync(ct);
        return EventDietary.OnNight(confirmed, nightId);
    }

    internal static async Task<HostedEventDiningRecord> DescribeAsync(
        BenDataContext db, Guid eventId, Guid? sittingId, string? sentence, CancellationToken ct)
    {
        var tables = await db.HostedEventDiningTables.AsNoTracking()
            .Where(t => t.HostedEventId == eventId).OrderBy(t => t.SortOrder)
            .Select(t => new HostedEventDiningTableRecord(t.Id, t.Name, t.Seats, t.SortOrder))
            .ToListAsync(ct);

        var menus = await db.HostedEventMenus.AsNoTracking().Include(m => m.HostedEventNight)
            .Where(m => m.HostedEventNight.HostedEventId == eventId)
            .ToListAsync(ct);
        var allSeats = await ConfirmedSeatsQuery(db, eventId).ToListAsync(ct);

        var sittings = menus
            .OrderBy(m => m.HostedEventNight.Date).ThenBy(m => m.SortOrder)
            .Select(m => new HostedEventDiningSittingRecord(m.Id, m.HostedEventNight.Date, m.Title, m.ServedAtLocal,
                allSeats.Where(s => s.HostedEventMenuId == m.Id).Sum(s => s.People)))
            .ToList();

        var chosen = sittingId is { } wanted && menus.FirstOrDefault(m => m.Id == wanted) is { } found
            ? found
            : menus.OrderBy(m => m.HostedEventNight.Date).ThenBy(m => m.SortOrder).FirstOrDefault();

        if (chosen is null)
            return new HostedEventDiningRecord(eventId, tables, sittings, null, [], [], sentence);

        var seats = allSeats.Where(s => s.HostedEventMenuId == chosen.Id).ToList();
        var parties = (await PartiesAsync(db, eventId, chosen.HostedEventNightId, ct))
            .OrderBy(b => b.LeadAppUser?.DisplayName)
            .Select(b => new HostedEventDiningPartyRecord(
                b.Id, b.LeadAppUser?.DisplayName ?? "Somebody", EventCapacity.ClampPartySize(b.PartySize),
                DiningSeating.OfParty(seats, b.Id),
                [.. b.Guests.OrderBy(g => g.SortOrder)
                    .Where(g => g.DietaryNotes is { Length: > 0 })
                    .Select(g => $"{g.DisplayName}: {g.DietaryNotes!.Trim()}")]))
            .ToList();

        return new HostedEventDiningRecord(eventId, tables, sittings, chosen.Id, parties,
            [.. seats.Select(s => new HostedEventDiningSeatRecord(s.Id, s.HostedEventDiningTableId, s.HostedEventBookingId, s.People))],
            sentence);
    }
}
