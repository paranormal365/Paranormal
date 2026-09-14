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
/// The host's side of an event's programme: sessions, publishing them, and who signed up
/// (item 235 phase 10).
/// </summary>
/// <remarks>
/// <para><b>Drafted in private, then published.</b> Nobody but the host sees a session until the
/// programme is published, so a half-planned weekend never shows a guest a class that is about to
/// move.</para>
///
/// <para><b>After publishing, changes are told.</b> A session whose time or place changes is marked
/// and everybody with a place is written to; a cancelled one writes to everybody, queue included;
/// raising a capacity moves the queue up and tells whoever got in. A session people have signed up
/// for cannot be deleted — it is cancelled, so they hear.</para>
/// </remarks>
[Route("api/organizations/{orgId:guid}/events/{eventId:guid}/sessions")]
public sealed class HostedEventSessionController : OrgCmsControllerBase
{
    private readonly Services.Access.HostedEventAccess _access;

    public HostedEventSessionController(
        IDbContextFactory<BenDataContext> dbFactory, IMapper mapper, IOrganizationSecurityService security,
        Services.Access.HostedEventAccess access)
        : base(dbFactory, mapper, security)
    { _access = access; }

    [HttpGet]
    public async Task<ActionResult<HostedEventProgrammeRecord>> Get(Guid orgId, Guid eventId, CancellationToken ct)
    {
        var userId = GetCurrentUserId();
        if (userId is null) return Unauthorized();
        if (!await _access.CanReadEventAsync(userId.Value, orgId, ct)) return Forbid();

        await using var db = await DbFactory.CreateDbContextAsync(ct);
        var hosted = await EventAsync(db, orgId, eventId, ct);
        return hosted is null ? NotFound() : Ok(await ProgrammeAsync(db, hosted, null, ct));
    }

    [HttpPost]
    public Task<ActionResult<HostedEventProgrammeRecord>> Create(
        Guid orgId, Guid eventId, [FromBody] SaveHostedEventSessionRequest request,
        [FromServices] EventGuestMailer mail, CancellationToken ct)
        => SaveAsync(orgId, eventId, null, request, mail, ct);

    [HttpPut("{sessionId:guid}")]
    public Task<ActionResult<HostedEventProgrammeRecord>> Update(
        Guid orgId, Guid eventId, Guid sessionId, [FromBody] SaveHostedEventSessionRequest request,
        [FromServices] EventGuestMailer mail, CancellationToken ct)
        => SaveAsync(orgId, eventId, sessionId, request, mail, ct);

    [HttpPost("{sessionId:guid}/cancel")]
    public async Task<ActionResult<HostedEventProgrammeRecord>> Cancel(
        Guid orgId, Guid eventId, Guid sessionId, [FromBody] CancelHostedEventSessionRequest request,
        [FromServices] EventGuestMailer mail, CancellationToken ct)
    {
        var userId = GetCurrentUserId();
        if (userId is null) return Unauthorized();
        if (!await _access.CanEditEventAsync(userId.Value, orgId, ct)) return Forbid();

        await using var db = await DbFactory.CreateDbContextAsync(ct);
        var hosted = await EventAsync(db, orgId, eventId, ct);
        if (hosted is null) return NotFound();
        var session = await db.HostedEventSessions.FirstOrDefaultAsync(s => s.Id == sessionId && s.HostedEventId == eventId, ct);
        if (session is null) return NotFound();
        if (session.CalledOffUtc is not null) return Ok(await ProgrammeAsync(db, hosted, null, ct));

        var now = DateTime.UtcNow;
        session.CalledOffUtc = now;
        session.CancelledReason = request.Reason?.Trim() is { Length: > 0 } r ? (r.Length > 1000 ? r[..1000] : r) : null;
        if (hosted.ProgrammePublishedUtc is not null) session.ChangedUtc = now;
        session.DateUpdated = now;
        session.UpdatedByAppUserId = userId.Value;
        await db.SaveChangesAsync(ct);

        var told = await mail.SendSessionCancelledAsync(db, sessionId, session.CancelledReason, ct);
        return Ok(await ProgrammeAsync(db, hosted,
            $"{session.Title} is cancelled. " + (told == 0 ? "Nobody had signed up." : $"{Plural(told, "person", "people")} signed up {(told == 1 ? "has" : "have")} been told."), ct));
    }

