using Ben.Data.Common.Enums;
using Ben.Data.Common.Interfaces;
using Ben.Data.Source.Context;
using Ben.Data.Source.Entities;
using Ben.Data.WebApi.SeedData;
using Ben.Data.WebApi.Services;
using Ben.Data.WebApi.Services.Billing;
using Ben.Data.WebApi.Services.Feed;
using Ben.Service.Models.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Ben.Data.WebApi.Controllers.Entities;

/// <summary>
/// Adding a file to a public place, with no investigation behind it (item 250).
/// </summary>
/// <remarks>
/// <para>Ben, 2026-09-21: <i>"people don't have to have a dedicated investigation to add files to
/// the public location."</i></para>
///
/// <para><b>The door is open to anybody signed in</b>, which Ben chose deliberately. The person
/// with the photograph of Cragfont is rarely a member of a paranormal group, and members-only
/// would shut out exactly who a public place page exists for. An account is still required,
/// because evidence nobody stands behind is worth nothing to read and impossible to moderate.</para>
///
/// <para><b>An open door on a public page is a moderation surface</b>, so it goes through the
/// screener and the held pile the feed already has rather than a second one. The row starts
/// Pending and only a verdict moves it: a screener that throws grows a queue, and can never
/// publish something nobody looked at.</para>
///
/// <para><b>Public locations only.</b> A private residence is somebody's home, and the whole
/// safety story of the archive is that its bytes can only ever be attached to a place that is not
/// one. Checked here, and re-asked wherever a row is read.</para>
/// </remarks>
[ApiController]
[Route("api/places/{placeId:guid}/evidence")]
[Authorize]
public sealed class PlaceEvidenceController : BenControllerBase
{
    private readonly IDbContextFactory<BenDataContext> _db;
    private readonly IFileStorageService _fileStorage;
    private readonly IMediaIngestService _mediaIngest;
    private readonly IFeedMediaScreener _screener;
    private readonly ILogger<PlaceEvidenceController> _log;

    public PlaceEvidenceController(
        IDbContextFactory<BenDataContext> db,
        IFileStorageService fileStorage,
        IMediaIngestService mediaIngest,
        IFeedMediaScreener screener,
        ILogger<PlaceEvidenceController> log)
    {
        _db = db; _fileStorage = fileStorage; _mediaIngest = mediaIngest;
        _screener = screener; _log = log;
    }

