using System.Security.Cryptography;
using System.Text.Json;
using Ben.Data.Common.Interfaces;
using Ben.Data.Source.Context;
using Ben.Data.Source.Entities;
using Ben.Data.WebApi.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Ben.Data.WebApi.Controllers;

/// <summary>
/// Field sessions recorded on a phone and sent up afterwards.
/// </summary>
/// <remarks>
/// <para><b>Two phases, deliberately.</b> The document arrives first and creates the session;
/// each recording follows on its own request. A night of video is gigabytes over whatever
/// connection somebody has when they get home, and a single monolithic upload bets the whole
/// session on none of it dropping. One file at a time means one dropped connection costs one
/// file, and the rest can be retried without re-sending what already landed.</para>
///
/// <para><b>Retries are expected.</b> The device's own session id makes a resent document find
/// its existing row rather than making a second one, and a resent file replaces its predecessor.
/// Two copies of one night is worse than none, because nobody can tell which is which.</para>
///
/// <para><b>Who may send.</b> Somebody who was actually on the investigation — an attendee row —
/// or an active member of the organization running it. The same shape as
/// <see cref="Entities.EventEvidenceController"/>, for the same reason: the record has to be
/// attributable to somebody who was there.</para>
/// </remarks>
[ApiController]
[Route("api/field-sessions")]
[Authorize]
public sealed class FieldSessionUploadController : BenControllerBase
{
    /// The "Case Evidence" type, shared with the other evidence doors.
    private static readonly Guid EvidenceFileTypeId = new("20000000-0000-0000-0000-000000000001");

    private readonly IDbContextFactory<BenDataContext> _db;
    private readonly IFileStorageService _fileStorage;
    private readonly IMediaIngestService _mediaIngest;
    private readonly ILogger<FieldSessionUploadController> _log;

    public FieldSessionUploadController(
        IDbContextFactory<BenDataContext> db,
        IFileStorageService fileStorage,
        IMediaIngestService mediaIngest,
        ILogger<FieldSessionUploadController> log)
    {
        _db = db;
        _fileStorage = fileStorage;
        _mediaIngest = mediaIngest;
        _log = log;
    }

    // ── Reading ───────────────────────────────────────────────────────────────

    /// <summary>Everything this account has sent up — personal sessions included.</summary>
    [HttpGet("mine")]
    public async Task<ActionResult<IEnumerable<FieldSessionRecord>>> GetMine(CancellationToken ct)
    {
        var userId = GetCurrentUserId();
        if (userId == Guid.Empty) return Unauthorized();

        await using var db = await _db.CreateDbContextAsync(ct);
        var sessions = await db.FieldSessionUploads.AsNoTracking()
            .Where(s => s.SubmittedByAppUserId == userId)
            .Include(s => s.Files)
            .OrderByDescending(s => s.StartedAt)
            .ToListAsync(ct);

        return Ok(sessions.Select(ToRecord));
    }

    /// <summary>Everything anyone has sent up for one investigation.</summary>
    [HttpGet("for-investigation/{investigationId:guid}")]
    public async Task<ActionResult<IEnumerable<FieldSessionRecord>>> GetForInvestigation(
        Guid investigationId, CancellationToken ct)
    {
        var userId = GetCurrentUserId();
        if (userId == Guid.Empty) return Unauthorized();

        await using var db = await _db.CreateDbContextAsync(ct);
        if (!await MayContributeAsync(db, investigationId, userId, ct)) return NotFound();

        var sessions = await db.FieldSessionUploads.AsNoTracking()
            .Where(s => s.InvestigationId == investigationId)
            .Include(s => s.Files)
            .OrderByDescending(s => s.StartedAt)
            .ToListAsync(ct);

        return Ok(sessions.Select(ToRecord));
    }

    // ── The document ──────────────────────────────────────────────────────────