    [HttpDelete("{sessionId:guid}")]
    public async Task<ActionResult<HostedEventProgrammeRecord>> Delete(Guid orgId, Guid eventId, Guid sessionId, CancellationToken ct)
    {
        var userId = GetCurrentUserId();
        if (userId is null) return Unauthorized();
        if (!await _access.CanEditEventAsync(userId.Value, orgId, ct)) return Forbid();

        await using var db = await DbFactory.CreateDbContextAsync(ct);
        var hosted = await EventAsync(db, orgId, eventId, ct);
        if (hosted is null) return NotFound();
        var session = await db.HostedEventSessions.Include(s => s.SignUps)
            .FirstOrDefaultAsync(s => s.Id == sessionId && s.HostedEventId == eventId, ct);
        if (session is null) return NotFound();

        if (session.SignUps.Count > 0)
            return Conflict("People have signed up for this one. Cancel it instead, so they're told.");

        db.HostedEventSessions.Remove(session);
        await db.SaveChangesAsync(ct);
        return Ok(await ProgrammeAsync(db, hosted, $"{session.Title} is off the programme.", ct));
    }

    /// <summary>Puts the programme in front of guests.</summary>
    [HttpPost("~/api/organizations/{orgId:guid}/events/{eventId:guid}/programme/publish")]
    public async Task<ActionResult<HostedEventProgrammeRecord>> Publish(Guid orgId, Guid eventId, CancellationToken ct)
    {
        var userId = GetCurrentUserId();
        if (userId is null) return Unauthorized();
        if (!await _access.CanEditEventAsync(userId.Value, orgId, ct)) return Forbid();

        await using var db = await DbFactory.CreateDbContextAsync(ct);
        var hosted = await db.HostedEvents.FirstOrDefaultAsync(e => e.Id == eventId && e.OrganizationId == orgId, ct);
        if (hosted is null) return NotFound();
        if (!await db.HostedEventSessions.AnyAsync(s => s.HostedEventId == eventId && s.CalledOffUtc == null, ct))
            return Conflict("Add a session before publishing the programme.");

        hosted.ProgrammePublishedUtc ??= DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
        return Ok(await ProgrammeAsync(db, (await EventAsync(db, orgId, eventId, ct))!, "The programme is on the event's page.", ct));
    }

    /// <summary>Takes the programme back into draft, while nobody has signed up for anything.</summary>
    [HttpPost("~/api/organizations/{orgId:guid}/events/{eventId:guid}/programme/unpublish")]
    public async Task<ActionResult<HostedEventProgrammeRecord>> Unpublish(Guid orgId, Guid eventId, CancellationToken ct)
    {
        var userId = GetCurrentUserId();
        if (userId is null) return Unauthorized();
        if (!await _access.CanEditEventAsync(userId.Value, orgId, ct)) return Forbid();

        await using var db = await DbFactory.CreateDbContextAsync(ct);
        var hosted = await db.HostedEvents.FirstOrDefaultAsync(e => e.Id == eventId && e.OrganizationId == orgId, ct);
        if (hosted is null) return NotFound();
        if (await db.HostedEventSessionSignUps.AnyAsync(s => s.HostedEventSession.HostedEventId == eventId, ct))
            return Conflict("Guests have signed up for sessions, so the programme stays up. Change or cancel sessions instead.");

        hosted.ProgrammePublishedUtc = null;
        await db.SaveChangesAsync(ct);
        return Ok(await ProgrammeAsync(db, (await EventAsync(db, orgId, eventId, ct))!, "The programme is back in draft.", ct));
    }

