using System.Globalization;
using System.Text;
using AutoMapper;
using Ben.Data.Common.Enums;
using Ben.Data.Common.Interfaces;
using Ben.Data.Source.Context;
using Ben.Data.Source.Entities;
using Ben.Data.WebApi.Controllers.Cms;
using Ben.Data.WebApi.Services;
using Ben.Data.WebApi.Services.Events;
using Ben.Service.Models.Entities;
using Ben.Service.RepositoryService.GenericInterfaces;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Ben.Data.WebApi.Controllers.Entities;

/// <summary>
/// What an organizer does with an event once it has happened: start the next one from it, and take
/// the list of who came away with them (item 235 phase 12).
/// </summary>
[Route("api/organizations/{orgId:guid}/events/{eventId:guid}")]
public sealed class HostedEventAfterController : OrgCmsControllerBase
{
    /// <summary>The event-files type, as the files door uses.</summary>
    private static readonly Guid FileTypeId = new("20000000-0000-0000-0000-000000000001");

    private readonly Services.Access.HostedEventAccess _access;
    private readonly HostedEventCalendarSync _sync;
    private readonly IMediaIngestService _ingest;
    private readonly IFileStorageService _storage;
    private readonly ILogger<HostedEventAfterController> _logger;

    public HostedEventAfterController(
        IDbContextFactory<BenDataContext> dbFactory, IMapper mapper, IOrganizationSecurityService security,
        Services.Access.HostedEventAccess access, HostedEventCalendarSync sync,
        IMediaIngestService ingest, IFileStorageService storage, ILogger<HostedEventAfterController> logger)
        : base(dbFactory, mapper, security)
    {
        _access = access;
        _sync = sync;
        _ingest = ingest;
        _storage = storage;
        _logger = logger;
    }

    // ── copying ──────────────────────────────────────────────────────────────

    /// <summary>What a copy would bring, so the page can offer each part with its size.</summary>
    [HttpGet("copy")]
    public async Task<ActionResult<HostedEventCopyPreviewRecord>> Preview(Guid orgId, Guid eventId, CancellationToken ct)
    {
        var userId = GetCurrentUserId();
        if (userId is null) return Unauthorized();
        if (!await _access.CanEditEventAsync(userId.Value, orgId, ct)) return Forbid();

        await using var db = await DbFactory.CreateDbContextAsync(ct);
        var ev = await db.HostedEvents.AsNoTracking().Include(e => e.Nights)
            .FirstOrDefaultAsync(e => e.Id == eventId && e.OrganizationId == orgId, ct);
        if (ev is null) return NotFound();

        var files = await db.HostedEventFiles.AsNoTracking()
            .Where(f => f.HostedEventId == eventId)
            .Select(f => f.UploadFile.FileSize)
            .ToListAsync(ct);

        return Ok(new HostedEventCopyPreviewRecord(
            ev.Id, ev.Name, ev.StartsOn, ev.EndsOn, ev.Nights.Count, ev.LayoutKind,
            await db.HostedEventLayoutUnits.CountAsync(u => u.HostedEventId == eventId, ct),
            await db.HostedEventMenus.CountAsync(m => m.HostedEventNight.HostedEventId == eventId, ct),
            await db.HostedEventSessions.CountAsync(s => s.HostedEventId == eventId && s.CalledOffUtc == null, ct),
            await db.HostedEventBands.CountAsync(b => b.HostedEventId == eventId, ct),
            await db.HostedEventStaff.CountAsync(s => s.HostedEventId == eventId && s.AppUserId != null, ct),
            await db.HostedEventStaff.CountAsync(s => s.HostedEventId == eventId && s.AppUserId == null, ct),
            await db.OrganizationAds.CountAsync(a => a.HostedEventId == eventId, ct),
            files.Count,
            files.Sum(),
            VenueSentence(ev)));
    }

