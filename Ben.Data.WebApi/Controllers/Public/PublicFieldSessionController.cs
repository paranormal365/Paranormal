using Ben.Data.Common.Enums;
using Ben.Data.Common.Interfaces;
using Ben.Data.Source.Context;
using Ben.Data.WebApi.Services.FieldSessions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Ben.Data.WebApi.Controllers.Public;

/// <summary>
/// Published field sessions, found by where they were recorded, and the public copy of each.
/// </summary>
/// <remarks>
/// <para>Ben, 2026-09-27: "when looking up where you are, be able to have a server lookup to see if
/// there are any available public .ben files from nearby. Also, be able to look up .ben files based
/// on looking up locations." The archive already lists a place's sessions on the place's page; this
/// is the same set, reached from a position or a name instead of from a page.</para>
///
/// <para><b>One gate, the archive's own.</b> A session appears here exactly when it would appear
/// on its place's page: published, at a place, and the place a public location. Media rides on the
/// same approval the archive's media door uses.</para>
///
/// <para><b>Distances are measured to the place's PUBLIC point</b> — the approximate one its page
/// shows — never its stored one, for the same reason <see cref="SearchController"/> gives: a true
/// distance next to an approximate position hands the position back to anybody who asks from three
/// places.</para>
/// </remarks>
[ApiController]
[Route("api/public/field-sessions")]
[AllowAnonymous]
public sealed class PublicFieldSessionController : ControllerBase
{
    private readonly IDbContextFactory<BenDataContext> _db;
    private readonly IFileStorageService _fileStorage;
    private readonly IBenBundleStore _bundles;

    /// <summary>The most rows a lookup returns.</summary>
    internal const int MostRows = 50;

    public PublicFieldSessionController(IDbContextFactory<BenDataContext> db,
                                        IFileStorageService fileStorage, IBenBundleStore bundles)
    {
        _db = db;
        _fileStorage = fileStorage;
        _bundles = bundles;
    }

    /// <summary>Published sessions recorded near a point, nearest first.</summary>
    [HttpGet("nearby")]
    public async Task<ActionResult<IReadOnlyList<PublicArchiveSessionRow>>> Nearby(
        [FromQuery] double latitude, [FromQuery] double longitude,
        [FromQuery] double radiusMiles = 10, CancellationToken ct = default)
    {
        if (latitude is < -90 or > 90 || longitude is < -180 or > 180)
            return BadRequest("That position isn't on the map.");
        // Never tighter than the public cell: a smaller circle would say which side of the cell
        // the real place sits.
        var radius = Math.Clamp(radiusMiles, PublicCoordinates.RadiusMiles, 100);

        var rows = await PublishedAsync(ct);
        var near = rows
            .Select(r => r with { DistanceMiles = r.PublicLatitude is double lat && r.PublicLongitude is double lon
                ? Math.Round(Miles(latitude, longitude, lat, lon), 1) : null })
            .Where(r => r.DistanceMiles is double d && d <= radius)
            .OrderBy(r => r.DistanceMiles)
            .ThenByDescending(r => r.StartedAt)
            .Take(MostRows)
            .ToList();
        return Ok(near);
    }

