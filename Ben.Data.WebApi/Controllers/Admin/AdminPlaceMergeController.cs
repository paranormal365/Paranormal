using Ben.Data.Common.Constants;
using Ben.Data.Source.Context;
using Ben.Data.Source.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Ben.Data.WebApi.Controllers.Admin;

/// <summary>
/// Folding two records of one place into one.
/// </summary>
/// <remarks>
/// <para><b>Why this has to exist.</b> The archive's entire value is that everybody who records at
/// a location lands on the same page. Matching prevents most duplicates and a picker prevents most
/// of the rest, but neither is perfect and neither can heal what already exists — one afternoon's
/// testing left three "Bell Witch Cave" records, which is precisely the mess the feature promises
/// not to make. Without a merge the only cure is a database console.</para>
///
/// <para><b>It moves everything, then deletes.</b> Investigations, cases, calendar events, field
/// sessions and rooms all point at places; leaving any behind would orphan somebody's work at a
/// record nothing links to any more. The delete is the last statement, in the same transaction, so
/// a failure half way leaves both places intact rather than one gutted.</para>
///
/// <para><b>SuperAdmin only, and irreversible.</b> Nothing records which place a row used to point
/// at, so a merge cannot be undone by anything short of restoring a backup. That is acceptable for
/// a tool used to fix duplicates and would not be for anything a group could reach.</para>
/// </remarks>
[ApiController]
[Authorize(Policy = RoleNames.SuperAdmin)]
[Route("api/admin/places")]
public sealed class AdminPlaceMergeController : BenControllerBase
{
    private readonly IDbContextFactory<BenDataContext> _db;
    private readonly IAuditLogService _auditLog;
    private readonly ILogger<AdminPlaceMergeController> _log;

    public AdminPlaceMergeController(
        IDbContextFactory<BenDataContext> db, IAuditLogService auditLog,
        ILogger<AdminPlaceMergeController> log)
    {
        _db = db;
        _auditLog = auditLog;
        _log = log;
    }

    private static async Task<int> RepointAsync<T>(
        IQueryable<T> rows, Action<T> repoint, CancellationToken ct) where T : class
    {
        var loaded = await rows.ToListAsync(ct);
        foreach (var row in loaded) repoint(row);
        return loaded.Count;
    }

    /// <param name="IntoPlaceId">The record that survives and inherits everything.</param>
    public sealed record MergeRequest(Guid IntoPlaceId);

    /// <param name="Moved">What was repointed, so the caller can see the merge did something.</param>
    public sealed record MergeResult(
        Guid SurvivingPlaceId, int Investigations, int Cases, int CalendarEvents,
        int FieldSessions, int Rooms);

    /// <summary>Moves everything from one place onto another and deletes the empty one.</summary>
    [HttpPost("{id:guid}/merge")]
    public async Task<ActionResult<MergeResult>> Merge(
        Guid id, [FromBody] MergeRequest request, CancellationToken ct)
    {
        if (id == request.IntoPlaceId)
            return BadRequest("A place cannot be merged into itself.");

        var userId = GetCurrentUserIdOrThrow();
        await using var db = await _db.CreateDbContextAsync(ct);

        var losing = await db.Places.FirstOrDefaultAsync(p => p.Id == id, ct);
        if (losing is null) return NotFound("That place doesn't exist.");
        if (!await db.Places.AnyAsync(p => p.Id == request.IntoPlaceId, ct))
            return NotFound("The place to merge into doesn't exist.");

        await using var transaction = await db.Database.BeginTransactionAsync(ct);

        // Tracked updates rather than ExecuteUpdate. One SQL statement per table would be
        // tidier, but a merge touches one place's rows — tens, not thousands — and the tracked
        // form is the one that can be exercised against the in-memory provider the rest of these
        // tests use. An untested merge tool is worse than a slightly slower one.
        var investigations = await RepointAsync(db.Investigations.Where(x => x.PlaceId == id),
            x => x.PlaceId = request.IntoPlaceId, ct);
        var cases = await RepointAsync(db.Cases.Where(x => x.PlaceId == id),
            x => x.PlaceId = request.IntoPlaceId, ct);
        var events = await RepointAsync(db.OrgCalendarEvents.Where(x => x.PlaceId == id),
            x => x.PlaceId = request.IntoPlaceId, ct);
        var sessions = await RepointAsync(db.FieldSessionUploads.Where(x => x.PlaceId == id),
            x => x.PlaceId = request.IntoPlaceId, ct);

        // Rooms are named PER GROUP for a shared place (item 197), so two records of one building
        // can carry rooms of the same name from different groups. Moving them all is right — the
        // pair remains distinguishable by the group that named them.
        var rooms = await RepointAsync(db.PlaceRooms.Where(x => x.PlaceId == id),
            x => x.PlaceId = request.IntoPlaceId, ct);

        db.Places.Remove(losing);
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);

        await _auditLog.LogDeleteAsync(nameof(Ben.Data.Source.Entities.Place), id, losing,
            userId, AppSources.WebApi);

        _log.LogInformation(
            "Place {Losing} merged into {Surviving}: {Investigations} investigations, {Cases} cases, "
          + "{Events} events, {Sessions} sessions, {Rooms} rooms moved.",
            id, request.IntoPlaceId, investigations, cases, events, sessions, rooms);

        return Ok(new MergeResult(
            request.IntoPlaceId, investigations, cases, events, sessions, rooms));
    }
}