    /// <summary>
    /// Takes the session's <c>data.json</c> and creates (or updates) its record.
    /// </summary>
    [HttpPost("document")]
    [Consumes("multipart/form-data")]
    [DisableRequestSizeLimit]
    public async Task<ActionResult<FieldSessionRecord>> SubmitDocument(
        IFormFile file, [FromForm] Guid deviceSessionId,
        [FromForm] Guid? investigationId,
        [FromForm] Guid? recordedByAppUserId, [FromForm] string? recordedByName,
        CancellationToken ct)
    {
        var userId = GetCurrentUserId();
        if (userId == Guid.Empty) return Unauthorized();
        if (file is null || file.Length == 0) return BadRequest("The session document is empty.");
        if (deviceSessionId == Guid.Empty)
            return BadRequest("The session is missing its own identifier.");

        await using var db = await _db.CreateDbContextAsync(ct);

        // No investigation is an ordinary case, not a missing value: somebody scouting a
        // building, or a tour guide walking a route. It belongs to their account until there is
        // an investigation to attach it to.
        Guid organizationId;
        if (investigationId is Guid target && target != Guid.Empty)
        {
            var investigation = await db.Investigations
                .FirstOrDefaultAsync(i => i.Id == target, ct);
            if (investigation is null) return NotFound();
            // Same answer as absent: whether somebody else's investigation exists is not a
            // thing to let an outsider probe for.
            if (!await MayContributeAsync(db, target, userId, ct)) return NotFound();
            organizationId = investigation.OrganizationId;
        }
        else
        {
            investigationId = null;
            organizationId = Guid.Empty;
        }

        // Read and check the document BEFORE anything is stored. A row pointing at a file that
        // turns out not to be a session is worse than a refusal.
        string documentText;
        using (var stream = file.OpenReadStream())
        using (var reader = new StreamReader(stream))
        {
            documentText = await reader.ReadToEndAsync(ct);
        }

        DeviceDataSummary summary;
        try
        {
            summary = DeviceDataSummary.Read(documentText);
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException)
        {
            return BadRequest("That doesn't look like a session document: " + ex.Message);
        }

        var storedName = $"{Guid.NewGuid()}.json";
        // A personal session lives under the person, not under a group they may not belong to.
        var storagePath = organizationId == Guid.Empty
            ? _fileStorage.UserFilePath(userId, $"field-sessions/{storedName}")
            : _fileStorage.OrgFilePath(organizationId, $"field-sessions/{storedName}");
        var bytes = System.Text.Encoding.UTF8.GetBytes(documentText);
        await _fileStorage.WriteAsync(storagePath, new MemoryStream(bytes), ct);

        var uploadFile = new UploadFile
        {
            Id = Guid.NewGuid(), UploadFileTypeId = EvidenceFileTypeId, AppUserId = userId,
            FileName = string.IsNullOrWhiteSpace(file.FileName) ? "data.json" : file.FileName,
            StoredFileName = storedName, ContentType = "application/json",
            FileSize = bytes.LongLength, StoragePath = storagePath, IsPublic = false,
            DateCreated = DateTime.UtcNow, CreatedByAppUserId = userId,
        };
        db.UploadFiles.Add(uploadFile);

        // A retried upload finds its own row. Two copies of one night is worse than none.
        // Scoped to the SENDER: a retry finds its own row, while two people handed the same
        // exported session each keep their own copy.
        var session = await db.FieldSessionUploads
            .FirstOrDefaultAsync(s => s.SubmittedByAppUserId == userId
                                   && s.DeviceSessionId == deviceSessionId, ct);
        if (session is null)
        {
            session = new FieldSessionUpload
            {
                Id = Guid.NewGuid(),
                SubmittedByAppUserId = userId,
                DeviceSessionId = deviceSessionId,
                DateCreated = DateTime.UtcNow,
                CreatedByAppUserId = userId,
            };
            db.FieldSessionUploads.Add(session);
        }
        else
        {
            session.DateUpdated = DateTime.UtcNow;
            session.UpdatedByAppUserId = userId;
        }

        // Who RECORDED it, which is not always who is sending it — a device can be handed over,
        // and a session recorded while signed out has nobody's name on it at all. It is never
        // silently attributed to the uploader.
        if (recordedByAppUserId is Guid recorded && recorded != Guid.Empty)
        {
            var account = await db.Users.AsNoTracking()
                .Where(u => u.Id == recorded)
                .Select(u => new { u.Id, u.DisplayName })
                .FirstOrDefaultAsync(ct);
            session.RecordedByAppUserId = account?.Id;
            // The server resolves the name: the device knows the id it signed in with but not
            // necessarily the display name, and the account is the authority on that anyway.
            session.RecordedByName = string.IsNullOrWhiteSpace(recordedByName)
                ? account?.DisplayName
                : recordedByName.Trim();
        }
        else
        {
            session.RecordedByAppUserId = null;
            session.RecordedByName = null;
        }

        // Set on every submission, so choosing an investigation later is simply re-sending.
        session.InvestigationId = investigationId;
        session.DocumentUploadFileId = uploadFile.Id;
        session.DeviceModel = summary.DeviceModel;
        session.LocationLabel = summary.LocationLabel;
        session.StartedAt = summary.StartedAt;
        session.EndedAt = summary.EndedAt;
        session.ReadingCount = summary.ReadingCount;
        session.MarkerCount = summary.MarkerCount;

        await db.SaveChangesAsync(ct);
        await db.Entry(session).Collection(s => s.Files).LoadAsync(ct);

        _log.LogInformation(
            "Field session {DeviceSessionId} uploaded to investigation {InvestigationId} "
            + "({Readings} readings, {Markers} marked).",
            deviceSessionId, investigationId, summary.ReadingCount, summary.MarkerCount);

        return Ok(ToRecord(session));
    }

