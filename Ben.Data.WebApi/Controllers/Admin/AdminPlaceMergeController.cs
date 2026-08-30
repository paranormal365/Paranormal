using Ben.Data.Common.Constants;
using Ben.Data.Common.Enums;
using Ben.Data.WebApi.Services.Places;
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

    /// <summary>
    /// Places that look like duplicates of each other, grouped.
    /// </summary>
    /// <remarks>
    /// <para><b>A finder, not a list.</b> Showing every place and asking somebody to spot the
    /// pairs is the job the computer should be doing — and the pairs are exactly what the
    /// automatic matcher could not decide on its own, so they are few. Anything within the
    /// matcher's own radius of another place is offered here, with what each record is carrying,
    /// because "this one has three sessions and that one has none" is the whole decision.</para>
    ///
    /// <para><b>Proximity only, deliberately loose.</b> No name test: the pairs worth surfacing
    /// are the ones the name rules already failed on. A human reading two rows decides in a
    /// second what no string comparison was going to get right.</para>
    /// </remarks>
    [HttpGet("duplicates")]
    public async Task<ActionResult<IReadOnlyList<DuplicatePlaceGroup>>> GetDuplicates(
        CancellationToken ct)
    {
        await using var db = await _db.CreateDbContextAsync(ct);

        // Projected straight into the row the caller gets, with the coordinates alongside.
        // An anonymous type read back through `dynamic` was the first shape of this, and it threw
        // at runtime on every group: a `decimal?` boxes as a plain `decimal`, so `.Value` binds
        // to nothing. Typed all the way through, the compiler has the argument instead.
        var places = await db.Places.AsNoTracking()
            .Where(p => p.Latitude != null && p.Longitude != null)
            .Select(p => new Candidate(
                (double)p.Latitude!.Value, (double)p.Longitude!.Value,
                new DuplicatePlaceRow(
                    p.Id, p.Name, p.StreetAddress1, p.City, p.State, p.Kind, p.DateCreated,
                    db.Investigations.Count(x => x.PlaceId == p.Id),
                    db.Cases.Count(x => x.PlaceId == p.Id),
                    db.OrgCalendarEvents.Count(x => x.PlaceId == p.Id),
                    db.FieldSessionUploads.Count(x => x.PlaceId == p.Id),
                    db.FieldSessionUploads.Count(x => x.PlaceId == p.Id && x.PublishedAtUtc != null),
                    db.PlaceRooms.Count(x => x.PlaceId == p.Id))))
            .ToListAsync(ct);

        // Single-link clustering: A near B and B near C puts all three in one group, which is
        // what a row of near-identical records actually looks like once somebody has typed the
        // same landmark three times.
        var remaining = places.ToList();
        var groups = new List<DuplicatePlaceGroup>();

        while (remaining.Count > 0)
        {
            var seed = remaining[0];
            remaining.RemoveAt(0);
            var cluster = new List<Candidate> { seed };
            var frontier = new Queue<Candidate>();
            frontier.Enqueue(seed);

            while (frontier.Count > 0)
            {
                var current = frontier.Dequeue();
                for (var i = remaining.Count - 1; i >= 0; i--)
                {
                    var other = remaining[i];
                    if (PlaceMatcher.DistanceMiles(
                            current.Latitude, current.Longitude,
                            other.Latitude, other.Longitude)
                        >= PlaceMatcher.MatchRadiusMiles) continue;

                    cluster.Add(other);
                    frontier.Enqueue(other);
                    remaining.RemoveAt(i);
                }
            }

            if (cluster.Count < 2) continue;   // a place alone is not a duplicate of anything

            groups.Add(new DuplicatePlaceGroup(
                [.. cluster.Select(c => c.Row)
                    .OrderByDescending(r => r.PublishedSessions)
                    .ThenByDescending(r => r.Investigations + r.Cases + r.Sessions)
                    .ThenBy(r => r.DateCreated)]));
        }

        // Busiest clusters first: the duplicate that is actually splitting an archive matters
        // more than two empty records nobody has reached.
        return Ok(groups
            .OrderByDescending(g => g.Places.Sum(p => p.PublishedSessions))
            .ThenByDescending(g => g.Places.Count)
            .ToList());
    }

    /// <summary>A place with its coordinates kept out to one side, for the distance pass only.</summary>
    private sealed record Candidate(double Latitude, double Longitude, DuplicatePlaceRow Row);

    /// <summary>Places close enough to each other to be one place typed twice.</summary>
    /// <remarks>Ordered so the record carrying the most published work comes first — it is
    /// almost always the one that should survive, and the screen defaults to it.</remarks>
    public sealed record DuplicatePlaceGroup(IReadOnlyList<DuplicatePlaceRow> Places);

    /// <param name="PublishedSessions">The count that decides which record should survive: an
    /// archive people can already read is the one with something to lose.</param>
    public sealed record DuplicatePlaceRow(
        Guid Id, string? Name, string? StreetAddress1, string? City, string? State,
        PlaceKind Kind, DateTime DateCreated,
        int Investigations, int Cases, int Events, int Sessions, int PublishedSessions, int Rooms);

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
