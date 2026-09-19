using AutoMapper;
using Ben.Data.Common.Enums;
using Ben.Data.Source.Context;
using Ben.Data.Source.Entities;
using Ben.Data.WebApi.Controllers.Cms;
using Ben.Service.Models.Entities;
using Ben.Service.RepositoryService.GenericInterfaces;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Ben.Data.WebApi.Controllers.Entities;

/// <summary>
/// The colours a venue's guests wear, and what each of them means (item 235 phase 7).
/// </summary>
/// <remarks>
/// <para>Ben, 2026-09-13: <i>"the organizer gets coloured wrist bands which mean different things
/// — blue could be the full event with food, purple the full event, red is day one … lets their
/// employees know by glance what a person is registered for."</i></para>
///
/// <para><b>Replace-the-set</b>, like the menus and the plan: this is one card a venue fills in
/// and saves, and half a set of bands is worse than none. Ids are carried across where a row is
/// being kept, so a party wearing a hand-picked band keeps wearing it when the venue edits the
/// wording.</para>
///
/// <para><b>Readable by anybody who may see the door or the board</b>, because both draw the chip.
/// Writable by whoever may edit the event — it is part of arranging one, not of running it.</para>
/// </remarks>
[Route("api/organizations/{orgId:guid}/events/{eventId:guid}/bands")]
public sealed class HostedEventBandController : OrgCmsControllerBase
{
    private readonly Services.Access.HostedEventAccess _access;

    public HostedEventBandController(
        IDbContextFactory<BenDataContext> dbFactory, IMapper mapper,
        IOrganizationSecurityService security,
        Services.Access.HostedEventAccess access)
        : base(dbFactory, mapper, security) { _access = access; }

    /// <summary>Every band this event has.</summary>
    [HttpGet]
    public async Task<ActionResult<HostedEventBandsRecord>> Get(
        Guid orgId, Guid eventId, CancellationToken ct)
    {
        var userId = GetCurrentUserId();
        if (userId is null) return Unauthorized();

        await using var db = await DbFactory.CreateDbContextAsync(ct);

        // The door draws these, and a steward may run a door without being able to edit anything.
        if (!await _access.CanRunTheDoorAsync(userId.Value, orgId, eventId, db, ct)
         && !await _access.CanReadBookingsAsync(userId.Value, orgId, eventId, db, ct)
         && !await _access.CanEditEventAsync(userId.Value, orgId, ct))
            return Forbid();

        if (!await db.HostedEvents.AnyAsync(e => e.Id == eventId && e.OrganizationId == orgId, ct))
            return NotFound();

        return Ok(await BandsAsync(db, eventId, ct));
    }