    /// <summary>Who has a place, and who is waiting, in order.</summary>
    [HttpGet("{sessionId:guid}/roster")]
    public async Task<ActionResult<HostedEventSessionRosterRecord>> Roster(Guid orgId, Guid eventId, Guid sessionId, CancellationToken ct)
    {
        var userId = GetCurrentUserId();
        if (userId is null) return Unauthorized();

        await using var db = await DbFactory.CreateDbContextAsync(ct);
        if (!await _access.CanReadBookingsAsync(userId.Value, orgId, eventId, db, ct)) return Forbid();

        var session = await db.HostedEventSessions.AsNoTracking()
            .Include(s => s.SignUps).ThenInclude(u => u.AppUser)
            .FirstOrDefaultAsync(s => s.Id == sessionId && s.HostedEventId == eventId && s.HostedEvent.OrganizationId == orgId, ct);
        if (session is null) return NotFound();

        HostedEventSessionRosterLine Line(HostedEventSessionSignUp u) => new(
            u.Id, u.AppUser?.DisplayName ?? u.AppUser?.Email ?? "A guest", u.People, u.DateCreated, u.HostedEventBookingId is null);

        return Ok(new HostedEventSessionRosterRecord(
            session.Id, session.Title,
            [.. session.SignUps.Where(u => u.WaitlistedUtc is null).OrderBy(u => u.DateCreated).Select(Line)],
            [.. session.SignUps.Where(u => u.WaitlistedUtc is not null).OrderBy(u => u.WaitlistedUtc).ThenBy(u => u.DateCreated).Select(Line)]));
    }

    /// <summary>Takes somebody off a session, and moves the queue up.</summary>
    [HttpDelete("{sessionId:guid}/sign-ups/{signUpId:guid}")]
    public async Task<ActionResult<HostedEventSessionRosterRecord>> RemoveSignUp(
        Guid orgId, Guid eventId, Guid sessionId, Guid signUpId,
        [FromServices] EventGuestMailer mail, CancellationToken ct)
    {
        var userId = GetCurrentUserId();
        if (userId is null) return Unauthorized();

        await using (var db = await DbFactory.CreateDbContextAsync(ct))
        {
            if (!await _access.CanDecideBookingsAsync(userId.Value, orgId, eventId, db, ct)) return Forbid();
            if (!await db.HostedEventSessions.AnyAsync(s => s.Id == sessionId && s.HostedEventId == eventId && s.HostedEvent.OrganizationId == orgId, ct))
                return NotFound();
        }

        var (left, promoted) = await SessionSignUps.LeaveAsync(DbFactory, sessionId, signUpId, userId.Value, DateTime.UtcNow, ct);
        if (!left) return NotFound();

        await using var read = await DbFactory.CreateDbContextAsync(ct);
        if (promoted.Count > 0) await mail.SendSessionPromotedAsync(read, promoted, ct);
        return await Roster(orgId, eventId, sessionId, ct);
    }

    // ── the plumbing ─────────────────────────────────────────────────────────