    /// <summary>Starts a new draft from this event.</summary>
    [HttpPost("copy")]
    public async Task<ActionResult<HostedEventCopyResultRecord>> Copy(
        Guid orgId, Guid eventId, [FromBody] CopyHostedEventRequest request, CancellationToken ct)
    {
        var userId = GetCurrentUserId();
        if (userId is null) return Unauthorized();
        if (!await _access.CanEditEventAsync(userId.Value, orgId, ct)) return Forbid();

        var name = request.Name?.Trim();
        if (string.IsNullOrWhiteSpace(name)) return BadRequest("Give the new event a name.");
        if (name.Length > 160) return BadRequest("That name is too long — 160 characters at most.");

        await using var db = await DbFactory.CreateDbContextAsync(ct);
        if (await db.HostedEvents.AnyAsync(e => e.OrganizationId == orgId && e.Name == name, ct))
            return BadRequest($"You already have an event called {name}. "
                            + "Two events are told apart by their names, so give this one a name of its own.");
        var source = await db.HostedEvents.AsNoTracking().Include(e => e.Nights)
            .FirstOrDefaultAsync(e => e.Id == eventId && e.OrganizationId == orgId, ct);
        if (source is null) return NotFound();

        var zone = HostedEventCalendarSync.ZoneOf(source.TimeZoneId);
        var today = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, zone).Date;
        if (request.StartsOn.Date < today)
            return BadRequest("The new event can't start in the past. Choose its first date.");

        var now = DateTime.UtcNow;
        var (copy, brought) = await HostedEventCopy.CopyAsync(db, source, request, userId.Value, now, ct);

        await _sync.SyncAsync(db, copy, userId.Value, ct);
        await db.SaveChangesAsync(ct);

        var (files, filesSentence) = request.Files
            ? await CopyFilesAsync(db, source, copy, userId.Value, ct)
            : (0, null);

