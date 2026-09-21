using Ben.Data.Common.Interfaces;
using Ben.Data.Source.Context;
using Ben.Data.WebApi.Services;
using Ben.Service.Models.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Ben.Data.WebApi.Controllers.Public;

/// <summary>
/// The evidence people added to a public place, and its bytes (item 250).
/// </summary>
/// <remarks>
/// <para>Anonymous, because the whole point of a public place page is that somebody who has never
/// been here can read what has been found there. Every answer goes through
/// <see cref="PlaceEvidencePublication"/>, so what is listed and what is served are the same
/// set.</para>
/// </remarks>
[ApiController]
[Route("api/public/places/{placeId:guid}/evidence")]
[AllowAnonymous]
public sealed class PublicPlaceEvidenceController : ControllerBase
{
    private readonly IDbContextFactory<BenDataContext> _db;
    private readonly IFileStorageService _fileStorage;
    private readonly IMediaIngestService _mediaIngest;

    public PublicPlaceEvidenceController(
        IDbContextFactory<BenDataContext> db, IFileStorageService fileStorage,
        IMediaIngestService mediaIngest)
    { _db = db; _fileStorage = fileStorage; _mediaIngest = mediaIngest; }

    /// <summary>What has been added to this place and may be shown, newest first.</summary>
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<PlaceAddedEvidenceRow>>> List(
        Guid placeId, CancellationToken ct)
    {
        await using var db = await _db.CreateDbContextAsync(ct);

        var rows = await PlaceEvidencePublication.Showable(db, placeId)
            .Select(e => new PlaceAddedEvidenceRow(
                e.Id,
                e.UploadFile!.FileName,
                e.UploadFile.ContentType,
                e.Caption,
                // Named, because anonymous evidence is worth less than evidence somebody stands
                // behind — and the person chose to put their name to it by signing in to add it.
                e.AddedByAppUser!.DisplayName ?? "Someone",
                e.AddedByAppUser.Handle,
                e.DateCreated))
            .ToListAsync(ct);

        return Ok(rows);
    }

    /// <summary>One piece of evidence, as bytes.</summary>
    /// <remarks>
    /// Range processing is on for the same reason the archive has it: without it Safari will not
    /// start a recording at all, and nothing can seek.
    /// </remarks>
    [HttpGet("{evidenceId:guid}/file")]
    public async Task<IActionResult> File(Guid placeId, Guid evidenceId, CancellationToken ct)
    {
        await using var db = await _db.CreateDbContextAsync(ct);

        if (!await PlaceEvidencePublication.MayServeAsync(db, placeId, evidenceId, ct))
            return NotFound();

        var file = await db.PlaceEvidence.AsNoTracking()
            .Where(e => e.Id == evidenceId)
            .Select(e => e.UploadFile!)
            .FirstOrDefaultAsync(ct);
        if (file is null) return NotFound();

        if (!string.IsNullOrEmpty(file.StoragePath))
        {
            var servingPath = _mediaIngest.ServingPathFor(file.StoragePath);
            var stream = await _fileStorage.OpenReadAsync(servingPath, ct);
            return base.File(stream, file.ContentType, file.FileName, enableRangeProcessing: true);
        }

        return NotFound();
    }
}