    /// <summary>Adds one file to this place's evidence.</summary>
    /// <remarks>
    /// <para>The answer says what state it landed in, because "we have it and a person will look
    /// at it" and "it is on the page now" are different things and the uploader has to be told
    /// which one happened. A page that says "added" over something held is a page that teaches
    /// people to upload it again.</para>
    /// </remarks>
    [HttpPost]
    [Consumes("multipart/form-data")]
    [DisableRequestSizeLimit]
    public async Task<ActionResult<PlaceEvidenceAdded>> Add(
        Guid placeId, IFormFile file, [FromForm] string? caption, CancellationToken ct,
        [FromForm] PlaceMediaKind kind = PlaceMediaKind.Evidence)
    {
        var userId = GetCurrentUserId();
        if (userId == Guid.Empty) return Unauthorized();
        if (file is null || file.Length == 0) return BadRequest("That file is empty.");

        await using var db = await _db.CreateDbContextAsync(ct);

        var place = await db.Places.AsNoTracking()
            .Where(p => p.Id == placeId)
            .Select(p => new { p.Id, p.Kind, p.Name })
            .FirstOrDefaultAsync(ct);
        if (place is null) return NotFound();

        // Said in words rather than refused with a status: somebody looking at a place page has no
        // way to know what kind of place the row behind it is, and "not found" would read as a
        // broken site.
        if (place.Kind != PlaceKind.PublicLocation)
        {
            return BadRequest(
                "Evidence can only be added to a public location. This place is somebody's home, "
              + "and work there belongs to the group that was invited.");
        }

        // The free account's allowance, asked BEFORE a byte is written, the way the field-session
        // door asks it. This was missed when the door was built (C1): the guard's own comment
        // anticipated "a second kind of personal upload", place evidence is that second kind, and
        // it arrived without counting against anything. A limit noticed afterwards is not a limit.
        if (await AccountStorageGuard.WhyCannotStoreAsync(db, userId, file.Length, ct) is { } full)
            return StatusCode(StatusCodes.Status413PayloadTooLarge, full);

        var storedName = $"{Guid.NewGuid()}{Path.GetExtension(file.FileName)}";
        // Stored under the PERSON, not the place. The file belongs to whoever added it — that is
        // what UploadFile.AppUserId says, it is whose allowance it spends, and it is who may take
        // it back. A place is somewhere evidence is ABOUT, not an owner. Namespaced so "what did
        // this account contribute to places" is still a directory rather than only a query.
        var storagePath = _fileStorage.UserFilePath(userId, $"place-evidence/{placeId}/{storedName}");
        var uploadFileId = Guid.NewGuid();

        IngestedMedia ingested;
        try
        {
            // stripAudioVideo: TRUE, which the feed and a group's own uploads leave to a plan
            // setting. There is no group here whose settings could decide it — the contributor is
            // a person, often in no group at all — and the destination is a page any stranger can
            // read. Images are stripped regardless; this covers the recording that carries a GPS
            // fix and a device name from somebody's pocket. The conservative default is the only
            // defensible one when nobody is in a position to choose.
            ingested = await _mediaIngest.IngestAsync(
                file, storagePath, uploadFileId, ct, stripAudioVideo: true);
        }
        catch (UnreadableImageException ex)
        {
            return BadRequest(ex.Message);
        }

        db.UploadFiles.Add(new UploadFile
        {
            Id = uploadFileId,
            UploadFileTypeId = UploadFileTypeSeeder.FeedMediaFileTypeId,
            AppUserId = userId,
            FileName = file.FileName,
            StoredFileName = storedName,
            ContentType = ingested.ServedContentType,
            FileSize = ingested.ServedFileSize,
            StoragePath = storagePath,
            // False deliberately, exactly as the feed does it: the place's own endpoint decides
            // who may see this and refuses anything unscreened. Public here would route around it.
            IsPublic = false,
            DateCreated = DateTime.UtcNow,
            CreatedByAppUserId = userId,
        });
        db.UploadFileMetadata.Add(ingested.Metadata);

        var row = new PlaceEvidence
        {
            Id = Guid.NewGuid(),
            PlaceId = placeId,
            UploadFileId = uploadFileId,
            AddedByAppUserId = userId,
            Caption = string.IsNullOrWhiteSpace(caption) ? null : caption.Trim(),
            // Evidence unless somebody says otherwise. A picture OF the building is the deliberate
            // act; offering something as evidence is what the door is for.
            MediaKind = kind,
            // Fail-closed by construction. Only a verdict moves it off Pending.
            ReviewState = FeedMediaReviewState.Pending,
            DateCreated = DateTime.UtcNow,
            CreatedByAppUserId = userId,
        };

        try
        {
            var verdict = await _screener.ScreenAsync(storagePath, file.ContentType, ct);
            row.ReviewState = verdict.State;
            row.ReviewNote = verdict.Reason;
            row.ScreenerScore = verdict.Score;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _log.LogWarning(ex, "Screening failed for evidence added to place {PlaceId}.", placeId);
            row.ReviewNote = "Screening failed; held for a moderator.";
        }

        db.PlaceEvidence.Add(row);
        await db.SaveChangesAsync(ct);

        return Ok(new PlaceEvidenceAdded(
            row.Id,
            row.ReviewState == FeedMediaReviewState.Approved,
            row.ReviewState == FeedMediaReviewState.Approved
                ? "Added. It is on the page now."
                : "Thank you — somebody will look at this before it appears on the page."));
    }

    /// <summary>Takes back something this account added.</summary>
    /// <remarks>
    /// Only the person who added it, and only their own row. A public page anybody may add to has
    /// to be a page they can also change their mind about; moderation is a separate power with a
    /// separate screen.
    /// </remarks>
    [HttpDelete("{evidenceId:guid}")]
    public async Task<ActionResult<bool>> Remove(Guid placeId, Guid evidenceId, CancellationToken ct)
    {
        var userId = GetCurrentUserId();
        if (userId == Guid.Empty) return Unauthorized();

        await using var db = await _db.CreateDbContextAsync(ct);

        var row = await db.PlaceEvidence
            .FirstOrDefaultAsync(e => e.Id == evidenceId && e.PlaceId == placeId, ct);
        if (row is null) return NotFound();
        if (row.AddedByAppUserId != userId) return Forbid();

        db.PlaceEvidence.Remove(row);
        await db.SaveChangesAsync(ct);
        return Ok(true);
    }
}
