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
                .Select(n => $"{n.HostedEventNight.Date:MM/dd/yyyy} {EventCapacity.NameOf(n)}"));

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