        return Ok(new HostedEventCopyResultRecord(
            copy.Id, copy.Name, brought.Nights, brought.Units, brought.Blocks, brought.Menus,
            brought.Sessions, brought.Bands, brought.Helpers, brought.Adverts, files, filesSentence));
    }

    /// <summary>
    /// Brings the files across as files of their own, through the ingest like any upload.
    /// </summary>
    /// <remarks>
    /// <b>Copies, not shared rows.</b> A file belongs to one event — that is what lets the old event's
    /// files be tidied away later without taking the new event's guest pack with them. It reads the
    /// already-cleaned copy, so nothing that was stripped comes back, and it counts against the new
    /// event's allowance.
    /// </remarks>
    private async Task<(int Copied, string? Sentence)> CopyFilesAsync(
        BenDataContext db, HostedEvent source, HostedEvent copy, Guid actorId, CancellationToken ct)
    {
        var files = await db.HostedEventFiles.AsNoTracking()
            .Include(f => f.UploadFile)
            .Where(f => f.HostedEventId == source.Id)
            .OrderBy(f => f.SortOrder)
            .ToListAsync(ct);
        if (files.Count == 0) return (0, null);

        var total = files.Sum(f => f.UploadFile.FileSize);
        if (await EventStorage.WhyItDoesNotFitAsync(db, copy.Id, total, ct) is { } full)
            return (0, "The files weren't brought across: " + full);

        var copied = 0;
        var missing = 0;

        foreach (var file in files)
        {
            var served = _ingest.ServingPathFor(file.UploadFile.StoragePath ?? "");
            if (string.IsNullOrEmpty(file.UploadFile.StoragePath) || !_storage.Exists(served))
            {
                missing++;
                continue;
            }

            var uploadId = Guid.NewGuid();
            var extension = Path.GetExtension(file.UploadFile.FileName).ToLowerInvariant();
            var storedName = $"{uploadId:N}{extension}";
            var storagePath = _storage.OrgFilePath(copy.OrganizationId, $"events/{copy.Id:N}/{storedName}");

            try
            {
                await using var stream = await _storage.OpenReadAsync(served, ct);
                var form = new FormFile(stream, 0, file.UploadFile.FileSize, "file", file.UploadFile.FileName)
                {
                    Headers = new HeaderDictionary(),
                    ContentType = file.UploadFile.ContentType,
                };

                var ingested = await _ingest.IngestAsync(form, storagePath, uploadId, ct);
                var now = DateTime.UtcNow;

                db.UploadFiles.Add(new UploadFile
                {
                    Id = uploadId, UploadFileTypeId = FileTypeId, OwnerOrganizationId = copy.OrganizationId,
                    FileName = file.UploadFile.FileName, StoredFileName = storedName,
                    ContentType = ingested.ServedContentType, FileSize = ingested.ServedFileSize,
                    StoragePath = storagePath, IsPublic = false,
                    DateCreated = now, CreatedByAppUserId = actorId,
                });
                db.UploadFileMetadata.Add(ingested.Metadata);
                db.HostedEventFiles.Add(new HostedEventFile
                {
                    Id = Guid.NewGuid(), HostedEventId = copy.Id, UploadFileId = uploadId,
                    Folder = file.Folder, Description = file.Description, Audience = file.Audience,
                    SortOrder = file.SortOrder, DateCreated = now, CreatedByAppUserId = actorId,
                });
                await db.SaveChangesAsync(ct);
                copied++;
            }
            catch (Exception e) when (e is not OperationCanceledException)
            {
                _logger.LogWarning(e, "Could not copy event file {FileId} to {EventId}.", file.Id, copy.Id);
                db.ChangeTracker.Clear();
                await _ingest.DeleteAllAsync(storagePath, CancellationToken.None);
                missing++;
            }
        }

        return (copied, missing == 0 ? null
            : $"{missing} {(missing == 1 ? "file" : "files")} couldn't be brought across. Add "
              + $"{(missing == 1 ? "it" : "them")} again from the new event's files page.");
    }

    private static string VenueSentence(HostedEvent ev) => ev.VenueArrangement switch
    {
        HostedEventVenueArrangement.External =>
            "The venue's contact comes across; record when they agreed to the new dates before publishing.",
        HostedEventVenueArrangement.PlatformGrant =>
            "The venue's yes was for these dates only. Ask them again for the new ones before publishing.",
        _ => "The venue is yours, so nothing needs agreeing again.",
    };

    // ── the thank-you and the reviews ────────────────────────────────────────

    /// <summary>The thank-you, whether reviews are taken, and every review including hidden ones.</summary>
    [HttpGet("after")]
    public async Task<ActionResult<HostedEventAfterRecord>> GetAfter(Guid orgId, Guid eventId, CancellationToken ct)
    {
        var userId = GetCurrentUserId();
        if (userId is null) return Unauthorized();
        if (!await _access.CanEditEventAsync(userId.Value, orgId, ct)) return Forbid();

        await using var db = await DbFactory.CreateDbContextAsync(ct);
        var ev = await db.HostedEvents.AsNoTracking()
            .FirstOrDefaultAsync(e => e.Id == eventId && e.OrganizationId == orgId, ct);
        if (ev is null) return NotFound();

        return Ok(await AfterAsync(db, ev, ct));
    }

    [HttpPut("after")]
    public async Task<ActionResult<HostedEventAfterRecord>> SetAfter(
        Guid orgId, Guid eventId, [FromBody] SetHostedEventAfterRequest request, CancellationToken ct)
    {
        var userId = GetCurrentUserId();
        if (userId is null) return Unauthorized();
        if (!await _access.CanEditEventAsync(userId.Value, orgId, ct)) return Forbid();

        var note = string.IsNullOrWhiteSpace(request.ThankYouNote) ? null : request.ThankYouNote.Trim();
        if (note is { Length: > 2000 }) return BadRequest("The note is longer than 2,000 characters.");

        await using var db = await DbFactory.CreateDbContextAsync(ct);
        var ev = await db.HostedEvents.FirstOrDefaultAsync(e => e.Id == eventId && e.OrganizationId == orgId, ct);
        if (ev is null) return NotFound();

        ev.AllowReviews = request.AllowReviews;
        ev.SendThankYou = request.SendThankYou;
        ev.ThankYouNote = note;
        ev.DateUpdated = DateTime.UtcNow;
        ev.UpdatedByAppUserId = userId;
        await db.SaveChangesAsync(ct);

        return Ok(await AfterAsync(db, ev, ct));
    }

    /// <summary>
    /// Hides or restores a review. Hide, never edit: the words stay where the person who wrote them can
    /// see them.
    /// </summary>
    [HttpPost("reviews/{reviewId:guid}/{action}")]
    public async Task<ActionResult<HostedEventAfterRecord>> SetReviewHidden(
        Guid orgId, Guid eventId, Guid reviewId, string action, CancellationToken ct)
    {
        if (action is not ("hide" or "show")) return NotFound();

        var userId = GetCurrentUserId();
        if (userId is null) return Unauthorized();
        if (!await _access.CanEditEventAsync(userId.Value, orgId, ct)) return Forbid();

        await using var db = await DbFactory.CreateDbContextAsync(ct);
        var review = await db.HostedEventReviews.Include(r => r.HostedEvent)
            .FirstOrDefaultAsync(r => r.Id == reviewId && r.HostedEventId == eventId
                                   && r.HostedEvent.OrganizationId == orgId, ct);
        if (review is null) return NotFound();

        var hiding = action == "hide";
        review.HiddenAtUtc = hiding ? DateTime.UtcNow : null;
        review.HiddenByAppUserId = hiding ? userId : null;
        review.DateUpdated = DateTime.UtcNow;
        review.UpdatedByAppUserId = userId;
        await db.SaveChangesAsync(ct);

        return Ok(await AfterAsync(db, review.HostedEvent, ct));
    }

    private static async Task<HostedEventAfterRecord> AfterAsync(BenDataContext db, HostedEvent ev, CancellationToken ct)
    {
        var reviews = await db.HostedEventReviews.AsNoTracking()
            .Where(r => r.HostedEventId == ev.Id)
            .OrderByDescending(r => r.DateCreated)
            .Select(r => new TourReviewRecord(
                r.Id, r.AppUser.DisplayName ?? r.AppUser.UserName ?? "A guest", r.AppUser.Handle,
                r.Stars, r.Comment, r.DateCreated, false, r.HiddenAtUtc != null))
            .ToListAsync(ct);
        var (average, count) = await HostedEventReviews.RatingAsync(db, ev.Id, ct);

        return new HostedEventAfterRecord(
            ev.Id, ev.Name, ev.LifecycleState, ev.AllowReviews, ev.SendThankYou, ev.ThankYouNote, ev.ThankYouSentUtc,
            await db.HostedEventBookings.CountAsync(b => b.HostedEventId == ev.Id && b.ThankedUtc != null, ct),
            await db.HostedEventBookings.CountAsync(b => b.HostedEventId == ev.Id && b.Status == HostedEventBookingStatus.Confirmed, ct),
            await db.HostedEventGalleryImages.AnyAsync(g => g.HostedEventId == ev.Id, ct),
            average, count, reviews);
    }

    // ── what the venue remembers ─────────────────────────────────────────────

    /// <summary>
    /// Plans used at this event's venue before, most useful first: the venue's own group, then this group's,
    /// then anybody's published events there.
    /// </summary>
    /// <remarks>
    /// Ben, 2026-09-13: <i>"we should remember the venue from now on and use the room or seating as a starting
    /// point next time someone reserves the venue."</i> Remembered by reading what is already there rather than
    /// storing a second copy: the plan an event used is the record. Another group's draft is never offered, and
    /// another group's prices and notes never come across — the rows and seats of a building are a fact about
    /// the building; what somebody charged for them is not.
    /// </remarks>
    [HttpGet("layout/earlier")]
    public async Task<ActionResult<IReadOnlyList<EarlierPlanRecord>>> EarlierPlans(Guid orgId, Guid eventId, CancellationToken ct)
    {
        var userId = GetCurrentUserId();
        if (userId is null) return Unauthorized();
        if (!await _access.CanReadEventAsync(userId.Value, orgId, ct)) return Forbid();

        await using var db = await DbFactory.CreateDbContextAsync(ct);
        var ev = await db.HostedEvents.AsNoTracking().FirstOrDefaultAsync(e => e.Id == eventId && e.OrganizationId == orgId, ct);
        if (ev is null) return NotFound();

        return Ok(await EarlierAsync(db, ev, ct));
    }

    /// <summary>One earlier plan's units, to load into the designer unsaved.</summary>
    [HttpGet("layout/earlier/{sourceId:guid}")]
    public async Task<ActionResult<EarlierPlanUnitsRecord>> EarlierPlanUnits(Guid orgId, Guid eventId, Guid sourceId, CancellationToken ct)
    {
        var userId = GetCurrentUserId();
        if (userId is null) return Unauthorized();
        if (!await _access.CanReadEventAsync(userId.Value, orgId, ct)) return Forbid();

        await using var db = await DbFactory.CreateDbContextAsync(ct);
        var ev = await db.HostedEvents.AsNoTracking().FirstOrDefaultAsync(e => e.Id == eventId && e.OrganizationId == orgId, ct);
        if (ev is null) return NotFound();

        // Only one of the plans this event would be offered — never an arbitrary event by id.
        var offered = await EarlierAsync(db, ev, ct);
        if (offered.FirstOrDefault(p => p.HostedEventId == sourceId) is not { } plan) return NotFound();

        var ours = plan.Ours;
        var roomOwners = new List<Guid> { orgId };
        if (await Services.Venues.VenueGrants.RoomsLentToAsync(db, ev, ct) is Guid venueOrgId) roomOwners.Add(venueOrgId);

        var units = await db.HostedEventLayoutUnits.AsNoTracking()
            .Include(u => u.PlaceRoom)
            .Where(u => u.HostedEventId == sourceId)
            .OrderBy(u => u.SortOrder)
            .ToListAsync(ct);

        var kept = units.Where(u => u.PlaceRoomId is null || roomOwners.Contains(u.PlaceRoom!.OrganizationId)).ToList();

        return Ok(new EarlierPlanUnitsRecord(
            plan.Kind,
            [.. kept.Select(u => new HostedEventLayoutUnitRecord(
                Guid.Empty, u.PlaceRoomId, EventCapacity.NameOf(u), u.Section, u.PlaceRoom?.Floor, u.PlaceRoom?.BedNote,
                EventCapacity.CapacityOf(u), u.Capacity,
                ours ? u.Price : null, ours ? u.Note : null,
                u.LayoutRow, u.LayoutColumn, u.SortOrder))],
            units.Count - kept.Count));
    }

    internal static async Task<IReadOnlyList<EarlierPlanRecord>> EarlierAsync(BenDataContext db, HostedEvent ev, CancellationToken ct)
    {
        var venueOrg = await db.OrganizationVenueProfiles.AsNoTracking()
            .Where(p => p.PlaceId == ev.PlaceId && p.VerifiedUtc != null)
            .Select(p => (Guid?)p.OrganizationId)
            .FirstOrDefaultAsync(ct);

        var rows = await db.HostedEvents.AsNoTracking()
            .Where(e => e.PlaceId == ev.PlaceId && e.Id != ev.Id
                     && db.HostedEventLayoutUnits.Any(u => u.HostedEventId == e.Id)
                     && (e.OrganizationId == ev.OrganizationId
                      || HostedEventStates.OnThePublicSite.Contains(e.LifecycleState)
                      || e.LifecycleState == HostedEventLifecycleState.Archived))
            .Select(e => new
            {
                e.Id, e.Name, OrgName = e.Organization.Name, e.OrganizationId, e.StartsOn, e.LayoutKind,
                Units = db.HostedEventLayoutUnits.Count(u => u.HostedEventId == e.Id),
            })
            .ToListAsync(ct);

        return [.. rows
            .Select(r => new EarlierPlanRecord(r.Id, r.Name, r.OrgName, r.StartsOn, r.LayoutKind, r.Units,
                FromTheVenue: venueOrg == r.OrganizationId, Ours: r.OrganizationId == ev.OrganizationId))
            .OrderByDescending(r => r.FromTheVenue)
            .ThenByDescending(r => r.Ours)
            // A plan that has been used — the event has happened — before one only drafted for the future.
            .ThenByDescending(r => r.StartsOn <= DateTime.UtcNow.Date)
            .ThenByDescending(r => r.StartsOn)
            .Take(5)];
    }

    // ── pick and zip ─────────────────────────────────────────────────────────

    /// <summary>The most things one zip may carry, so the address that asks for them stays a reasonable length.</summary>
    internal const int MaxInOneZip = 100;

    /// <summary>
    /// Everything an organizer may take away: the event's files, its gallery, and the room's photos that are
    /// theirs to take — posted by their own people, or sent to them by a guest.
    /// </summary>
    /// <remarks>
    /// Ben, 2026-09-13: <i>"allow the organizer the ability to pick from a list to download and just zip up
    /// whatever they pick into a .zip file for them."</i> A guest's photo that was never sent to the organizers
    /// is not on the list: it is on the wall for the evening, and it is the guest's.
    /// </remarks>
    [HttpGet("keep")]
    public async Task<ActionResult<HostedEventKeepRecord>> Keep(Guid orgId, Guid eventId, CancellationToken ct)
    {
        var userId = GetCurrentUserId();
        if (userId is null) return Unauthorized();

        await using var db = await DbFactory.CreateDbContextAsync(ct);
        if (!await _access.CanManageFilesAsync(userId.Value, orgId, eventId, db, ct)) return Forbid();
        var ev = await db.HostedEvents.AsNoTracking().FirstOrDefaultAsync(e => e.Id == eventId && e.OrganizationId == orgId, ct);
        if (ev is null) return NotFound();

        var days = await EventRetention.DaysAsync(db, ct);
        DateTime? clearsOn = EventRetention.Applies.Contains(ev.LifecycleState) ? EventRetention.ClearsOn(ev, days) : null;

        return Ok(new HostedEventKeepRecord(ev.Id, ev.Name, clearsOn, ev.MediaClearedUtc, await KeepItemsAsync(db, ev, ct)));
    }

    /// <summary>The chosen things as one zip, in a folder each for files, gallery and room photos.</summary>
    [HttpGet("keep.zip")]
    public async Task<IActionResult> KeepZip(Guid orgId, Guid eventId, [FromQuery] string? ids, CancellationToken ct)
    {
        var userId = GetCurrentUserId();
        if (userId is null) return Unauthorized();

        await using var db = await DbFactory.CreateDbContextAsync(ct);
        if (!await _access.CanManageFilesAsync(userId.Value, orgId, eventId, db, ct)) return Forbid();
        var ev = await db.HostedEvents.AsNoTracking().FirstOrDefaultAsync(e => e.Id == eventId && e.OrganizationId == orgId, ct);
        if (ev is null) return NotFound();

        var wanted = (ids ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(i => Guid.TryParse(i, out var g) ? g : Guid.Empty)
            .Where(g => g != Guid.Empty)
            .Distinct()
            .ToList();
        if (wanted.Count == 0) return BadRequest("Pick at least one thing to download.");
        if (wanted.Count > MaxInOneZip) return BadRequest($"Pick up to {MaxInOneZip} at a time.");

        // Only what this event offers this organizer; anything else in the list is ignored, not fetched.
        var items = (await KeepItemsAsync(db, ev, ct)).Where(i => wanted.Contains(i.UploadFileId)).ToList();
        if (items.Count == 0) return NotFound();

        var paths = await db.UploadFiles.AsNoTracking()
            .Where(f => items.Select(i => i.UploadFileId).Contains(f.Id))
            .Select(f => new { f.Id, f.StoragePath })
            .ToDictionaryAsync(f => f.Id, f => f.StoragePath, ct);

        // Written to a temporary file and streamed from there: a zip writes synchronously as it closes,
        // which the server refuses on a response body, and a file on disk never holds 2 GB in memory.
        var temp = Path.Combine(Path.GetTempPath(), $"event-keep-{Guid.NewGuid():N}.zip");
        var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        await using (var output = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, useAsync: true))
        using (var zip = new System.IO.Compression.ZipArchive(output, System.IO.Compression.ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var item in items)
            {
                if (!paths.TryGetValue(item.UploadFileId, out var stored) || string.IsNullOrEmpty(stored)) continue;
                var served = _ingest.ServingPathFor(stored);
                if (!_storage.Exists(served)) continue;

                var folder = item.Kind switch { "file" => "Files", "picture" => "Gallery", _ => "Room photos" };
                var entry = zip.CreateEntry(UniqueName(used, folder, item.Name), System.IO.Compression.CompressionLevel.Fastest);
                await using var into = entry.Open();
                await using var from = await _storage.OpenReadAsync(served, ct);
                await from.CopyToAsync(into, ct);
            }
        }

        var stream = new FileStream(temp, FileMode.Open, FileAccess.Read, FileShare.Read, 81920,
            FileOptions.Asynchronous | FileOptions.DeleteOnClose);
        return File(stream, "application/zip", $"{(UrlSlug.From(ev.Name) ?? "event")}.zip");
    }

    private static string UniqueName(HashSet<string> used, string folder, string name)
    {
        var safe = string.Concat((name.Length > 0 ? name : "file").Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c));
        var candidate = $"{folder}/{safe}";
        var stem = Path.GetFileNameWithoutExtension(safe);
        var extension = Path.GetExtension(safe);
        for (var n = 2; !used.Add(candidate); n++)
            candidate = $"{folder}/{stem} ({n}){extension}";
        return candidate;
    }

    internal static async Task<IReadOnlyList<HostedEventKeepItemRecord>> KeepItemsAsync(
        BenDataContext db, HostedEvent ev, CancellationToken ct)
    {
        var files = await db.HostedEventFiles.AsNoTracking()
            .Where(f => f.HostedEventId == ev.Id)
            .OrderBy(f => f.SortOrder)
            .Select(f => new HostedEventKeepItemRecord(f.UploadFileId, "file", f.UploadFile.FileName, f.UploadFile.FileSize,
                f.CreatedByAppUser.DisplayName, f.DateCreated))
            .ToListAsync(ct);

        var pictures = await db.HostedEventGalleryImages.AsNoTracking()
            .Where(g => g.HostedEventId == ev.Id)
            .OrderBy(g => g.SortOrder)
            .Select(g => new HostedEventKeepItemRecord(g.UploadFileId, "picture", g.UploadFile.FileName, g.UploadFile.FileSize,
                null, g.DateCreated))
            .ToListAsync(ct);

        var photos = await db.OrgMessages.AsNoTracking()
            .Where(m => m.HostedEventId == ev.Id && m.MediaUploadFileId != null && m.HiddenUtc == null
                     && (db.OrganizationUserMemberships.Any(u => u.OrganizationId == ev.OrganizationId && u.AppUserId == m.AuthorAppUserId && u.IsActive)
                      || db.UploadFileOrganizationShares.Any(s => s.UploadFileId == m.MediaUploadFileId && s.OrganizationId == ev.OrganizationId && s.IsActive)))
            .OrderBy(m => m.DateCreated)
            .Join(db.UploadFiles, m => m.MediaUploadFileId, f => (Guid?)f.Id, (m, f) => new HostedEventKeepItemRecord(
                f.Id, "photo", f.FileName, f.FileSize, m.AuthorAppUser.DisplayName, m.DateCreated))
            .ToListAsync(ct);

        return [.. files, .. pictures, .. photos];
    }

    // ── the list of who came ─────────────────────────────────────────────────

    /// <summary>
    /// Every booking as a spreadsheet: who, how to reach them, how many, which nights, and whether they
    /// came.
    /// </summary>
    /// <remarks>
    /// <para><b>The same eyes as the board.</b> Whoever may read bookings may take them away; dietary notes
    /// only go to the people the board shows them to.</para>
    ///
    /// <para><b>A cell that starts like a formula is written as text</b> (a leading apostrophe), because a
    /// guest's note that begins with "=" would otherwise run in the organizer's spreadsheet.</para>
    /// </remarks>
    [HttpGet("bookings/export.csv")]
    public async Task<IActionResult> ExportBookings(Guid orgId, Guid eventId, CancellationToken ct)
    {
        var userId = GetCurrentUserId();
        if (userId is null) return Unauthorized();

        await using var db = await DbFactory.CreateDbContextAsync(ct);
        var ev = await db.HostedEvents.AsNoTracking()
            .FirstOrDefaultAsync(e => e.Id == eventId && e.OrganizationId == orgId, ct);
        if (ev is null) return NotFound();
        if (!await _access.CanReadBookingsAsync(userId.Value, orgId, eventId, db, ct)) return Forbid();

        var bookings = await db.HostedEventBookings.AsNoTracking()
            .Include(b => b.LeadAppUser)
            .Include(b => b.Guests)
            .Include(b => b.Nights).ThenInclude(n => n.HostedEventNight)
            .Include(b => b.Nights).ThenInclude(n => n.HostedEventLayoutUnit).ThenInclude(u => u!.PlaceRoom)
            .Where(b => b.HostedEventId == eventId)
            .OrderBy(b => b.Status).ThenBy(b => b.DateCreated)
            .ToListAsync(ct);

        var arrivals = await db.HostedEventCheckIns.AsNoTracking()
            .Where(c => c.HostedEventBooking.HostedEventId == eventId)
            .Select(c => new { c.HostedEventBookingId, c.HostedEventNight.Date })
            .ToListAsync(ct);

        var csv = BookingsCsv.Write(bookings,
            arrivals.GroupBy(a => a.HostedEventBookingId)
                .ToDictionary(g => g.Key, g => (IReadOnlyList<DateTime>)[.. g.Select(a => a.Date).OrderBy(d => d)]),
            includeDietary: true);

        var name = $"{(UrlSlug.From(ev.Name) ?? "event")}-bookings.csv";
        return File(Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(csv)).ToArray(), "text/csv", name);
    }
}

