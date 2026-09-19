using Ben.Data.Common.Interfaces;
using Ben.Data.Source.Context;
using Ben.Data.WebApi.Controllers.Entities;
using Ben.Data.WebApi.Services.Events;
using Ben.Service.Models.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Ben.Data.WebApi.Controllers.Public;

/// <summary>
/// An event's files, for whoever may have each one (item 235 phase 11).
/// </summary>
/// <remarks>
/// <b>Asked every time, on the server.</b> The list and the download ask the same question
/// (<see cref="EventFileAudiences"/>), so a link copied out of a guest's page into a group chat
/// downloads nothing for somebody who is not coming. Every file is served as an attachment, never
/// inline, whatever it claims to be.
/// </remarks>
[ApiController]
[AllowAnonymous]
[Route("api/public/hosted-events/{eventId:guid}/files")]
public sealed class PublicHostedEventFileController : BenControllerBase
{
    private readonly IDbContextFactory<BenDataContext> _dbFactory;
    private readonly Services.Access.HostedEventAccess _access;
    private readonly IFileStorageService _storage;
    private readonly Services.IMediaIngestService _ingest;

    public PublicHostedEventFileController(
        IDbContextFactory<BenDataContext> dbFactory, Services.Access.HostedEventAccess access,
        IFileStorageService storage, Services.IMediaIngestService ingest)
    { _dbFactory = dbFactory; _access = access; _storage = storage; _ingest = ingest; }

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<HostedEventFileRecord>>> Get(Guid eventId, CancellationToken ct)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var hosted = await db.HostedEvents.AsNoTracking().FirstOrDefaultAsync(e => e.Id == eventId, ct);
        if (hosted is null) return NotFound();

        var widest = await EventFileAudiences.WidestAsync(db, _access, hosted, GetCurrentUserId(), ct);
        if (widest is null) return NotFound();

        return Ok(await HostedEventFileController.ListAsync(db, eventId, widest, ct));
    }

    [HttpGet("{fileId:guid}/download")]
    public async Task<IActionResult> Download(Guid eventId, Guid fileId, CancellationToken ct)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var row = await db.HostedEventFiles.AsNoTracking()
            .Include(f => f.HostedEvent)
            .Include(f => f.UploadFile)
            .FirstOrDefaultAsync(f => f.Id == fileId && f.HostedEventId == eventId, ct);
        if (row is null) return NotFound();

        var viewer = GetCurrentUserId();
        var widest = await EventFileAudiences.WidestAsync(db, _access, row.HostedEvent, viewer, ct);
        if (!EventFileAudiences.Reaches(row.Audience, widest))
            return viewer == Guid.Empty ? Unauthorized() : Forbid();

        // The stripped copy when there is one: the original still carries what the ingest took off.
        if (row.UploadFile.StoragePath is not { } stored) return NotFound();
        var path = _ingest.ServingPathFor(stored);
        if (!_storage.Exists(path)) return NotFound();

        var stream = await _storage.OpenReadAsync(path, ct);
        // enableRangeProcessing: a player asks for the piece it needs; without it Safari will not start at all and nothing can seek (2026-09-17).
        return File(stream, "application/octet-stream", row.UploadFile.FileName, enableRangeProcessing: true);
    }
}
