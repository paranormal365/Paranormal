using Ben.Data.Common.Interfaces;
using Ben.Data.Source.Context;
using Ben.Data.WebApi.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Ben.Data.WebApi.Controllers.Public;

/// <summary>
/// Serves the bytes of a photo, video or audio clip attached to a published field session — the
/// only anonymous route to archive media, and deliberately the narrowest one that can exist.
/// </summary>
/// <remarks>
/// <para><b>Why this had to be written.</b> The archive shipped with a COUNT of approved media and
/// nothing that could serve it: a visitor was told eleven people recorded here and could see none
/// of it. <c>/api/upload-files/{id}/download</c> grants an anonymous caller only files flagged
/// <c>IsPublic</c> or shared to a Public target, and a field-session recording is neither. Exactly
/// the shape found on the case-media slots — the rule said publishable, the pipe said no — and the
/// same answer applies, because the same cheap alternative (<c>IsPublic</c>) is the same
/// permanent, global grant.</para>
///
/// <para><b>The sanitized copy, always.</b> This matters more here than anywhere else in the app.
/// Archive media is photographed at a location by somebody standing in it, so the original very
/// likely carries GPS in its EXIF — and the whole archive is built on places whose exact
/// coordinates are deliberately not published. Serving an original would hand out the precise
/// spot beside a page that shows a vague one. <c>ServingPathFor</c> falls back to the original
/// when nothing was sanitized, so this is a no-op for files with no derivative — which is exactly
/// why the ingest side, not this line, is what keeps the promise.</para>
///
/// <para><b>404, never 403.</b> A refusal that distinguishes "no such session" from "that session
/// exists but its media is held" tells an anonymous caller which ids are real, and tells a
/// contributor's flagger that their flag landed. Both answers are the same answer.</para>
/// </remarks>
[ApiController]
[Route("api/public/field-sessions/{fieldSessionId:guid}/media")]
[AllowAnonymous]
public sealed class PublicArchiveMediaController : ControllerBase
{
    private readonly IDbContextFactory<BenDataContext> _db;
    private readonly IFileStorageService _fileStorage;
    private readonly IMediaIngestService _mediaIngest;

    public PublicArchiveMediaController(
        IDbContextFactory<BenDataContext> db,
        IFileStorageService fileStorage,
        IMediaIngestService mediaIngest)
    {
        _db = db;
        _fileStorage = fileStorage;
        _mediaIngest = mediaIngest;
    }

    /// <summary>What a visitor may currently see for this session — empty while media is held.</summary>
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<ArchiveMediaItem>>> List(
        Guid fieldSessionId, CancellationToken ct)
    {
        await using var db = await _db.CreateDbContextAsync(ct);
        return Ok(await ArchiveMediaPublication.ServableFilesAsync(db, fieldSessionId, ct));
    }

    /// <summary>Streams one approved file's bytes, or 404.</summary>
    /// <remarks>
    /// The publication check runs <b>before</b> the file row is read, so an id that may not be
    /// served never reaches storage — a refusal should not be distinguishable by how long it took,
    /// and it should not touch the disk on the way to saying no.
    /// </remarks>
    [HttpGet("{uploadFileId:guid}")]
    public async Task<IActionResult> Get(Guid fieldSessionId, Guid uploadFileId, CancellationToken ct)
    {
        await using var db = await _db.CreateDbContextAsync(ct);

        if (!await ArchiveMediaPublication.MayServeAsync(db, fieldSessionId, uploadFileId, ct))
            return NotFound();

        var file = await db.UploadFiles.AsNoTracking()
            .FirstOrDefaultAsync(f => f.Id == uploadFileId, ct);
        if (file is null) return NotFound();

        // Same disk-then-blob fallback as UploadFileController.Download: rows predating the
        // storage migration still keep their bytes in the column.
        if (!string.IsNullOrEmpty(file.StoragePath))
        {
            var servingPath = _mediaIngest.ServingPathFor(file.StoragePath);
            var stream = await _fileStorage.OpenReadAsync(servingPath, ct);
            // The row's ContentType already describes the SERVED copy — ingest records the
            // derivative's type, not the original's.
            return File(stream, file.ContentType, file.FileName);
        }

        if (file.FileData is not null)
            return File(file.FileData, file.ContentType, file.FileName);

        return NotFound();
    }
}