    private async Task<ActionResult<HostedEventProgrammeRecord>> SaveAsync(
        Guid orgId, Guid eventId, Guid? sessionId, SaveHostedEventSessionRequest request, EventGuestMailer mail, CancellationToken ct)
    {
        var userId = GetCurrentUserId();
        if (userId is null) return Unauthorized();
        if (!await _access.CanEditEventAsync(userId.Value, orgId, ct)) return Forbid();

        await using var db = await DbFactory.CreateDbContextAsync(ct);
        var hosted = await EventAsync(db, orgId, eventId, ct);
        if (hosted is null) return NotFound();

        var session = sessionId is Guid id
            ? await db.HostedEventSessions.Include(s => s.SignUps).FirstOrDefaultAsync(s => s.Id == id && s.HostedEventId == eventId, ct)
            : null;
        if (sessionId is not null && session is null) return NotFound();
        if (session?.CalledOffUtc is not null) return Conflict("That session has been cancelled. Add a new one instead.");

        var (startsUtc, endsUtc) = SessionSignUps.ToUtc(hosted, request.Date, request.StartsLocal, request.EndsLocal);
        var nights = hosted.Nights.Select(n => n.Date).ToList();
        if (SessionSignUps.WhyNotValid(hosted, nights, request.Title, startsUtc, endsUtc, request.Capacity,
                session?.PlacesTaken ?? 0) is { } refusal)
            return BadRequest(refusal);

        if (request.PlaceRoomId is Guid roomId && !await db.PlaceRooms.AnyAsync(r => r.Id == roomId && r.PlaceId == hosted.PlaceId, ct))
            return BadRequest("That room isn't at this event's venue.");

        var now = DateTime.UtcNow;
        var moved = false;
        IReadOnlyList<Guid> promoted = [];

        if (session is null)
        {
            session = new HostedEventSession
            {
                Id = Guid.NewGuid(), HostedEventId = eventId,
                SortOrder = await db.HostedEventSessions.CountAsync(s => s.HostedEventId == eventId, ct),
                DateCreated = now, CreatedByAppUserId = userId.Value,
            };
            db.HostedEventSessions.Add(session);
        }
        else
        {
            moved = hosted.ProgrammePublishedUtc is not null
                 && (session.StartsAtUtc != startsUtc || session.EndsAtUtc != endsUtc
                     || session.PlaceRoomId != request.PlaceRoomId || session.LocationText != Trim(request.LocationText));
            session.DateUpdated = now;
            session.UpdatedByAppUserId = userId.Value;
        }

        session.Title = request.Title.Trim();
        session.Description = Trim(request.Description);
        session.StartsAtUtc = startsUtc;
        session.EndsAtUtc = endsUtc;
        session.PlaceRoomId = request.PlaceRoomId;
        session.LocationText = request.PlaceRoomId is null ? Trim(request.LocationText) : null;
        session.LedBy = Trim(request.LedBy);
        session.Capacity = request.Capacity;
        session.RequiresSignUp = request.RequiresSignUp;
        if (moved) session.ChangedUtc = now;

        // A raised limit, or none at all, lets the queue in.
        if (session.SignUps.Count > 0) promoted = SessionSignUps.Promote(session, userId.Value, now);

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            return Conflict("Somebody signed up while you were editing. Open the programme again and make the change once more.");
        }

        if (moved) await mail.SendSessionMovedAsync(db, session.Id, ct);
        if (promoted.Count > 0) await mail.SendSessionPromotedAsync(db, promoted, ct);

        return Ok(await ProgrammeAsync(db, hosted, sessionId is null ? $"Added {session.Title}." : $"Saved {session.Title}.", ct));
    }

    private static Task<HostedEvent?> EventAsync(BenDataContext db, Guid orgId, Guid eventId, CancellationToken ct)
        => db.HostedEvents.AsNoTracking().Include(e => e.Nights)
            .FirstOrDefaultAsync(e => e.Id == eventId && e.OrganizationId == orgId, ct);

    private static async Task<HostedEventProgrammeRecord> ProgrammeAsync(BenDataContext db, HostedEvent hosted, string? note, CancellationToken ct)
    {
        var published = await db.HostedEvents.AsNoTracking().Where(e => e.Id == hosted.Id)
            .Select(e => e.ProgrammePublishedUtc).FirstAsync(ct);

        var sessions = await db.HostedEventSessions.AsNoTracking()
            .Where(s => s.HostedEventId == hosted.Id)
            .OrderBy(s => s.StartsAtUtc).ThenBy(s => s.SortOrder)
            .Select(s => new HostedEventSessionRecord(
                s.Id, s.Title, s.Description, s.StartsAtUtc, s.EndsAtUtc, s.PlaceRoomId, s.LocationText,
                s.PlaceRoom != null ? s.PlaceRoom.Name : s.LocationText,
                s.LedBy, s.Capacity, s.RequiresSignUp, s.PlacesTaken,
                s.SignUps.Count(u => u.WaitlistedUtc != null),
                s.CalledOffUtc != null, s.CancelledReason, s.ChangedUtc))
            .ToListAsync(ct);

        return new(hosted.Id, hosted.TimeZoneId, published,
            [.. hosted.Nights.Select(n => n.Date.Date).OrderBy(d => d)], sessions, note);
    }

    private static string? Trim(string? value) => value?.Trim() is { Length: > 0 } v ? v : null;

    private static string Plural(int n, string one, string many) => n == 1 ? $"1 {one}" : $"{n} {many}";
}
