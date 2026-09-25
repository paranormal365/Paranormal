using Ben.Data.Source.Entities;
using Ben.Data.WebApi.Services;
using Ben.Data.WebApi.Services.Billing;
using Ben.Data.WebApi.Services.FieldSessions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Ben.Data.WebApi.Controllers;

/// <summary>
/// A whole session as one <c>.ben</c> file.
/// </summary>
/// <remarks>
/// <para>
/// Ben, 2026-09-16, looking at an upload on the site: "It looks like a bunch of data.json files. I
/// specifically asked that we zip the whole session and unzip it after upload, but want to
/// reference it as a single file and not a bunch of data.json files and separate audio and video
/// files. Even if we need to give it its own type like .ben but know that is a field session
/// zipped for player."
/// </para>
/// <para>
/// Nothing is unpacked. The bundle stores its members uncompressed, so each recording is served as
/// a byte range of the one file — one copy on disk, and the thing being referenced is the thing
/// that is there. See <see cref="BenBundle"/>.
/// </para>
/// <para>
/// <b>The two-phase door stays.</b> The approved 1.0.2 build sends a document and then a file per
/// recording, and will go on doing so on every phone it is installed on long after 1.0.3 ships.
/// Both shapes are first-class; neither is a migration of the other.
/// </para>
/// </remarks>
public sealed partial class FieldSessionUploadController
{
    /// <summary>
    /// Takes one <c>.ben</c> and makes a session of it.
    /// </summary>
    [HttpPost("bundle")]
    [Consumes("multipart/form-data")]
    [DisableRequestSizeLimit]
    public async Task<ActionResult<FieldSessionRecord>> SubmitBundle(
        IFormFile file, [FromForm] Guid deviceSessionId,
        [FromForm] Guid? investigationId,
        [FromForm] Guid? recordedByAppUserId, [FromForm] string? recordedByName,
        CancellationToken ct)
    {
        var userId = GetCurrentUserId();
        if (userId == Guid.Empty) return Unauthorized();
        if (file is null || file.Length == 0) return BadRequest("That session file is empty.");
        if (deviceSessionId == Guid.Empty)
            return BadRequest("The session is missing its own identifier.");

        await using var db = await _db.CreateDbContextAsync(ct);

        Guid organizationId;
        if (investigationId is Guid target && target != Guid.Empty)
        {
            var investigation = await db.Investigations
                .FirstOrDefaultAsync(i => i.Id == target, ct);
            if (investigation is null) return NotFound();
            // Same answer as absent: whether somebody else's investigation exists is not a thing
            // to let an outsider probe for.
            if (!await MayWriteAsync(db, target, userId, ct)) return NotFound();
            organizationId = investigation.OrganizationId;
        }
        else
        {
            investigationId = null;
            organizationId = Guid.Empty;
        }

        // Asked before a gigabyte is written rather than after: being refused for space having
        // already uploaded the night is the version of this that wastes somebody's evening.
        if (organizationId == Guid.Empty
            && await AccountStorageGuard.WhyCannotStoreAsync(db, userId, file.Length, ct) is { } full)
        {
            return BadRequest(full);
        }

        var storedName = $"{Guid.NewGuid()}{BenBundle.FileExtension}";
        var storagePath = organizationId == Guid.Empty
            ? _fileStorage.UserFilePath(userId, $"field-sessions/{storedName}")
            : _fileStorage.OrgFilePath(organizationId, $"field-sessions/{storedName}");

        // Streamed to storage before it is read. A session is gigabytes and must never be held in
        // memory to be checked — and the check itself needs to seek, which a request body cannot.
        await using (var incoming = file.OpenReadStream())
        {
            await _fileStorage.WriteAsync(storagePath, incoming, ct);
        }

        BenBundle bundle;
        string documentText;
        DeviceDataSummary summary;
        try
        {
            bundle = await _bundles.IndexAsync(storagePath, ct);

            if (bundle.Document is not { } documentEntry)
            {
                throw new BenBundleFormatException(
                    "That session file has no data.json in it, so there is nothing to describe what was recorded.");
            }

            await using var document = await _bundles.OpenEntryAsync(
                storagePath, BenBundle.DocumentEntryPath, ct);
            using var reader = new StreamReader(document!);
            documentText = await reader.ReadToEndAsync(ct);

            summary = DeviceDataSummary.Read(documentText);

            // The same two refusals the document door has kept since it existed. A session with
            // no readings recorded nothing; one whose numbers no phone could have produced is not
            // a session a Field Kit wrote.
            if (summary.ReadingCount == 0)
            {
                throw new BenBundleFormatException(
                    "This session has no readings. Nothing was recorded, so there is nothing to upload.");
            }
            if (FieldSessionDocumentGuard.Refusal(documentText) is { } implausible)
            {
                throw new BenBundleFormatException(
                    "That doesn't look like a session a Field Kit recorded: " + implausible);
            }

            // The seal, when the phone wrote one, is read for shape only: small, and JSON. What it
            // promises — that every member is still what was sealed — is the next phone's to check
            // when the bundle is handed back; the server keeps it exactly as sent. It is not a
            // recording, and handing it to the file guard as one refused every sealed upload.
            if (bundle.Seal is { } sealEntry)
            {
                if (sealEntry.Length > BenBundle.MaxSealBytes)
                {
                    throw new BenBundleFormatException(
                        "That session file's seal is far larger than a seal can be, so the file isn't trusted.");
                }
                await using var sealStream = await _bundles.OpenEntryAsync(storagePath, BenBundle.SealEntryPath, ct);
                using var _ = await System.Text.Json.JsonDocument.ParseAsync(sealStream!, cancellationToken: ct);
            }

            // Every recording inside is held to the same rule as one sent on its own: the right
            // kind of file, the right size, and bytes that actually are what the name claims. A
            // bundle is a container somebody else can write, so the door cannot be softer just
            // because everything arrived at once.
            var checkedSoFar = 0;
            foreach (var entry in bundle.Recordings)
            {
                var header = new byte[FieldSessionFileGuard.HeaderBytes];
                await using var member = await _bundles.OpenEntryAsync(storagePath, entry.Path, ct);
                var read = await member!.ReadAsync(header, ct);

                if (FieldSessionFileGuard.Refusal(
                        entry.Path, contentType: null, entry.Length,
                        header.AsSpan(0, read), checkedSoFar) is { } badFile)
                {
                    throw new BenBundleFormatException(
                        $"That session file can't be taken: {badFile}");
                }
                checkedSoFar++;
            }
        }
        catch (Exception ex) when (ex is BenBundleFormatException
                                      or System.Text.Json.JsonException
                                      or InvalidOperationException)
        {
            // Nothing half-accepted is left behind: the bytes go back out of storage, and the
            // person is told in a sentence rather than left with a session that will not open.
            _bundles.Forget(storagePath);
            try { await _fileStorage.DeleteAsync(storagePath, ct); }
            catch (Exception cleanup)
            {
                _log.LogWarning(cleanup,
                    "Could not remove the refused session bundle at {Path}.", storagePath);
            }
            return BadRequest(ex is BenBundleFormatException
                ? ex.Message
                : "That doesn't look like a session file: " + ex.Message);
        }

        var uploadFile = new UploadFile
        {
            Id = Guid.NewGuid(), UploadFileTypeId = EvidenceFileTypeId, AppUserId = userId,
            FileName = SafeBundleName(file.FileName, deviceSessionId),
            StoredFileName = storedName, ContentType = BenBundle.ContentType,
            FileSize = file.Length, StoragePath = storagePath, IsPublic = false,
            DateCreated = DateTime.UtcNow, CreatedByAppUserId = userId,
        };
        db.UploadFiles.Add(uploadFile);

        // A retried upload finds its own row, scoped to the sender: two copies of one night is
        // worse than none, because nobody can tell which is which.
        var session = await db.FieldSessionUploads
            .Include(s => s.Files)
            .FirstOrDefaultAsync(s => s.SubmittedByAppUserId == userId
                                   && s.DeviceSessionId == deviceSessionId, ct);

        string? replacedPath = null;
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

            // A resent bundle replaces the one before it wholesale — it describes the same night
            // and may carry more of it. The old rows go now; the old FILE goes only once the new
            // row is safely saved, below.
            if (session.IsBundle)
            {
                replacedPath = await db.UploadFiles.AsNoTracking()
                    .Where(f => f.Id == session.DocumentUploadFileId)
                    .Select(f => f.StoragePath)
                    .FirstOrDefaultAsync(ct);
            }
            db.FieldSessionUploadFiles.RemoveRange(session.Files);
        }