    // ── The recordings ────────────────────────────────────────────────────────

    /// <summary>
    /// Attaches one recording the document refers to.
    /// </summary>
    [HttpPost("{sessionId:guid}/files")]
    [Consumes("multipart/form-data")]
    [DisableRequestSizeLimit]
    public async Task<ActionResult<FieldSessionFileRecord>> SubmitFile(
        Guid sessionId, IFormFile file,
        [FromForm] string relativePath, [FromForm] string? sha256, CancellationToken ct)
    {
        var userId = GetCurrentUserId();
        if (userId == Guid.Empty) return Unauthorized();
        if (file is null || file.Length == 0) return BadRequest("That file is empty.");

        // The document's own path rules are a security boundary, not a style preference: an
        // importer must never be steered outside its own directory.
        if (string.IsNullOrWhiteSpace(relativePath)
            || relativePath.StartsWith('/') || relativePath.StartsWith('\\')
            || relativePath.Contains("..") || relativePath.Contains('\\'))
        {
            return BadRequest("That file path isn't one a session can carry.");
        }

        await using var db = await _db.CreateDbContextAsync(ct);
        var session = await db.FieldSessionUploads
            .Include(s => s.Investigation)
            .FirstOrDefaultAsync(s => s.Id == sessionId, ct);
        if (session is null) return NotFound();

        // Files follow the session's own door: whoever may contribute to its investigation, or
        // the person whose session it is when there isn't one.
        var allowed = session.InvestigationId is Guid linked
            ? await MayContributeAsync(db, linked, userId, ct)
            : session.SubmittedByAppUserId == userId;
        if (!allowed) return NotFound();

        var storedName = $"{Guid.NewGuid()}{Path.GetExtension(relativePath)}";
        var storagePath = session.Investigation is null
            ? _fileStorage.UserFilePath(session.SubmittedByAppUserId, $"field-sessions/{storedName}")
            : _fileStorage.OrgFilePath(session.Investigation.OrganizationId,
                                       $"field-sessions/{storedName}");

        var uploadFileId = Guid.NewGuid();
        IngestedMedia ingested;
        try
        {
            ingested = await _mediaIngest.IngestAsync(file, storagePath, uploadFileId, ct);
        }
        catch (UnreadableImageException ex)
        {
            return BadRequest(ex.Message);
        }

        // Checked against the digest the DEVICE computed. A truncated upload nobody noticed is
        // worse than a refused one — and the mismatch is recorded rather than thrown away, so
        // somebody can see what happened rather than wondering why a file sounds wrong.
        var matched = true;
        if (!string.IsNullOrWhiteSpace(sha256))
        {
            await using var stored = await _fileStorage.OpenReadAsync(storagePath, ct);
            var digest = Convert.ToHexString(await SHA256.HashDataAsync(stored, ct)).ToLowerInvariant();
            matched = string.Equals(digest, sha256.Trim(), StringComparison.OrdinalIgnoreCase);
            if (!matched)
            {
                _log.LogWarning(
                    "Field session file {Path} arrived with a digest that did not match.",
                    relativePath);
            }
        }

        var uploadFile = new UploadFile
        {
            Id = uploadFileId, UploadFileTypeId = EvidenceFileTypeId, AppUserId = userId,
            FileName = Path.GetFileName(relativePath), StoredFileName = storedName,
            ContentType = ingested.ServedContentType, FileSize = ingested.ServedFileSize,
            StoragePath = storagePath, IsPublic = false,
            DateCreated = DateTime.UtcNow, CreatedByAppUserId = userId,
        };
        db.UploadFiles.Add(uploadFile);
        db.UploadFileMetadata.Add(ingested.Metadata);

        // A retried file replaces its predecessor rather than doubling up.
        var existing = await db.FieldSessionUploadFiles
            .FirstOrDefaultAsync(f => f.FieldSessionUploadId == sessionId
                                   && f.RelativePath == relativePath, ct);
        if (existing is null)
        {
            existing = new FieldSessionUploadFile
            {
                Id = Guid.NewGuid(), FieldSessionUploadId = sessionId,
                RelativePath = relativePath,
                DateCreated = DateTime.UtcNow, CreatedByAppUserId = userId,
            };
            db.FieldSessionUploadFiles.Add(existing);
        }
        else
        {
            existing.DateUpdated = DateTime.UtcNow;
            existing.UpdatedByAppUserId = userId;
        }

        existing.UploadFileId = uploadFile.Id;
        existing.Sha256 = sha256?.Trim().ToLowerInvariant();
        existing.DigestMatched = matched;

        await db.SaveChangesAsync(ct);

        return Ok(new FieldSessionFileRecord(
            existing.Id, existing.RelativePath, uploadFile.FileSize,
            existing.Sha256, existing.DigestMatched, existing.DateCreated));
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Somebody who was on the investigation, or an active member of the group running it.
    /// </summary>
    private static async Task<bool> MayContributeAsync(
        BenDataContext db, Guid investigationId, Guid userId, CancellationToken ct)
    {
        var wasThere = await db.InvestigationAttendees.AsNoTracking()
            .AnyAsync(a => a.InvestigationId == investigationId && a.AppUserId == userId, ct);
        if (wasThere) return true;

        return await db.Investigations.AsNoTracking()
            .Where(i => i.Id == investigationId)
            .AnyAsync(i => db.OrganizationUserMemberships
                .Any(m => m.OrganizationId == i.OrganizationId
                       && m.AppUserId == userId && m.IsActive), ct);
    }

    private static FieldSessionRecord ToRecord(FieldSessionUpload session) =>
        new(session.Id, session.InvestigationId, session.DeviceSessionId, session.DeviceModel, session.LocationLabel,
            session.StartedAt, session.EndedAt, session.ReadingCount, session.MarkerCount,
            session.DocumentUploadFileId, session.RecordedByAppUserId, session.RecordedByName,
            session.DateCreated,
            session.Files
                .OrderBy(f => f.RelativePath)
                .Select(f => new FieldSessionFileRecord(
                    f.Id, f.RelativePath, f.UploadFile?.FileSize ?? 0,
                    f.Sha256, f.DigestMatched, f.DateCreated))
                .ToList());
}

/// <summary>The few facts read out of a session document so sessions can be listed without
/// opening it. Everything else stays in the document, which is the only copy that is definitely
/// what the device wrote.</summary>
public sealed record DeviceDataSummary(
    string DeviceModel, string? LocationLabel, DateTime StartedAt, DateTime? EndedAt,
    int ReadingCount, int MarkerCount)
{
    /// <summary>Reads a Device Data Format v1 document, refusing anything that is not one.</summary>
    public static DeviceDataSummary Read(string json)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;

        if (!root.TryGetProperty("format_version", out var version))
            throw new InvalidOperationException("it has no format_version.");
        if (!root.TryGetProperty("device", out var device)
            || !device.TryGetProperty("model", out var model))
            throw new InvalidOperationException("it does not say what device recorded it.");
        if (!root.TryGetProperty("session", out var session)
            || !session.TryGetProperty("started_at", out var startedAt))
            throw new InvalidOperationException("it does not say when the session started.");

        var major = version.GetString()?.Split('.').FirstOrDefault();
        if (major != "1")
            throw new InvalidOperationException($"this server reads version 1, not {version.GetString()}.");

        var readings = root.TryGetProperty("readings", out var readingArray)
                       && readingArray.ValueKind == JsonValueKind.Array
            ? readingArray
            : default;

        var readingCount = readings.ValueKind == JsonValueKind.Array ? readings.GetArrayLength() : 0;
        var markerCount = 0;
        if (readings.ValueKind == JsonValueKind.Array)
        {
            foreach (var reading in readings.EnumerateArray())
            {
                if (reading.TryGetProperty("measurements", out var measurements)
                    && measurements.ValueKind == JsonValueKind.Object
                    && measurements.TryGetProperty("marker", out _))
                {
                    markerCount++;
                }
            }
        }

        return new DeviceDataSummary(
            DeviceModel: model.GetString() ?? "unknown",
            LocationLabel: session.TryGetProperty("location_label", out var label)
                           && label.ValueKind == JsonValueKind.String ? label.GetString() : null,
            StartedAt: startedAt.GetDateTime().ToUniversalTime(),
            EndedAt: session.TryGetProperty("ended_at", out var endedAt)
                     && endedAt.ValueKind == JsonValueKind.String
                     ? endedAt.GetDateTime().ToUniversalTime() : null,
            ReadingCount: readingCount,
            MarkerCount: markerCount);
    }
}

public sealed record FieldSessionRecord(
    Guid Id, Guid? InvestigationId, Guid DeviceSessionId, string DeviceModel, string? LocationLabel,
    DateTime StartedAt, DateTime? EndedAt, int ReadingCount, int MarkerCount,
    Guid DocumentUploadFileId, Guid? RecordedByAppUserId, string? RecordedByName,
    DateTime DateCreated, IReadOnlyList<FieldSessionFileRecord> Files);

public sealed record FieldSessionFileRecord(
    Guid Id, string RelativePath, long FileSize, string? Sha256, bool DigestMatched,
    DateTime DateCreated);