    /// <summary>Published sessions at places whose name, town or state matches.</summary>
    [HttpGet("search")]
    public async Task<ActionResult<IReadOnlyList<PublicArchiveSessionRow>>> Search(
        [FromQuery] string? query, CancellationToken ct = default)
    {
        var words = (query ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (words.Length == 0 || query!.Trim().Length < 2)
            return BadRequest("Type at least two letters of a place or a town.");

        var rows = await PublishedAsync(ct);
        // Every word must appear somewhere in the place's name, town or state, so "waverly
        // louisville" finds Waverly Hills in Louisville and "hills" alone finds all of them.
        var found = rows
            .Where(r => words.All(w =>
                (r.PlaceName ?? "").Contains(w, StringComparison.OrdinalIgnoreCase)
                || (r.PlaceCity ?? "").Contains(w, StringComparison.OrdinalIgnoreCase)
                || (r.PlaceState ?? "").Contains(w, StringComparison.OrdinalIgnoreCase)))
            .OrderByDescending(r => r.StartedAt)
            .Take(MostRows)
            .ToList();
        return Ok(found);
    }

    /// <summary>
    /// The public copy of one published session, as a <c>.ben</c> — see <see cref="PublicSessionBundle"/>.
    /// </summary>
    /// <remarks>
    /// 404 for anything the archive would not list, whatever the reason, so the answer never says
    /// which ids are real. Built once per state of the session and kept: the key carries when it
    /// was published and how its media stands, so unpublishing, republishing, a flag or an approval
    /// each lead to a fresh copy and a stale one is never served.
    /// </remarks>
    [HttpGet("{sessionId:guid}/bundle")]
    public async Task<IActionResult> Bundle(Guid sessionId, CancellationToken ct = default)
    {
        await using var db = await _db.CreateDbContextAsync(ct);
        var session = await Publishable(db)
            .Where(s => s.Id == sessionId)
            .Select(s => new
            {
                s.Id, s.IsBundle, s.PublishedAtUtc, s.MediaReviewState, s.StartedAt,
                s.RecordedByAppUserId, s.SubmittedByAppUserId,
                PlaceLatitude = s.Place!.Latitude, PlaceLongitude = s.Place.Longitude,
                PlaceName = s.Place.Name,
                StoragePath = s.DocumentUploadFile.StoragePath,
            })
            .FirstOrDefaultAsync(ct);
        if (session is null || !session.IsBundle
            || session.StoragePath is not { Length: > 0 } original || !_fileStorage.Exists(original))
        {
            return NotFound();
        }

        var approved = session.MediaReviewState == FeedMediaReviewState.Approved;
        var cached = $"public-bundles/{session.Id:N}-{session.PublishedAtUtc!.Value.Ticks}-{(int)session.MediaReviewState}.ben";
        if (!_fileStorage.Exists(cached))
        {
            var (lat, lon) = PublicCoordinates.Approximate(session.PlaceLatitude, session.PlaceLongitude);
            var scratch = Path.Combine(Path.GetTempPath(), $"public-{Guid.NewGuid():N}.ben");
            try
            {
                await using (var source = await _bundles.OpenBundleAsync(original, ct))
                await using (var target = new FileStream(scratch, FileMode.CreateNew, FileAccess.Write))
                {
                    await PublicSessionBundle.WriteAsync(
                        source, target, (double?)lat, (double?)lon,
                        includeRecordings: approved,
                        recordedByAccountId: session.RecordedByAppUserId ?? session.SubmittedByAppUserId,
                        sessionId: session.Id, sealedAtUtc: DateTime.UtcNow, ct);
                }
                await using var built = new FileStream(scratch, FileMode.Open, FileAccess.Read);
                await _fileStorage.WriteAsync(cached, built, ct);
            }
            finally
            {
                if (System.IO.File.Exists(scratch)) System.IO.File.Delete(scratch);
            }
        }

        var stream = await _fileStorage.OpenReadAsync(cached, ct);
        var name = SessionFileName.Build(session.StartedAt, session.PlaceName);
        return File(stream, BenBundle.ContentType, name, enableRangeProcessing: true);
    }

    /// <summary>
    /// One published session's heading and the files a visitor may be served — what the place
    /// page's player needs besides the timeline (10/14/2026).
    /// </summary>
    [HttpGet("{sessionId:guid}/summary")]
    public async Task<ActionResult<Ben.Service.Models.FieldSessions.PublicSessionSummary>> Summary(
        Guid sessionId, CancellationToken ct = default)
    {
        await using var db = await _db.CreateDbContextAsync(ct);
        var session = await Publishable(db)
            .Where(s => s.Id == sessionId)
            .Select(s => new
            {
                s.Id, PlaceId = s.PlaceId!.Value, PlaceName = s.Place!.Name, s.LocationLabel,
                s.StartedAt, s.EndedAt, s.DeviceModel,
                RecordedBy = s.RecordedByName ?? s.SubmittedByAppUser.DisplayName ?? "A contributor",
                EventTitle = (string?)null, EventOrg = (string?)null, EventUrlName = (string?)null, GroupName = (string?)null,
            })
            .FirstOrDefaultAsync(ct)
            // Or shown with a hosted event's results (event-scenario-gaps): the event's place stands in.
            ?? await EventSessionPublication.Shown(db)
                .Where(s => s.Id == sessionId)
                .Select(s => new
                {
                    s.Id,
                    PlaceId = db.HostedEvents.Where(h => h.Id == s.HostedEventId).Select(h => h.PlaceId).First(),
                    PlaceName = db.HostedEvents.Where(h => h.Id == s.HostedEventId).Select(h => h.Place.Name).First(),
                    s.LocationLabel, s.StartedAt, s.EndedAt, s.DeviceModel,
                    RecordedBy = s.RecordedByName ?? s.SubmittedByAppUser.DisplayName ?? "A contributor",
                    // Named and linked on the page (10/28/2026): it read "From the field archive" and
                    // pointed back to the place, though a visitor came from the event's results.
                    EventTitle = db.HostedEvents.Where(h => h.Id == s.HostedEventId).Select(h => (string?)h.Name).First(),
                    EventOrg = db.HostedEvents.Where(h => h.Id == s.HostedEventId).Select(h => (string?)h.Organization.UrlName).First(),
                    EventUrlName = db.HostedEvents.Where(h => h.Id == s.HostedEventId).Select(h => (string?)h.UrlName).First(),
                    GroupName = db.HostedEvents.Where(h => h.Id == s.HostedEventId).Select(h => (string?)h.Organization.Name).First(),
                })
                .FirstOrDefaultAsync(ct);
        if (session is null) return NotFound();

        // The same list the media route serves from, so the page and the door agree; empty while
        // the session's media is held for review.
        var files = (await ArchiveMediaPublication.ServableFilesAsync(db, sessionId, ct))
            .Select(m => new Ben.Service.Models.FieldSessions.PublicSessionFile(m.UploadFileId, m.RelativePath, m.ContentType))
            .ToList();

        return Ok(new Ben.Service.Models.FieldSessions.PublicSessionSummary(
            session.Id, session.PlaceId, session.PlaceName ?? "this place", session.LocationLabel,
            session.StartedAt, session.EndedAt, session.DeviceModel, session.RecordedBy, files,
            EventTitle: session.EventTitle,
            EventUrl: session.EventOrg is { } org && session.EventUrlName is { } slug ? $"/o/{org}/events/{slug}" : null,
            GroupName: session.GroupName));
    }

    /// <summary>
    /// A published session as a replay timeline: every position replaced by the place's public
    /// point, and recordings only once its media has been approved.
    /// </summary>
    /// <remarks>
    /// The positions go through <see cref="PublicSessionBundle.Sanitize"/> — the same sweep the
    /// archive's downloadable copy uses — before anything is read out of the document, so the
    /// timeline cannot carry a coordinate the bundle would not.
    /// </remarks>
    [HttpGet("{sessionId:guid}/timeline")]
    public async Task<IActionResult> Timeline(
        Guid sessionId, [FromServices] FieldSessionTimelineService timelines, CancellationToken ct = default)
    {
        await using var db = await _db.CreateDbContextAsync(ct);
        var session = await Publishable(db)
            .Include(s => s.Files).ThenInclude(f => f.UploadFile)
            .Include(s => s.DocumentUploadFile)
            .Include(s => s.Place)
            .FirstOrDefaultAsync(s => s.Id == sessionId, ct)
            // Or shown with a hosted event's results (event-scenario-gaps).
            ?? await EventSessionPublication.Shown(db)
                .Include(s => s.Files).ThenInclude(f => f.UploadFile)
                .Include(s => s.DocumentUploadFile)
                .FirstOrDefaultAsync(s => s.Id == sessionId, ct);
        if (session is null || session.DocumentUploadFile.StoragePath is not { Length: > 0 } path) return NotFound();

        // The point positions are swept to: the archive's place, or the hosted event's own.
        var place = session.Place
            ?? await db.HostedEvents.AsNoTracking().Where(h => h.Id == session.HostedEventId).Select(h => h.Place).FirstAsync(ct);
        var shownAt = session.PublishedAtUtc ?? session.ShownWithEventResultsAtUtc!.Value;

        var approved = session.MediaReviewState == FeedMediaReviewState.Approved;
        // By the id the archive's media route takes: the upload's own, or the row's for a
        // recording inside the session's .ben.
        var files = approved
            ? session.Files
                .Where(f => f.UploadFileId is not null || f.BundleEntryPath is { Length: > 0 })
                .Select(f => new TimelineSourceFile(
                    f.UploadFileId ?? f.Id, f.RelativePath, f.UploadFile?.StoragePath, f.BundleEntryPath,
                    f.DateUpdated ?? f.DateCreated))
                .ToList()
            : [];

        var (lat, lon) = PublicCoordinates.Approximate(place.Latitude, place.Longitude);
        Func<string, string?> redact = document =>
        {
            try
            {
                return System.Text.Encoding.UTF8.GetString(PublicSessionBundle.Sanitize(
                    System.Text.Encoding.UTF8.GetBytes(document), (double?)lat, (double?)lon));
            }
            catch (System.Text.Json.JsonException)
            {
                return null;   // not refused on a hope: a document that cannot be swept is not shown
            }
        };

        var payload = await timelines.GetAsync(new TimelineRequest(
            session.Id, session.IsBundle, path, session.DocumentUploadFileId,
            session.DocumentUploadFile.DateUpdated ?? session.DocumentUploadFile.DateCreated,
            files, Ben.Service.Models.FieldSessions.TimelinePositionPolicy.PublicPoint, redact,
            Variant: $"public:{shownAt.Ticks}:{(int)session.MediaReviewState}:{lat}:{lon}",
            Arrived: session.Files.Select(f => f.RelativePath).ToList()), ct);

        return payload is null ? NotFound() : FieldSessionTimelineService.Respond(this, payload);
    }

    /// <summary>
    /// The sessions shown with a hosted event's results, for its page (event-scenario-gaps). Anonymous; an
    /// event whose sessions aren't shown answers an empty list, whatever the reason.
    /// </summary>
    [HttpGet("for-hosted-event/{hostedEventId:guid}")]
    public async Task<ActionResult<IReadOnlyList<Ben.Service.Models.FieldSessions.PublicEventSessionRow>>> ForHostedEvent(
        Guid hostedEventId, CancellationToken ct = default)
    {
        await using var db = await _db.CreateDbContextAsync(ct);
        var placeId = await db.HostedEvents.AsNoTracking().Where(h => h.Id == hostedEventId).Select(h => (Guid?)h.PlaceId).FirstOrDefaultAsync(ct);
        if (placeId is null) return NotFound();

        var rows = await EventSessionPublication.Shown(db)
            .Where(s => s.HostedEventId == hostedEventId)
            .OrderBy(s => s.StartedAt)
            .Select(s => new Ben.Service.Models.FieldSessions.PublicEventSessionRow(
                s.Id, placeId.Value,
                s.RecordedByName ?? s.SubmittedByAppUser.DisplayName ?? "A contributor",
                s.LocationLabel, s.StartedAt, s.EndedAt, s.MarkerCount,
                s.MediaReviewState == FeedMediaReviewState.Approved))
            .ToListAsync(ct);
        return Ok(rows);
    }

    // ── What was found in it (10/28/2026) ─────────────────────────────────────

    /// <summary>
    /// The EVPs and certified clips a public session's page shows: the sender's own and those of
    /// whoever shows it, once its media is approved (<see cref="SessionFindingsReader.ForPublicAsync"/>).
    /// </summary>
    [HttpGet("{sessionId:guid}/findings")]
    public async Task<ActionResult<Ben.Service.Models.FieldSessions.SessionFindings>> Findings(Guid sessionId, CancellationToken ct = default)
    {
        await using var db = await _db.CreateDbContextAsync(ct);
        return await PublicSessionAsync(db, sessionId, ct) is { } session
            ? Ok(await SessionFindingsReader.ForPublicAsync(db, session, ct))
            : NotFound();
    }

    /// <summary>A clip the page shows, by byte range like the session's own recordings.</summary>
    [HttpGet("{sessionId:guid}/findings/{fileId:guid}/media")]
    public async Task<IActionResult> FindingMedia(Guid sessionId, Guid fileId, CancellationToken ct = default)
    {
        await using var db = await _db.CreateDbContextAsync(ct);
        if (await PublicSessionAsync(db, sessionId, ct) is not { } session
            || !await SessionFindingsReader.IsPublicClipAsync(db, session, fileId, ct)) return NotFound();
        var file = await db.UploadFiles.AsNoTracking().Where(f => f.Id == fileId)
            .Select(f => new { f.StoragePath, f.ContentType, f.FileName }).FirstOrDefaultAsync(ct);
        if (file?.StoragePath is not { Length: > 0 } path || !_fileStorage.Exists(path)) return NotFound();
        Response.Headers.CacheControl = "public, max-age=3600";
        return File(await _fileStorage.OpenReadAsync(path, ct), file.ContentType ?? "application/octet-stream",
                    file.FileName, enableRangeProcessing: true);
    }

    /// <summary>The certificate of a clip the page shows.</summary>
    [HttpGet("{sessionId:guid}/findings/{fileId:guid}/certificate")]
    public async Task<ActionResult<Ben.Service.Models.FieldSessions.FileCertificateRecord>> FindingCertificate(
        Guid sessionId, Guid fileId, CancellationToken ct = default)
    {
        await using var db = await _db.CreateDbContextAsync(ct);
        if (await PublicSessionAsync(db, sessionId, ct) is not { } session
            || !await SessionFindingsReader.IsPublicClipAsync(db, session, fileId, ct)) return NotFound();
        return await Services.Files.FileProvenanceService.DescribeAsync(db, fileId, ct) is { } certificate
            ? Ok(certificate) : NotFound();
    }

    /// <summary>Checks a shown clip's bytes now, and its certificate against the site's seal.</summary>
    [HttpPost("{sessionId:guid}/findings/{fileId:guid}/certificate/check")]
    [Microsoft.AspNetCore.RateLimiting.EnableRateLimiting(Services.RateLimiting.AudioProcessingPolicy)]
    public async Task<ActionResult<Ben.Service.Models.FieldSessions.FileCertificateCheck>> FindingCertificateCheck(
        Guid sessionId, Guid fileId, [FromServices] Services.Files.FileProvenanceService provenance, CancellationToken ct = default)
    {
        await using var db = await _db.CreateDbContextAsync(ct);
        if (await PublicSessionAsync(db, sessionId, ct) is not { } session
            || !await SessionFindingsReader.IsPublicClipAsync(db, session, fileId, ct)) return NotFound();
        return await provenance.CheckAsync(db, fileId, ct) is { } check ? Ok(check) : NotFound();
    }

    /// <summary>A session a visitor may see: in a place's archive, or shown with an event's results.</summary>
    private static async Task<Ben.Data.Source.Entities.FieldSessionUpload?> PublicSessionAsync(
        BenDataContext db, Guid sessionId, CancellationToken ct)
        => await Publishable(db).FirstOrDefaultAsync(s => s.Id == sessionId, ct)
           ?? await EventSessionPublication.Shown(db).FirstOrDefaultAsync(s => s.Id == sessionId, ct);

    /// <summary>The archive's own gate, as a query.</summary>
    private static IQueryable<Ben.Data.Source.Entities.FieldSessionUpload> Publishable(BenDataContext db)
        => db.FieldSessionUploads.AsNoTracking()
            .Where(s => s.PublishedAtUtc != null
                     && s.PlaceId != null
                     && s.Place!.Kind == PlaceKind.PublicLocation);

    private async Task<List<PublicArchiveSessionRow>> PublishedAsync(CancellationToken ct)
    {
        await using var db = await _db.CreateDbContextAsync(ct);
        var raw = await Publishable(db)
            .Select(s => new
            {
                s.Id, s.PlaceId, PlaceName = s.Place!.Name, s.Place.City, s.Place.State,
                s.Place.Latitude, s.Place.Longitude,
                RecordedBy = s.RecordedByName ?? s.SubmittedByAppUser.DisplayName ?? "A contributor",
                s.LocationLabel, s.StartedAt, s.EndedAt, s.ReadingCount, s.MarkerCount,
                MediaCount = s.MediaReviewState == FeedMediaReviewState.Approved
                    ? s.Files.Count(f => f.UploadFileId != null || f.BundleEntryPath != null) : 0,
                s.IsBundle,
            })
            .ToListAsync(ct);

        return raw.Select(r =>
        {
            var (lat, lon) = PublicCoordinates.Approximate(r.Latitude, r.Longitude);
            return new PublicArchiveSessionRow(
                r.Id, r.PlaceId!.Value, r.PlaceName, r.City, r.State,
                (double?)lat, (double?)lon, r.RecordedBy, r.LocationLabel,
                r.StartedAt, r.EndedAt, r.ReadingCount, r.MarkerCount, r.MediaCount,
                CanDownload: r.IsBundle, DistanceMiles: null);
        }).ToList();
    }

    private static double Miles(double lat1, double lon1, double lat2, double lon2)
    {
        const double earthMiles = 3958.8;
        var dLat = (lat2 - lat1) * Math.PI / 180;
        var dLon = (lon2 - lon1) * Math.PI / 180;
        var a = Math.Sin(dLat / 2) * Math.Sin(dLat / 2)
              + Math.Cos(lat1 * Math.PI / 180) * Math.Cos(lat2 * Math.PI / 180)
              * Math.Sin(dLon / 2) * Math.Sin(dLon / 2);
        return earthMiles * 2 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1 - a));
    }
}

/// <summary>One published session, as a lookup lists it.</summary>
/// <param name="PublicLatitude">The place's PUBLIC point — approximate by design.</param>
/// <param name="MediaCount">Recordings a visitor may have: zero while the session's media is held.</param>
/// <param name="CanDownload">Whether there is a public <c>.ben</c> to hand over. Sessions sent before
/// session files existed have none; they are still on the place's page.</param>
/// <param name="DistanceMiles">From where the lookup was asked, to the public point. Null for a search.</param>
public sealed record PublicArchiveSessionRow(
    Guid Id,
    Guid PlaceId,
    string? PlaceName,
    string? PlaceCity,
    string? PlaceState,
    double? PublicLatitude,
    double? PublicLongitude,
    string RecordedBy,
    string? LocationLabel,
    DateTime StartedAt,
    DateTime? EndedAt,
    int ReadingCount,
    int MarkerCount,
    int MediaCount,
    bool CanDownload,
    double? DistanceMiles);