        await ApplyRecordedByAsync(db, session, userId, recordedByAppUserId, recordedByName, ct);

        session.IsBundle = true;
        session.InvestigationId = investigationId;
        session.DocumentUploadFileId = uploadFile.Id;
        session.DeviceModel = summary.DeviceModel;
        session.LocationLabel = summary.LocationLabel;
        session.StartedAt = summary.StartedAt;
        session.EndedAt = summary.EndedAt;
        session.ReadingCount = summary.ReadingCount;
        session.MarkerCount = summary.MarkerCount;

        var fix = FirstFix(documentText);
        session.Latitude = fix?.Latitude;
        session.Longitude = fix?.Longitude;
        session.PositionResolved = true;

        foreach (var entry in bundle.Recordings)
        {
            db.FieldSessionUploadFiles.Add(new FieldSessionUploadFile
            {
                Id = Guid.NewGuid(),
                FieldSessionUploadId = session.Id,
                // No file of its own: the bytes are inside the session's single .ben.
                UploadFileId = null,
                BundleEntryPath = entry.Path,
                RelativePath = entry.Path,
                ContentType = FieldSessionFileGuard.ContentTypeFor(entry.Path),
                FileSize = entry.Length,
                DateCreated = DateTime.UtcNow,
                CreatedByAppUserId = userId,
            });
        }