/// <summary>The bookings spreadsheet, as text (item 235 phase 12).</summary>
public static class BookingsCsv
{
    public static readonly string[] Columns =
    [
        "Status", "First name", "Last name", "Name on the account", "Email", "Phone", "Party size", "Kind",
        "Nights and places", "Guests", "Dietary notes", "Note", "Asked on", "Decided on", "Arrived on",
    ];

    public static string Write(
        IReadOnlyList<HostedEventBooking> bookings,
        IReadOnlyDictionary<Guid, IReadOnlyList<DateTime>> arrivals,
        bool includeDietary)
    {
        var sb = new StringBuilder();
        sb.AppendLine(string.Join(",", Columns.Select(Cell)));

        foreach (var b in bookings)
        {
            var nights = string.Join("; ", b.Nights
                .Where(n => n.ReleasedUtc is null || b.Status is not (HostedEventBookingStatus.Held or HostedEventBookingStatus.Confirmed))
                .OrderBy(n => n.HostedEventNight.Date)
                .Select(n => $"{n.HostedEventNight.Date:MM/dd/yyyy} {EventCapacity.NameOf(n, b.Kind)}"));

            var guests = string.Join("; ", b.Guests.OrderBy(g => g.SortOrder).Select(g => g.DisplayName));
            var dietary = includeDietary
                ? string.Join("; ", b.Guests.Where(g => g.DietaryNotes is { Length: > 0 })
                    .OrderBy(g => g.SortOrder).Select(g => $"{g.DisplayName}: {g.DietaryNotes}"))
                : "";

            var came = arrivals.TryGetValue(b.Id, out var dates)
                ? string.Join("; ", dates.Select(d => d.ToString("MM/dd/yyyy", CultureInfo.InvariantCulture)))
                : "";

            string[] row =
            [
                b.Status.ToString(),
                b.LeadAppUser?.FirstName ?? "",
                b.LeadAppUser?.LastName ?? "",
                b.LeadAppUser?.DisplayName ?? "",
                b.LeadAppUser?.Email ?? "",
                b.ContactPhone ?? "",
                b.PartySize.ToString(CultureInfo.InvariantCulture),
                b.Kind == HostedEventBookingKind.DayPass ? "Day pass" : "Staying",
                nights,
                guests,
                dietary,
                b.Note ?? "",
                b.DateCreated.ToString("MM/dd/yyyy", CultureInfo.InvariantCulture),
                b.DecidedUtc?.ToString("MM/dd/yyyy", CultureInfo.InvariantCulture) ?? "",
                came,
            ];

            sb.AppendLine(string.Join(",", row.Select(Cell)));
        }

        return sb.ToString();
    }

    /// <summary>
    /// One cell: quoted when it must be, and never read as a formula by a spreadsheet.
    /// </summary>
    public static string Cell(string value)
    {
        if (value.Length > 0 && value[0] is '=' or '+' or '-' or '@' or '\t' or '\r')
            value = "'" + value;

        return value.IndexOfAny([',', '"', '\n', '\r']) >= 0
            ? "\"" + value.Replace("\"", "\"\"") + "\""
            : value;
    }
}