    /// <summary>
    /// Sets the whole set of bands, replacing whatever was there.
    /// </summary>
    /// <remarks>
    /// <para><b>A row kept keeps its id</b>, which is what stops an edit to the wording quietly
    /// unbanding every party who was given that colour by hand.</para>
    ///
    /// <para><b>A row removed is cleared off the bookings first.</b> The foreign key is NoAction —
    /// SQL Server will not have a second cascade path to the bookings — so the tidying is done
    /// here, deliberately, rather than left to a database that would simply refuse.</para>
    /// </remarks>
    [HttpPut]
    public async Task<ActionResult<HostedEventBandsRecord>> Set(
        Guid orgId, Guid eventId, [FromBody] SetHostedEventBandsRequest request, CancellationToken ct)
    {
        var userId = GetCurrentUserId();
        if (userId is null) return Unauthorized();

        await using var db = await DbFactory.CreateDbContextAsync(ct);
        if (!await _access.CanEditEventAsync(userId.Value, orgId, ct)) return Forbid();

        if (!await db.HostedEvents.AnyAsync(e => e.Id == eventId && e.OrganizationId == orgId, ct))
            return NotFound();

        var wanted = request.Bands ?? [];

        if (wanted.Any(b => string.IsNullOrWhiteSpace(b.Colour)))
            return BadRequest("Every band needs a colour — the word your stewards will use for it.");
        if (wanted.Any(b => string.IsNullOrWhiteSpace(b.Meaning)))
            return BadRequest("Say what each band means. A colour on its own tells a steward "
                            + "nothing they can act on.");

        var existing = await db.HostedEventBands
            .Where(b => b.HostedEventId == eventId)
            .ToListAsync(ct);

        var kept = wanted.Where(b => b.Id is not null).Select(b => b.Id!.Value).ToHashSet();
        var going = existing.Where(b => !kept.Contains(b.Id)).ToList();

        if (going.Count > 0)
        {
            var goingIds = going.Select(b => b.Id).ToList();

            // Cleared before the delete, because the reference is NoAction and the database would
            // otherwise refuse the whole save. A party whose colour has gone falls back to the
            // rules, which is the right answer: the venue stopped using that band.
            var wearing = await db.HostedEventBookings
                .Where(b => b.HostedEventBandId != null
                         && goingIds.Contains(b.HostedEventBandId.Value))
                .ToListAsync(ct);

            foreach (var booking in wearing) booking.HostedEventBandId = null;

            db.HostedEventBands.RemoveRange(going);
        }

        var now = DateTime.UtcNow;
        var order = 0;

        foreach (var input in wanted)
        {
            var band = input.Id is { } id ? existing.FirstOrDefault(b => b.Id == id) : null;

            if (band is null)
            {
                band = new HostedEventBand
                {
                    Id = Guid.NewGuid(),
                    HostedEventId = eventId,
                    DateCreated = now,
                    CreatedByAppUserId = userId.Value,
                };
                db.HostedEventBands.Add(band);
            }
            else
            {
                band.DateUpdated = now;
                band.UpdatedByAppUserId = userId.Value;
            }

            band.Colour = input.Colour.Trim();
            band.Meaning = input.Meaning.Trim();
            band.Hex = Trimmed(input.Hex);
            band.Rule = input.Rule;
            // Position in the list is the order, and the order is what decides which rule wins.
            band.SortOrder = order++;
        }

        await db.SaveChangesAsync(ct);

        return Ok(await BandsAsync(db, eventId, ct));
    }

    /// <summary>
    /// Gives one party a band by hand, or puts them back on the rules.
    /// </summary>
    /// <remarks>
    /// The escape hatch, and today the only way to say anything about food: nothing on a booking
    /// records that a party is eating, so a colour meaning dinner cannot be derived from what the
    /// site knows. Deciding a booking is what it takes — this is a statement about a guest.
    /// </remarks>
    [HttpPost("~/api/organizations/{orgId:guid}/events/{eventId:guid}/bookings/{bookingId:guid}/band")]
    public async Task<ActionResult<HostedEventBandsRecord>> SetForBooking(
        Guid orgId, Guid eventId, Guid bookingId,
        [FromBody] SetHostedEventBookingBandRequest request, CancellationToken ct)
    {
        var userId = GetCurrentUserId();
        if (userId is null) return Unauthorized();

        await using var db = await DbFactory.CreateDbContextAsync(ct);
        if (!await _access.CanDecideBookingsAsync(userId.Value, orgId, eventId, db, ct))
            return Forbid();

        var booking = await db.HostedEventBookings
            .FirstOrDefaultAsync(b => b.Id == bookingId && b.HostedEventId == eventId, ct);
        if (booking is null) return NotFound();

        if (request.HostedEventBandId is { } bandId
            && !await db.HostedEventBands.AnyAsync(
                   b => b.Id == bandId && b.HostedEventId == eventId, ct))
            return BadRequest("That band isn't one of this event's.");

        booking.HostedEventBandId = request.HostedEventBandId;
        booking.DateUpdated = DateTime.UtcNow;
        booking.UpdatedByAppUserId = userId.Value;

        await db.SaveChangesAsync(ct);

        return Ok(await BandsAsync(db, eventId, ct));
    }

    // ── the work ─────────────────────────────────────────────────────────────

    public static async Task<HostedEventBandsRecord> BandsAsync(
        BenDataContext db, Guid eventId, CancellationToken ct)
        => new(eventId,
            [.. await db.HostedEventBands.AsNoTracking()
                .Where(b => b.HostedEventId == eventId)
                .OrderBy(b => b.SortOrder)
                .Select(b => new HostedEventBandRecord(
                    b.Id, b.Colour, b.Meaning, b.Hex, b.Rule, b.SortOrder))
                .ToListAsync(ct)]);

    private static string? Trimmed(string? value)
        => value?.Trim() is { Length: > 0 } v ? v : null;
}