        // Ben's rule, 2026-08-24: what comes off an upload is kept beside it. A .ben is a
        // container, so what is recorded here is what the container IS — when it arrived, how
        // long the session runs, and that it is a session file rather than a recording.
        //
        // The members keep whatever EXIF the phone wrote, and that is a gap — but not the one it
        // first looks like. Ben, 2026-09-16: "the app records the location and even direction they
        // are pointing... isn't that the same thing the EXIF contains?" It is. A field session's
        // position and heading are recorded ON PURPOSE and drawn on a map; nothing about them is
        // secret from the person who recorded them, and MediaIngestService keeps them in
        // UploadFileMetadata for every upload regardless.
        //
        // What stripping is actually for is the SERVE boundary: "the group keeps the facts, the
        // served file does not carry them". A share link can be made with positions withheld, and
        // SharedSessionDocument nulls every coordinate in the document to honour that. A JPEG in
        // that same share whose EXIF still carries a fix defeats it — the viewer opens the photo
        // and reads the address the document refused to give. The per-file door closes this by
        // serving a sanitized copy; a bundle currently would not.
        //
        // The fix belongs on the phone, when it builds the bundle: the original stays on the
        // device, so nothing is lost, and the position stays in data.json where the withholding
        // can reach it. Part of 1.0.3, with the rest of the sending side.
        db.UploadFileMetadata.Add(new UploadFileMetadata
        {
            Id = Guid.NewGuid(),
            UploadFileId = uploadFile.Id,
            MediaKind = "FieldSession",
            CapturedAtUtc = summary.StartedAt,
            DurationSeconds = summary.EndedAt is { } ended
                ? (ended - summary.StartedAt).TotalSeconds
                : null,
            ExtractedAtUtc = DateTime.UtcNow,
        });

