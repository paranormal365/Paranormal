using Ben.Data.Common.Enums;
using Ben.Data.Source.Context;
using Microsoft.EntityFrameworkCore;

namespace Ben.Data.WebApi.Services.Events;

/// <summary>
/// What every square on a plan is, for every night, in three queries (item 235 phase 4).
/// </summary>
/// <remarks>
/// <para><b>What it replaces.</b> The board asked <c>PeopleIn</c> once per unit per night, and each
/// call walked every booking on the event and every night of each. A thirteen-row house across
/// three nights with two hundred parties is roughly half a million list traversals to paint one
/// screen — all of it in memory, all of it repeated on every render, and all of it computing the
/// same handful of numbers over and over.</para>
///
/// <para><b>Three queries, whatever the size.</b> One for the units, one for the live holdings, one
/// for what people have merely asked for. Grouped in the database, which is the one place that
/// already has an index shaped exactly like the question.</para>
///
/// <para><b>It never returns a name.</b> The public seat picker and the organizer's board both read
/// this, and only one of them may know whose room the Blue Room is. So this answers what a square
/// IS and the board joins the names on separately — a shape where the private thing has to be
/// deliberately added rather than deliberately removed.</para>
/// </remarks>
public static class PlanOccupancy
{
    /// <summary>One square on one night: what holds it, and who has asked for it.</summary>
    /// <param name="BookingId">
    /// The party holding it, so the organizer's board can put a name on the square. Null when
    /// nothing holds it — and never returned to a guest, who is told only the state.
    /// </param>
    public readonly record struct Cell(
        Guid NightId,
        Guid UnitId,
        Guid? BookingId,
        HostedEventBookingStatus? HeldAs,
        int People,
        int Asked);

    /// <summary>Every square of an event's plan on every night, with what is on it.</summary>
    /// <remarks>
    /// A dictionary keyed by the pair, because every caller asks "what is this square" thousands of
    /// times and a list would put the linear scan straight back.
    /// </remarks>
    public static async Task<IReadOnlyDictionary<(Guid NightId, Guid UnitId), Cell>> ReadAsync(
        BenDataContext db, Guid hostedEventId, CancellationToken ct)
    {
        // ── what is actually held ─────────────────────────────────────────────
        //
        // IsHolding is the same flag the database's own arbiter index filters on, so this and the
        // rule that prevents double-booking can never disagree about what "held" means.
        var held = await db.HostedEventBookingNights.AsNoTracking()
            .Where(n => n.HostedEventBooking.HostedEventId == hostedEventId
                     && n.HostedEventLayoutUnitId != null
                     && n.IsHolding
                     && n.ReleasedUtc == null)
            .Select(n => new
            {
                NightId = n.HostedEventNightId,
                UnitId = n.HostedEventLayoutUnitId!.Value,
                BookingId = n.HostedEventBookingId,
                Status = n.HostedEventBooking.Status,
                People = n.People ?? n.HostedEventBooking.PartySize,
            })
            .ToListAsync(ct);

        // ── and what people have merely asked for ─────────────────────────────
        //
        // A request names a preference and holds nothing, so many parties may name one room. The
        // count is what turns "full" into "full, and eleven more would like it" — which is the
        // difference between a house a venue should leave alone and one it should find more rooms
        // for.
        var asked = await db.HostedEventBookingNights.AsNoTracking()
            .Where(n => n.HostedEventBooking.HostedEventId == hostedEventId
                     && n.HostedEventLayoutUnitId != null
                     && !n.IsHolding
                     && n.ReleasedUtc == null
                     && n.HostedEventBooking.Status == HostedEventBookingStatus.Requested)
            .GroupBy(n => new { n.HostedEventNightId, UnitId = n.HostedEventLayoutUnitId!.Value })
            .Select(g => new
            {
                g.Key.HostedEventNightId,
                g.Key.UnitId,
                People = g.Sum(n => n.People ?? n.HostedEventBooking.PartySize),
            })
            .ToListAsync(ct);

        var cells = new Dictionary<(Guid, Guid), Cell>();

        foreach (var row in held)
        {
            cells[(row.NightId, row.UnitId)] = new Cell(
                row.NightId, row.UnitId, row.BookingId, row.Status, Math.Max(1, row.People), 0);
        }

        foreach (var row in asked)
        {
            var key = (row.HostedEventNightId, row.UnitId);
            cells[key] = cells.TryGetValue(key, out var existing)
                ? existing with { Asked = row.People }
                : new Cell(row.HostedEventNightId, row.UnitId, null, null, 0, row.People);
        }

        return cells;
    }

    /// <summary>Every block on this event's units, which decide what is on offer at all.</summary>
    /// <remarks>
    /// The third query. Kept separate from the cells because a block is a fact about the VENUE's
    /// intentions rather than about who is coming, and folding them together would make "empty" and
    /// "not for sale" the same answer.
    /// </remarks>
    public static Task<List<Source.Entities.HostedEventUnitBlock>> BlocksAsync(
        BenDataContext db, Guid hostedEventId, CancellationToken ct)
        => db.HostedEventUnitBlocks.AsNoTracking()
            .Where(b => b.HostedEventLayoutUnit.HostedEventId == hostedEventId)
            .ToListAsync(ct);
}
