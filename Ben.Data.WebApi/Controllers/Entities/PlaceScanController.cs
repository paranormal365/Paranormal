using System.Security.Cryptography;
using Ben.Data.Common.Enums;
using Ben.Data.Common.Interfaces;
using Ben.Data.WebApi.Services;
using Ben.Data.WebApi.Services.Billing;
using Ben.Data.WebApi.Services.FieldSessions;
using Ben.Data.WebApi.Services.Places;
using Ben.Data.WebApi.Services.Venues;
using Ben.Service.Models.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Ben.Data.WebApi.Controllers.Entities;

/// <summary>
/// A place's room scans: which rooms have one, who is scanning which, the upload, the files, and
/// the offers a custodian decides (LiDAR plan, phase 1, §4).
/// </summary>
/// <remarks>
/// <para><b>The scan belongs to the place.</b> One current scan per room; everybody who comes
/// after compares against it. The rules — who may scan, who looks after the scans, when one may
/// be replaced — are <see cref="PlaceScans"/>; this controller asks them and stores the answer.</para>
///
/// <para><b>Not allowed is 404 for reading and 403 with the reason for writing</b>, like the rest
/// of the field-session API: whether a stranger's home has scans is not for probing, and somebody
/// turned away from scanning should be told why.</para>
///
/// <para><b>An offered scan is shown to nobody but the scanner and the custodian</b> until it is
/// accepted (§2 rule 5).</para>
/// </remarks>
[ApiController]
[Route("api/places/{placeId:guid}/scans")]
[Authorize]
public sealed class PlaceScanController(
    IDbContextFactory<BenDataContext> dbFactory,
    IFileStorageService storage,
    IBenBundleStore bundles,
    PlatformMessageService messages,
    ILogger<PlaceScanController> log,
    TimeProvider? clock = null) : BenControllerBase
{
    /// <summary>Room for the form's other fields around a scan at its largest.</summary>
    private const long FormSlackBytes = 16L * 1024 * 1024;

    private const string Baseline = "baseline", Replace = "replace", Offer = "offer";

    private DateTime Now => (clock ?? TimeProvider.System).GetUtcNow().UtcDateTime;

    /// <summary>
    /// The rooms at a place with their current scans, who is scanning which, and what the caller
    /// may do in each — the phone's room picker.
    /// </summary>
    /// <remarks>
    /// The verified venue's own rooms are listed too, scanned or not, so a venue's names come first
    /// and a visitor picks "Room 217" rather than typing "room 217 (the haunted one)".
    /// </remarks>
    [HttpGet]
    public async Task<ActionResult<PlaceScanRoomList>> List(Guid placeId, CancellationToken ct)
    {
        var userId = GetCurrentUserId();
        if (userId == Guid.Empty) return Unauthorized();

        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var standing = await PlaceScans.StandingAsync(db, placeId, userId, ct);
        if (standing is not { MayView: true }) return NotFound();

        var now = Now;
        var place = standing.Place;

        var current = await db.PlaceScans.AsNoTracking()
            .Where(s => s.PlaceId == placeId && s.State == PlaceScanState.Current)
            .ToListAsync(ct);
        var holds = await db.PlaceScanHolds.AsNoTracking()
            .Where(h => h.PlaceId == placeId && h.ExpiresUtc > now)
            .OrderBy(h => h.TakenUtc)
            .ToListAsync(ct);
        var offers = await db.PlaceScans.AsNoTracking()
            .Where(s => s.PlaceId == placeId && s.State == PlaceScanState.Offered)
            .Select(s => s.RoomKey)
            .ToListAsync(ct);

        var venue = await VenueGrants.VerifiedVenueAtAsync(db, placeId, ct);
        var venueRooms = venue is null
            ? new List<(Guid Id, string Name)>()
            : (await db.PlaceRooms.AsNoTracking()
                .Where(r => r.PlaceId == placeId && r.OrganizationId == venue.OrganizationId && r.IsActive)
                .OrderBy(r => r.SortOrder).ThenBy(r => r.Name)
                .Select(r => new { r.Id, r.Name })
                .ToListAsync(ct))
              .Select(r => (r.Id, r.Name)).ToList();

        var names = await NamesAsync(db, current.Select(s => s.ScannedByAppUserId).Concat(holds.Select(h => h.AppUserId)), ct);

        // Every room worth a line, in the venue's own order first and then by name.
        var keys = new List<(string Key, string Name, Guid? RoomId)>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var room in venueRooms)
        {
            var key = PlaceScans.NormalizeRoomKey(room.Name);
            if (key.Length > 0 && seen.Add(key)) keys.Add((key, room.Name, room.Id));
        }
        foreach (var (key, name) in current.Select(s => (s.RoomKey, s.RoomName))
                     .Concat(holds.Select(h => (h.RoomKey, h.RoomName)))
                     .OrderBy(r => r.Item2, StringComparer.OrdinalIgnoreCase))
        {
            if (seen.Add(key)) keys.Add((key, name, null));
        }

        var rooms = new List<PlaceScanRoomRecord>();
        foreach (var (key, name, roomId) in keys)
        {
            var scan = current.FirstOrDefault(s => s.RoomKey == key);
            var hold = holds.FirstOrDefault(h => h.RoomKey == key && h.AppUserId == userId)
                    ?? holds.FirstOrDefault(h => h.RoomKey == key);

            string? why = null;
            var mayDecide = false;
            if (scan is not null)
            {
                why = await PlaceScans.WhyCannotReplaceAsync(db, place, scan, userId, now, ct);
                mayDecide = await PlaceScans.IsCustodianAsync(db, await PlaceScans.CustodianAsync(db, place, scan, ct), userId, ct);
            }
            var heldByOther = hold is not null && hold.AppUserId != userId;

            rooms.Add(new PlaceScanRoomRecord(
                key, scan?.RoomName ?? name,
                scan is null ? null : Summary(scan, names, userId),
                CanScan: scan is null && standing.MayScan && !heldByOther,
                CanCompare: scan is not null && standing.MayScan,
                CanReplace: scan is not null && standing.MayScan && why is null,
                ReplaceReason: scan is null ? null : why,
                HoldBy: hold is null ? null : new PlaceScanHoldRecord(
                    hold.AppUserId == userId ? hold.Id : null, hold.RoomKey, hold.RoomName,
                    names.GetValueOrDefault(hold.AppUserId, "Someone"), hold.AppUserId == userId, hold.ExpiresUtc),
                OffersWaiting: mayDecide ? offers.Count(k => k == key) : 0,
                PlaceRoomId: roomId ?? scan?.PlaceRoomId));
        }

        var placeCustodian = await PlaceScans.CustodianAsync(db, place, null, ct);

        return Ok(new PlaceScanRoomList(
            place.Id, place.Name, place.Kind, standing.MayScan, standing.WhyNot,
            placeCustodian is null ? null : new PlaceScanCustodianRecord(
                placeCustodian.Kind == PlaceScans.CustodianKind.Venue ? "venue" : "grant",
                placeCustodian.Name, await PlaceScans.IsCustodianAsync(db, placeCustodian, userId, ct)),
            venue is null ? await StorageLeftAsync(db, userId, ct) : null,
            rooms));
    }

    /// <summary>
    /// Takes a room for twenty minutes, so the next person at the building is told somebody is
    /// already scanning it. Taking your own hold again extends it.
    /// </summary>
    [HttpPost("holds")]
    public async Task<ActionResult<PlaceScanHoldRecord>> TakeHold(
        Guid placeId, [FromBody] TakeScanHoldRequest? request, CancellationToken ct)
    {
        var userId = GetCurrentUserId();
        if (userId == Guid.Empty) return Unauthorized();

        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var standing = await PlaceScans.StandingAsync(db, placeId, userId, ct);
        if (standing is not { MayView: true }) return NotFound();
        if (!standing.MayScan) return StatusCode(StatusCodes.Status403Forbidden, standing.WhyNot);

        var name = PlaceScans.CleanRoomName(request?.Room);
        if (name is null) return BadRequest("Say which room you are about to scan.");
        if (name.Length > PlaceScans.MaxRoomNameLength)
            return BadRequest($"A room's name can be at most {PlaceScans.MaxRoomNameLength} characters.");
        var key = PlaceScans.NormalizeRoomKey(name);
        var now = Now;

        var current = await db.PlaceScans.AsNoTracking()
            .FirstOrDefaultAsync(s => s.PlaceId == placeId && s.RoomKey == key && s.State == PlaceScanState.Current, ct);

        var other = await db.PlaceScanHolds.AsNoTracking()
            .Where(h => h.PlaceId == placeId && h.RoomKey == key && h.ExpiresUtc > now && h.AppUserId != userId)
            .OrderBy(h => h.TakenUtc)
            .FirstOrDefaultAsync(ct);
        if (other is not null)
        {
            var who = await PlaceScans.PersonNameAsync(db, other.AppUserId, ct);
            return Conflict(new PlaceScanHeldRecord("held",
                current is null
                    ? $"{who} is scanning {other.RoomName} right now. When their scan is in, you can compare against it."
                    : $"{who} is scanning {other.RoomName} right now — compare it instead, or wait.",
                who, other.ExpiresUtc));
        }

        if (current is not null
            && await PlaceScans.WhyCannotReplaceAsync(db, standing.Place, current, userId, now, ct) is { } why)
        {
            var names = await NamesAsync(db, [current.ScannedByAppUserId], ct);
            return Conflict(new PlaceScanConflictRecord(
                "already scanned", why, current.RoomName, Summary(current, names, userId),
                MayReplace: false, why, ["compare", "offer"]));
        }

        // One room at a time: somebody who moves on to the next room has finished with the last.
        var mine = await db.PlaceScanHolds
            .Where(h => h.PlaceId == placeId && h.AppUserId == userId)
            .ToListAsync(ct);
        var hold = mine.FirstOrDefault(h => h.RoomKey == key && h.ExpiresUtc > now);
        db.PlaceScanHolds.RemoveRange(mine.Where(h => h != hold));

        if (hold is null)
        {
            hold = new PlaceScanHold
            {
                Id = Guid.NewGuid(), PlaceId = placeId, RoomKey = key,
                RoomName = current?.RoomName ?? name, AppUserId = userId, TakenUtc = now,
            };
            db.PlaceScanHolds.Add(hold);
        }
        hold.ExpiresUtc = now + PlaceScans.HoldLength;
        await db.SaveChangesAsync(ct);

        return Ok(new PlaceScanHoldRecord(
            hold.Id, hold.RoomKey, hold.RoomName,
            await PlaceScans.PersonNameAsync(db, userId, ct), IsYou: true, hold.ExpiresUtc));
    }

    /// <summary>Lets a room go before its twenty minutes are up — a scan canceled, or finished elsewhere.</summary>
    [HttpDelete("holds/{holdId:guid}")]
    public async Task<IActionResult> ReleaseHold(Guid placeId, Guid holdId, CancellationToken ct)
    {
        var userId = GetCurrentUserId();
        if (userId == Guid.Empty) return Unauthorized();

        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var hold = await db.PlaceScanHolds
            .FirstOrDefaultAsync(h => h.Id == holdId && h.PlaceId == placeId && h.AppUserId == userId, ct);

        // Gone already is what the caller wanted; somebody else's is not theirs to know about.
        if (hold is not null)
        {
            db.PlaceScanHolds.Remove(hold);
            await db.SaveChangesAsync(ct);
        }
        return NoContent();
    }

    /// <summary>
    /// The upload: one <c>.scan</c> file for one room, and the server's decision about what it is.
    /// </summary>
    /// <remarks>
    /// <para><b>No current scan:</b> it becomes the room's scan. <b>A current scan the caller may
    /// replace:</b> the old one becomes a previous version and this one the current. <b>One they may
    /// not:</b> sent as an offer, it waits for the custodian; sent as a first scan or a replacement,
    /// the answer is 409 with the choices — the phone still holds the file and asks whether to keep
    /// it as a comparison, offer it, or discard it.</para>
    ///
    /// <para><b>Who pays:</b> the verified venue's group when the place has one, otherwise the
    /// uploader's own allowance, asked before a byte is written.</para>
    /// </remarks>
    /// <param name="intent"><c>baseline</c> (the default), <c>replace</c> or <c>offer</c>.</param>
    [HttpPost]
    [Consumes("multipart/form-data")]
    [RequestSizeLimit(PlaceScanManifest.MaxScanBytes + FormSlackBytes)]
    [RequestFormLimits(MultipartBodyLengthLimit = PlaceScanManifest.MaxScanBytes + FormSlackBytes)]
    public async Task<ActionResult<PlaceScanUploadResult>> Upload(
        Guid placeId, IFormFile? file, [FromForm] string? room, [FromForm] string? retention,
        [FromForm] Guid? holdId, [FromForm] string? intent, CancellationToken ct)
    {
        var userId = GetCurrentUserId();
        if (userId == Guid.Empty) return Unauthorized();

        if (file is null || file.Length == 0) return BadRequest("That scan file is empty.");
        if (file.Length > PlaceScanManifest.MaxScanBytes)
            return BadRequest($"That scan is {file.Length / (1024.0 * 1024 * 1024):0.#} GB; a room scan can be at most 2 GB.");

        var name = PlaceScans.CleanRoomName(room);
        if (name is null) return BadRequest("Say which room this scan is of.");
        if (name.Length > PlaceScans.MaxRoomNameLength)
            return BadRequest($"A room's name can be at most {PlaceScans.MaxRoomNameLength} characters.");
        var key = PlaceScans.NormalizeRoomKey(name);

        PlaceScanRetention kept;
        switch (retention)
        {
            case null or "" or "modelAndPictures": kept = PlaceScanRetention.ModelAndPictures; break;
            case "modelOnly": kept = PlaceScanRetention.ModelOnly; break;
            default: return BadRequest("Keep the model and pictures, or the model only.");
        }

        intent = string.IsNullOrWhiteSpace(intent) ? Baseline : intent.Trim();
        if (intent is not (Baseline or Replace or Offer))
            return BadRequest("A scan is sent as a first scan, a replacement, or an offer.");

        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var standing = await PlaceScans.StandingAsync(db, placeId, userId, ct);
        if (standing is not { MayView: true }) return NotFound();
        if (!standing.MayScan) return StatusCode(StatusCodes.Status403Forbidden, standing.WhyNot);

        var now = Now;
        var venue = await VenueGrants.VerifiedVenueAtAsync(db, placeId, ct);

        // Asked before the bytes are written: being refused for space after waiting for a hundred
        // megabytes to send is the version of this that wastes somebody's evening.
        if (venue is null
            && await AccountStorageGuard.WhyCannotStoreAsync(db, userId, file.Length, ct) is { } full)
        {
            return StatusCode(StatusCodes.Status413PayloadTooLarge, full);
        }

        var scanId = Guid.NewGuid();
        var path = storage.PlaceFilePath(placeId, $"scans/{scanId:N}/room.scan");

        // Hashed on the way to storage, so the digest costs no second read of the file.
        string sha;
        await using (var incoming = file.OpenReadStream())
        {
            using var hash = SHA256.Create();
            await using var hashing = new CryptoStream(incoming, hash, CryptoStreamMode.Read, leaveOpen: true);
            await storage.WriteAsync(path, hashing, ct);
            if (!hashing.HasFlushedFinalBlock) await hashing.FlushFinalBlockAsync(ct);
            sha = Convert.ToHexString(hash.Hash!).ToLowerInvariant();
        }

        PlaceScanManifest manifest;
        BenBundle bundle;
        try
        {
            bundle = await bundles.IndexAsync(path, ct, BenBundleRules.Scan);
            manifest = await PlaceScanManifest.ReadAsync(bundle,
                async (entry, token) => await bundles.OpenEntryAsync(path, entry, token, BenBundleRules.Scan), ct);

            if (PlaceScans.NormalizeRoomKey(manifest.Room) != key)
                throw new BenBundleFormatException($"That scan is of {manifest.Room}, but it was sent as {name}.");
            if (manifest.PlaceId is Guid madeAt && madeAt != placeId)
                throw new BenBundleFormatException("That scan was made at a different place.");
            if (manifest.Retention != kept)
                throw new BenBundleFormatException(manifest.Retention == PlaceScanRetention.ModelOnly
                    ? "That scan keeps the model only, but it was sent as keeping its pictures."
                    : "That scan keeps its pictures, but it was sent as the model only.");
            if (manifest.ScannedAt > now.AddDays(1))
                throw new BenBundleFormatException("That scan says it was made in the future. Check the phone's date and time.");
        }
        catch (Exception ex) when (ex is BenBundleFormatException or InvalidOperationException or IOException)
        {
            // Nothing half-accepted is left behind.
            await DiscardAsync(path, ct);
            return BadRequest(ex is BenBundleFormatException ? ex.Message : "That doesn't look like a room scan: " + ex.Message);
        }

        var names = await NamesAsync(db, [userId], ct);

        // Decided, then saved; the unique indexes stand behind both halves. Somebody else's upload
        // landing between the read and the write is read again and decided again, so the loser of
        // the race is told rather than quietly made a second current scan.
        for (var attempt = 1; ; attempt++)
        {
            var current = await db.PlaceScans
                .FirstOrDefaultAsync(s => s.PlaceId == placeId && s.RoomKey == key && s.State == PlaceScanState.Current, ct);

            PlaceScanState state;
            string outcome;
            if (current is null)
            {
                (state, outcome) = (PlaceScanState.Current, "current");
            }
            else if (intent == Offer)
            {
                (state, outcome) = (PlaceScanState.Offered, "offered");
            }
            else if (await PlaceScans.WhyCannotReplaceAsync(db, standing.Place, current, userId, now, ct) is { } why)
            {
                await DiscardAsync(path, ct);
                var currentNames = await NamesAsync(db, [current.ScannedByAppUserId], ct);
                return Conflict(new PlaceScanConflictRecord(
                    "already scanned",
                    $"{current.RoomName} already has a scan. Your scan is still on your phone: keep it as a "
                    + "comparison, offer it to replace the room's scan, or discard it.",
                    current.RoomName, Summary(current, currentNames, userId),
                    MayReplace: false, why, ["compare", "offer", "discard"]));
            }
            else
            {
                (state, outcome) = (PlaceScanState.Current, "replaced");
            }

            var versions = await db.PlaceScans.AsNoTracking()
                .Where(s => s.PlaceId == placeId && s.RoomKey == key)
                .OrderBy(s => s.Version)
                .Select(s => new { s.Version, s.RoomName })
                .ToListAsync(ct);

            var scan = new PlaceScan
            {
                Id = scanId, PlaceId = placeId, RoomKey = key,
                // Kept as whoever scanned the room first typed it.
                RoomName = versions.FirstOrDefault()?.RoomName ?? manifest.Room,
                PlaceRoomId = await MatchingRoomAsync(db, placeId, key, venue?.OrganizationId, standing.ActingFor, ct),
                Version = (versions.LastOrDefault()?.Version ?? 0) + 1,
                State = state, Retention = kept, ScannedUtc = manifest.ScannedAt,
                ScannedByAppUserId = userId, ScannedForOrganizationId = standing.ActingFor,
                OwnerAppUserId = venue is null ? userId : null, OwnerOrganizationId = venue?.OrganizationId,
                StoragePath = path, FileSize = file.Length, Sha256 = sha,
                DeviceModel = manifest.Device, ScannerVersion = manifest.ScannerVersion,
                FloorAreaM2 = manifest.FloorAreaM2, TriangleCount = manifest.TriangleCount,
                KeyframeCount = manifest.KeyframeCount,
                DateCreated = now, CreatedByAppUserId = userId,
            };

            try
            {
                // Two saves in one transaction when replacing: the old version must stop being
                // current before the new one starts, or the index refuses the pair.
                if (outcome == "replaced")
                {
                    current!.State = PlaceScanState.Previous;
                    current.DateUpdated = now;
                    current.UpdatedByAppUserId = userId;
                }
                await SaveInOneTransactionAsync(db, async () =>
                {
                    db.PlaceScans.Add(scan);
                    // Their hold on this room has done its job — by room, or by the hold they
                    // took if they renamed the room on the way.
                    db.PlaceScanHolds.RemoveRange(await db.PlaceScanHolds
                        .Where(h => h.PlaceId == placeId && h.AppUserId == userId
                                 && (h.RoomKey == key || h.Id == holdId))
                        .ToListAsync(ct));
                }, ct);
            }
            catch (DbUpdateException) when (attempt < 3)
            {
                db.ChangeTracker.Clear();
                continue;
            }
            catch
            {
                await DiscardAsync(path, ct);
                throw;
            }

            log.LogInformation(
                "Room scan {ScanId} of {Room} at place {PlaceId} arrived as version {Version} ({Outcome}, {Bytes} bytes, {Keyframes} keyframes).",
                scan.Id, scan.RoomName, placeId, scan.Version, outcome, scan.FileSize, scan.KeyframeCount);

            var custodian = await PlaceScans.CustodianAsync(db, standing.Place, outcome == "offered" ? current : scan, ct);
            string says;
            switch (outcome)
            {
                case "offered":
                    says = $"Your scan of {scan.RoomName} was offered to {custodian?.Name ?? "whoever looks after this place's scans"}. "
                         + "Nobody else sees it unless they accept it.";
                    if (custodian is not null)
                        await TellCustodianOfOfferAsync(db, standing.Place, scan, custodian, names[userId], userId, ct);
                    break;
                case "replaced":
                    says = $"Your scan of {scan.RoomName} is now the room's scan. The one from "
                         + $"{current!.ScannedUtc:MM/dd/yyyy} is kept as version {current.Version}.";
                    break;
                default:
                    says = $"{scan.RoomName} is scanned. Everyone who comes after you will compare against it.";
                    break;
            }

            return Ok(new PlaceScanUploadResult(outcome, says, Record(scan, names, userId, bundle)));
        }
    }

    /// <summary>One version of a room's scan, with the members its file carries.</summary>
    [HttpGet("{scanId:guid}")]
    public async Task<ActionResult<PlaceScanRecord>> Get(Guid placeId, Guid scanId, CancellationToken ct)
    {
        var userId = GetCurrentUserId();
        if (userId == Guid.Empty) return Unauthorized();

        await using var db = await dbFactory.CreateDbContextAsync(ct);
        if (await VisibleAsync(db, placeId, scanId, userId, ct) is not { } scan) return NotFound();

        var names = await NamesAsync(db, [scan.ScannedByAppUserId], ct);
        var bundle = storage.Exists(scan.StoragePath)
            ? await bundles.IndexAsync(scan.StoragePath, ct, BenBundleRules.Scan)
            : null;
        return Ok(Record(scan, names, userId, bundle));
    }

    /// <summary>One member of a scan — the mesh, the preview, a keyframe — served by range.</summary>
    [HttpGet("{scanId:guid}/entries/{**entryPath}")]
    public async Task<IActionResult> Entry(Guid placeId, Guid scanId, string entryPath, CancellationToken ct)
    {
        var userId = GetCurrentUserId();
        if (userId == Guid.Empty) return Unauthorized();

        await using var db = await dbFactory.CreateDbContextAsync(ct);
        if (await VisibleAsync(db, placeId, scanId, userId, ct) is not { } scan) return NotFound();
        if (BenBundle.RefusalForPath(entryPath, "scan") is not null || !storage.Exists(scan.StoragePath)) return NotFound();

        var member = await bundles.OpenEntryAsync(scan.StoragePath, entryPath, ct, BenBundleRules.Scan);
        if (member is null) return NotFound();
        return File(member, PlaceScanManifest.ContentTypeFor(entryPath), enableRangeProcessing: true);
    }

    /// <summary>The whole <c>.scan</c>, by range — what a phone caches to view the room and to compare against it.</summary>
    [HttpGet("{scanId:guid}/file")]
    public async Task<IActionResult> Download(Guid placeId, Guid scanId, CancellationToken ct)
    {
        var userId = GetCurrentUserId();
        if (userId == Guid.Empty) return Unauthorized();

        await using var db = await dbFactory.CreateDbContextAsync(ct);
        if (await VisibleAsync(db, placeId, scanId, userId, ct) is not { } scan) return NotFound();
        if (!storage.Exists(scan.StoragePath)) return NotFound();

        var stream = await storage.OpenReadAsync(scan.StoragePath, ct);
        return File(stream, FieldSessionFileGuard.PlaceScanContentType,
                    $"{FileNameFor(scan.RoomKey)}-v{scan.Version}.scan", enableRangeProcessing: true);
    }

    /// <summary>Every version of one room's scan the caller may see, newest first.</summary>
    [HttpGet("rooms/{roomKey}/versions")]
    public async Task<ActionResult<IReadOnlyList<PlaceScanSummaryRecord>>> Versions(
        Guid placeId, string roomKey, CancellationToken ct)
    {
        var userId = GetCurrentUserId();
        if (userId == Guid.Empty) return Unauthorized();

        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var standing = await PlaceScans.StandingAsync(db, placeId, userId, ct);
        if (standing is not { MayView: true }) return NotFound();

        var key = PlaceScans.NormalizeRoomKey(roomKey);
        var all = await db.PlaceScans.AsNoTracking()
            .Where(s => s.PlaceId == placeId && s.RoomKey == key && s.State != PlaceScanState.Removed)
            .OrderByDescending(s => s.Version)
            .ToListAsync(ct);

        var current = all.FirstOrDefault(s => s.State == PlaceScanState.Current);
        var decides = await PlaceScans.IsCustodianAsync(db, await PlaceScans.CustodianAsync(db, standing.Place, current, ct), userId, ct);
        var visible = all
            .Where(s => s.State is PlaceScanState.Current or PlaceScanState.Previous
                     || decides || s.ScannedByAppUserId == userId)
            .ToList();

        var names = await NamesAsync(db, visible.Select(s => s.ScannedByAppUserId), ct);
        return Ok(visible.Select(s => Summary(s, names, userId)).ToList());
    }

    /// <summary>The custodian takes an offered scan: it becomes the room's scan and the old one a previous version.</summary>
    [HttpPost("{scanId:guid}/accept")]
    public async Task<ActionResult<PlaceScanRecord>> Accept(
        Guid placeId, Guid scanId, [FromBody] DecideScanOfferRequest? request, CancellationToken ct)
    {
        var userId = GetCurrentUserId();
        if (userId == Guid.Empty) return Unauthorized();

        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var (scan, current, refusal) = await OfferToDecideAsync(db, placeId, scanId, userId, ct);
        if (refusal is not null) return refusal;

        var now = Now;
        var note = Note(request);
        if (current is not null)
        {
            current.State = PlaceScanState.Previous;
            current.DateUpdated = now;
            current.UpdatedByAppUserId = userId;
        }

        try
        {
            await SaveInOneTransactionAsync(db, () =>
            {
                scan!.State = PlaceScanState.Current;
                scan.DecidedUtc = now;
                scan.DecidedByAppUserId = userId;
                scan.DecisionNote = note;
                scan.DateUpdated = now;
                scan.UpdatedByAppUserId = userId;
                return Task.CompletedTask;
            }, ct);
        }
        catch (DbUpdateException)
        {
            return Conflict("The room's scan changed while you were deciding. Look again and decide again.");
        }

        var place = await db.Places.AsNoTracking().FirstAsync(p => p.Id == placeId, ct);
        await messages.SendAsync(
            $"Your scan of {scan!.RoomName} was accepted",
            $"<p>Your scan of <strong>{VenueNotices.Safe(scan.RoomName)}</strong> at {VenueNotices.Safe(place.Name)} "
            + "is now the room's scan. Everyone who comes after you will compare against it.</p>"
            + (note is null ? "" : $"<p>They said: “{VenueNotices.Safe(note)}”</p>"),
            [scan.ScannedByAppUserId], userId, ct);

        var names = await NamesAsync(db, [scan.ScannedByAppUserId], ct);
        return Ok(Record(scan, names, userId, null));
    }

    /// <summary>The custodian turns an offered scan down. Its file goes; the scanner is told, with the note.</summary>
    [HttpPost("{scanId:guid}/decline")]
    public async Task<ActionResult<PlaceScanRecord>> Decline(
        Guid placeId, Guid scanId, [FromBody] DecideScanOfferRequest? request, CancellationToken ct)
    {
        var userId = GetCurrentUserId();
        if (userId == Guid.Empty) return Unauthorized();

        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var (scan, _, refusal) = await OfferToDecideAsync(db, placeId, scanId, userId, ct);
        if (refusal is not null) return refusal;

        var now = Now;
        var note = Note(request);
        scan!.State = PlaceScanState.Declined;
        scan.DecidedUtc = now;
        scan.DecidedByAppUserId = userId;
        scan.DecisionNote = note;
        scan.DateUpdated = now;
        scan.UpdatedByAppUserId = userId;
        await db.SaveChangesAsync(ct);
        await DiscardAsync(scan.StoragePath, ct);

        var place = await db.Places.AsNoTracking().FirstAsync(p => p.Id == placeId, ct);
        await messages.SendAsync(
            $"Your scan of {scan.RoomName} was not taken",
            $"<p>Your offered scan of <strong>{VenueNotices.Safe(scan.RoomName)}</strong> at {VenueNotices.Safe(place.Name)} "
            + "was declined, so the room keeps its scan. You can still compare against it.</p>"
            + (note is null ? "" : $"<p>They said: “{VenueNotices.Safe(note)}”</p>"),
            [scan.ScannedByAppUserId], userId, ct);

        var names = await NamesAsync(db, [scan.ScannedByAppUserId], ct);
        return Ok(Record(scan, names, userId, null));
    }

    /// <summary>
    /// Takes a version down: its file goes and nobody can open it again. Comparisons made against
    /// it keep their rows. The custodian may remove any version; a scanner may withdraw their own offer.
    /// </summary>
    [HttpDelete("{scanId:guid}")]
    public async Task<IActionResult> Remove(Guid placeId, Guid scanId, CancellationToken ct)
    {
        var userId = GetCurrentUserId();
        if (userId == Guid.Empty) return Unauthorized();

        await using var db = await dbFactory.CreateDbContextAsync(ct);
        if (await VisibleAsync(db, placeId, scanId, userId, ct) is not { } visible) return NotFound();
        var scan = await db.PlaceScans.FirstAsync(s => s.Id == visible.Id, ct);

        var place = await db.Places.AsNoTracking().FirstAsync(p => p.Id == placeId, ct);
        var current = await db.PlaceScans.AsNoTracking()
            .FirstOrDefaultAsync(s => s.PlaceId == placeId && s.RoomKey == scan.RoomKey && s.State == PlaceScanState.Current, ct);
        var custodian = await PlaceScans.CustodianAsync(db, place, current, ct);
        var withdrawing = scan.State == PlaceScanState.Offered && scan.ScannedByAppUserId == userId;
        if (!withdrawing && !await PlaceScans.IsCustodianAsync(db, custodian, userId, ct))
        {
            return StatusCode(StatusCodes.Status403Forbidden,
                $"Only {custodian?.Name ?? "whoever looks after this place's scans"} can take a scan down.");
        }

        scan.State = PlaceScanState.Removed;
        scan.DateUpdated = Now;
        scan.UpdatedByAppUserId = userId;
        await SaveAndAuditAsync(db, scan, scan.Id, userId, ct);
        await DiscardAsync(scan.StoragePath, ct);

        return NoContent();
    }

    // ── helpers ─────────────────────────────────────────────────────────────

    /// <summary>
    /// A scan the caller may see, or null: current and previous versions to anybody who may view
    /// the place; an offer or a declined offer only to its scanner and the custodian; a removed one
    /// to nobody.
    /// </summary>
    private static async Task<PlaceScan?> VisibleAsync(
        BenDataContext db, Guid placeId, Guid scanId, Guid userId, CancellationToken ct)
    {
        var standing = await PlaceScans.StandingAsync(db, placeId, userId, ct);
        if (standing is not { MayView: true }) return null;

        var scan = await db.PlaceScans.AsNoTracking()
            .FirstOrDefaultAsync(s => s.Id == scanId && s.PlaceId == placeId, ct);
        if (scan is null || scan.State == PlaceScanState.Removed) return null;
        if (scan.State is PlaceScanState.Current or PlaceScanState.Previous) return scan;
        if (scan.ScannedByAppUserId == userId) return scan;

        var current = await db.PlaceScans.AsNoTracking()
            .FirstOrDefaultAsync(s => s.PlaceId == placeId && s.RoomKey == scan.RoomKey && s.State == PlaceScanState.Current, ct);
        return await PlaceScans.IsCustodianAsync(db, await PlaceScans.CustodianAsync(db, standing.Place, current, ct), userId, ct)
            ? scan
            : null;
    }

    /// <summary>The offer and the room's current scan, tracked, or the answer to give instead.</summary>
    private async Task<(PlaceScan? Scan, PlaceScan? Current, ActionResult? Refusal)> OfferToDecideAsync(
        BenDataContext db, Guid placeId, Guid scanId, Guid userId, CancellationToken ct)
    {
        if (await VisibleAsync(db, placeId, scanId, userId, ct) is null) return (null, null, NotFound());

        var scan = await db.PlaceScans.FirstAsync(s => s.Id == scanId, ct);
        if (scan.State != PlaceScanState.Offered)
            return (null, null, Conflict("That scan isn't waiting for an answer."));

        var place = await db.Places.AsNoTracking().FirstAsync(p => p.Id == placeId, ct);
        var current = await db.PlaceScans
            .FirstOrDefaultAsync(s => s.PlaceId == placeId && s.RoomKey == scan.RoomKey && s.State == PlaceScanState.Current, ct);

        // With the room's scan taken down meanwhile, whoever kept the last one still decides.
        var keeper = current ?? await db.PlaceScans.AsNoTracking()
            .Where(s => s.PlaceId == placeId && s.RoomKey == scan.RoomKey
                     && (s.State == PlaceScanState.Previous || s.State == PlaceScanState.Removed))
            .OrderByDescending(s => s.Version)
            .FirstOrDefaultAsync(ct);
        var custodian = await PlaceScans.CustodianAsync(db, place, keeper, ct);
        if (!await PlaceScans.IsCustodianAsync(db, custodian, userId, ct))
        {
            return (null, null, StatusCode(StatusCodes.Status403Forbidden,
                $"Only {custodian?.Name ?? "whoever looks after this place's scans"} can decide on this scan."));
        }
        return (scan, current, null);
    }

    private async Task TellCustodianOfOfferAsync(
        BenDataContext db, Place place, PlaceScan scan, PlaceScans.Custodian custodian,
        string scannerName, Guid userId, CancellationToken ct)
    {
        var recipients = (await PlaceScans.CustodianRecipientsAsync(db, custodian, ct))
            .Where(id => id != userId).ToList();
        if (recipients.Count == 0) return;

        await messages.SendAsync(
            $"{scannerName} offered a new scan of {scan.RoomName}",
            $"<p>{VenueNotices.Safe(scannerName)} scanned <strong>{VenueNotices.Safe(scan.RoomName)}</strong> at "
            + $"{VenueNotices.Safe(place.Name)} and offered it to replace the room's scan. You look after this "
            + "place's scans, so it is yours to accept or decline from the room's scans in the Paranormal Platform "
            + "app. Nobody else sees it meanwhile.</p>",
            recipients, userId, ct);
    }

    /// <summary>A group's room at this place with the same name: the venue's first, then the scanner's group's.</summary>
    private static async Task<Guid?> MatchingRoomAsync(
        BenDataContext db, Guid placeId, string key, Guid? venueOrgId, Guid? actingFor, CancellationToken ct)
    {
        var rooms = await db.PlaceRooms.AsNoTracking()
            .Where(r => r.PlaceId == placeId && r.IsActive
                     && (r.OrganizationId == venueOrgId || r.OrganizationId == actingFor))
            .Select(r => new { r.Id, r.Name, r.OrganizationId })
            .ToListAsync(ct);

        return rooms
            .Where(r => PlaceScans.NormalizeRoomKey(r.Name) == key)
            .OrderBy(r => r.OrganizationId == venueOrgId ? 0 : 1)
            .Select(r => (Guid?)r.Id)
            .FirstOrDefault();
    }

    /// <summary>What the caller's own account can still store, or null when nothing caps it.</summary>
    private static async Task<long?> StorageLeftAsync(BenDataContext db, Guid userId, CancellationToken ct)
    {
        if (await PaidPlan.CoversAsync(db, userId, ct)) return null;
        var cap = await AccountStorageGuard.CapBytesAsync(db, ct);
        var used = await AccountStorageGuard.UsedBytesAsync(db, userId, ct);
        return Math.Max(0, cap - used);
    }

    private static async Task<Dictionary<Guid, string>> NamesAsync(
        BenDataContext db, IEnumerable<Guid> ids, CancellationToken ct)
    {
        var wanted = ids.Distinct().ToList();
        var people = await db.AppUsers.AsNoTracking()
            .Where(u => wanted.Contains(u.Id))
            .Select(u => new { u.Id, u.DisplayName, u.FirstName, u.LastName, u.Handle })
            .ToListAsync(ct);
        var names = people.ToDictionary(p => p.Id, p => PlaceScans.PersonName(p.DisplayName, p.FirstName, p.LastName, p.Handle));
        foreach (var id in wanted) names.TryAdd(id, "Someone");
        return names;
    }

    private static PlaceScanSummaryRecord Summary(PlaceScan s, IReadOnlyDictionary<Guid, string> names, Guid userId)
        => new(s.Id, s.Version, s.State, s.Retention, s.ScannedUtc,
               names.GetValueOrDefault(s.ScannedByAppUserId, "Someone"), s.ScannedByAppUserId == userId,
               s.FileSize, s.KeyframeCount, s.FloorAreaM2, s.TriangleCount);

    private static PlaceScanRecord Record(PlaceScan s, IReadOnlyDictionary<Guid, string> names, Guid userId, BenBundle? bundle)
        => new(s.Id, s.PlaceId, s.RoomKey, s.RoomName, s.Version, s.State, s.Retention, s.ScannedUtc,
               names.GetValueOrDefault(s.ScannedByAppUserId, "Someone"), s.ScannedByAppUserId == userId,
               s.FileSize, s.Sha256, s.DeviceModel, s.ScannerVersion, s.KeyframeCount, s.FloorAreaM2, s.TriangleCount,
               s.ShownOnPlacePage, s.DecidedUtc, s.DecisionNote,
               bundle is null ? [] : bundle.Entries.Values
                   .OrderBy(e => e.Path, StringComparer.Ordinal)
                   .Select(e => new PlaceScanEntryRecord(e.Path, e.Length, PlaceScanManifest.ContentTypeFor(e.Path)))
                   .ToList());

    private static string? Note(DecideScanOfferRequest? request)
        => request?.Note?.Trim() is { Length: > 0 } note ? note[..Math.Min(note.Length, 2000)] : null;

    /// <summary>"bedroom-1" — a download name nothing can be hurt by.</summary>
    private static string FileNameFor(string roomKey)
    {
        var safe = new string(roomKey.Select(c => char.IsLetterOrDigit(c) ? c : '-').ToArray()).Trim('-');
        return safe.Length == 0 ? "room" : safe;
    }

    private async Task DiscardAsync(string path, CancellationToken ct)
    {
        bundles.Forget(path);
        try { await storage.DeleteAsync(path, ct); }
        catch (Exception ex) { log.LogWarning(ex, "Could not remove the room scan at {Path}.", path); }
    }
}