        await db.SaveChangesAsync(ct);
        await db.Entry(session).Collection(s => s.Files).LoadAsync(ct);

        if (replacedPath is { Length: > 0 } old && old != storagePath)
        {
            _bundles.Forget(old);
            try { await _fileStorage.DeleteAsync(old, ct); }
            catch (Exception cleanup)
            {
                // The row is already right; a file left behind is a tidying problem the orphan
                // sweep can find, not a reason to fail an upload that succeeded.
                _log.LogWarning(cleanup,
                    "Could not remove the replaced session bundle at {Path}.", old);
            }
        }

        _log.LogInformation(
            "Field session {DeviceSessionId} arrived as one bundle for investigation "
            + "{InvestigationId} ({Readings} readings, {Markers} marked, {Files} recordings).",
            deviceSessionId, investigationId, summary.ReadingCount, summary.MarkerCount,
            bundle.Recordings.Count());

        return Ok(ToRecord(session));
    }

    /// <summary>
    /// The whole session as it was sent, for a phone that wants to play it back.
    /// </summary>
    /// <remarks>
    /// Ben, 2026-09-16: "We will also need to be able to pull .ben files back to the phone for it
    /// to play. So someone else can share their .ben file with another person on the iphone and
    /// the other person can view it like they had recorded it themselves." The bundle IS the
    /// portable session, so handing it back is handing back the file.
    /// </remarks>
    [HttpGet("{sessionId:guid}/bundle")]
    public async Task<IActionResult> GetBundle(Guid sessionId, CancellationToken ct)
    {
        var userId = GetCurrentUserId();
        if (userId == Guid.Empty) return Unauthorized();

        await using var db = await _db.CreateDbContextAsync(ct);
        var session = await db.FieldSessionUploads.AsNoTracking()
            .Include(s => s.DocumentUploadFile)
            .FirstOrDefaultAsync(s => s.Id == sessionId, ct);
        if (session is null) return NotFound();
        if (!await MayReadAsync(db, session, userId, ct)) return NotFound();

        if (!session.IsBundle)
        {
            // Truthful rather than silently handing back something that is not a bundle. Sessions
            // sent by 1.0.2 are a document plus loose recordings, and re-zipping one on demand is
            // a job for whoever decides it is worth doing.
            return BadRequest(
                "This session was sent before session files existed, so there is no single file to hand back.");
        }
        if (session.DocumentUploadFile?.StoragePath is not { Length: > 0 } path
            || !_fileStorage.Exists(path))
        {
            return NotFound();
        }

        var stream = await _bundles.OpenBundleAsync(path, ct);
        return File(stream, BenBundle.ContentType,
                    session.DocumentUploadFile.FileName, enableRangeProcessing: true);
    }

    /// <summary>
    /// A name a person will recognise in their downloads, and that nothing can be hurt by.
    /// </summary>
    private static string SafeBundleName(string? sent, Guid deviceSessionId)
    {
        var fallback = $"session-{deviceSessionId}{BenBundle.FileExtension}";
        if (string.IsNullOrWhiteSpace(sent)) return fallback;

        // Only the last segment, and only characters that cannot be read as a path anywhere. The
        // name a client sends is never a place to write to, but it does end up in a
        // Content-Disposition header and in somebody's Downloads folder.
        var name = Path.GetFileName(sent.Replace('\\', '/'));
        if (string.IsNullOrWhiteSpace(name) || name is "." or "..") return fallback;
        if (name.Any(c => Path.GetInvalidFileNameChars().Contains(c))) return fallback;
        return name.EndsWith(BenBundle.FileExtension, StringComparison.OrdinalIgnoreCase)
            ? name
            : name + BenBundle.FileExtension;
    }
}
