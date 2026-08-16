using AutoMapper;
using Ben.Data.Source.Context;
using Ben.Data.Source.Entities;
using Ben.Service.Models.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Ben.Data.WebApi.Controllers.Entities;

/// <summary>
/// CRUD for region notes attached to an UploadFile.
/// </summary>
/// <remarks>
/// Previously none of these five actions tied the caller to the file at all — any authenticated
/// user could read, add, edit, or delete region notes on any file, and <c>Delete</c> didn't even
/// resolve the caller's identity for authorization (only for the audit call). Reads and
/// <c>Create</c> now require <see cref="FileAudienceAccess.CanViewFileAsync"/> (the same
/// visibility check used elsewhere in the app); <c>Update</c>/<c>Delete</c> additionally require
/// the caller be the note's own author, the file's owner, or SuperAdmin.
/// </remarks>
[ApiController]
[Route("api/upload-files/{fileId:guid}/region-notes")]
[Authorize]
public sealed class UploadFileRegionNoteController : BenControllerBase
{
    private readonly IDbContextFactory<BenDataContext> _dbContextFactory;
    private readonly IMapper _mapper;
    private readonly IAuditLogService _auditLog;

    public UploadFileRegionNoteController(IDbContextFactory<BenDataContext> dbContextFactory, IMapper mapper, IAuditLogService auditLog)
    {
        _dbContextFactory = dbContextFactory;
        _mapper = mapper;
        _auditLog = auditLog;
    }

    [HttpGet]
    public async Task<ActionResult<IEnumerable<UploadFileRegionNoteRecord>>> GetAll(
        Guid fileId, CancellationToken ct)
    {
        var userId = GetCurrentUserIdOrThrow();
        await using var db = await _dbContextFactory.CreateDbContextAsync(ct);
        if (!await FileAudienceAccess.CanViewFileAsync(db, fileId, userId, ct)) return Forbid();

        var notes = await db.UploadFileRegionNotes
            .AsNoTracking()
            .Where(n => n.UploadFileId == fileId)
            .OrderBy(n => n.RegionStart).ThenBy(n => n.TimeOffset).ThenBy(n => n.DateCreated)
            .ToListAsync(ct);
        return Ok(_mapper.Map<IEnumerable<UploadFileRegionNoteRecord>>(notes));
    }

    [HttpGet("{noteId:guid}")]
    public async Task<ActionResult<UploadFileRegionNoteRecord>> GetById(
        Guid fileId, Guid noteId, CancellationToken ct)
    {
        var userId = GetCurrentUserIdOrThrow();
        await using var db = await _dbContextFactory.CreateDbContextAsync(ct);
        if (!await FileAudienceAccess.CanViewFileAsync(db, fileId, userId, ct)) return Forbid();

        var note = await db.UploadFileRegionNotes.AsNoTracking()
            .FirstOrDefaultAsync(n => n.Id == noteId && n.UploadFileId == fileId, ct);
        if (note is null) return NotFound();
        return Ok(_mapper.Map<UploadFileRegionNoteRecord>(note));
    }

    [HttpPost]
    public async Task<ActionResult<UploadFileRegionNoteRecord>> Create(
        Guid fileId,
        [FromBody] CreateRegionNoteRequest request,
        CancellationToken ct)
    {
        var userId = GetCurrentUserIdOrThrow();
        await using var db = await _dbContextFactory.CreateDbContextAsync(ct);
        if (!await db.UploadFiles.AnyAsync(f => f.Id == fileId, ct))
            return NotFound("File not found.");
        if (!await FileAudienceAccess.CanViewFileAsync(db, fileId, userId, ct)) return Forbid();

        var entity = new UploadFileRegionNote
        {
            Id                 = Guid.NewGuid(),
            UploadFileId       = fileId,
            RegionStart        = request.RegionStart,
            RegionEnd          = request.RegionEnd,
            RegionLabel        = request.RegionLabel,
            TimeOffset         = request.TimeOffset,
            NoteHtml           = request.NoteHtml,
            IsPublic           = request.IsPublic,
            DateCreated        = DateTime.UtcNow,
            CreatedByAppUserId = userId,
        };
        db.UploadFileRegionNotes.Add(entity);
        await db.SaveChangesAsync(ct);
        _ = TryAuditAsync(_auditLog.LogCreateAsync(nameof(UploadFileRegionNote), entity.Id, entity, userId, AppSources.WebApi));
        return CreatedAtAction(nameof(GetById), new { fileId, noteId = entity.Id },
            _mapper.Map<UploadFileRegionNoteRecord>(entity));
    }

    [HttpPut("{noteId:guid}")]
    public async Task<ActionResult<UploadFileRegionNoteRecord>> Update(
        Guid fileId, Guid noteId,
        [FromBody] UpdateRegionNoteRequest request,
        CancellationToken ct)
    {
        var userId = GetCurrentUserIdOrThrow();
        await using var db = await _dbContextFactory.CreateDbContextAsync(ct);
        var before = await db.UploadFileRegionNotes.AsNoTracking()
            .FirstOrDefaultAsync(n => n.Id == noteId && n.UploadFileId == fileId, ct);
        if (before is null) return NotFound();
        if (!await CanModifyNoteAsync(db, fileId, before.CreatedByAppUserId, userId, ct)) return Forbid();

        var entity = await db.UploadFileRegionNotes
            .FirstOrDefaultAsync(n => n.Id == noteId && n.UploadFileId == fileId, ct);

        entity!.TimeOffset         = request.TimeOffset;
        entity.NoteHtml           = request.NoteHtml;
        entity.IsPublic           = request.IsPublic;
        entity.DateUpdated        = DateTime.UtcNow;
        entity.UpdatedByAppUserId = userId;

        await db.SaveChangesAsync(ct);
        _ = TryAuditAsync(_auditLog.LogUpdateAsync(nameof(UploadFileRegionNote), noteId, before, entity, userId, AppSources.WebApi));
        return Ok(_mapper.Map<UploadFileRegionNoteRecord>(entity));
    }

    [HttpDelete("{noteId:guid}")]
    public async Task<IActionResult> Delete(Guid fileId, Guid noteId, CancellationToken ct)
    {
        var userId = GetCurrentUserIdOrThrow();
        await using var db = await _dbContextFactory.CreateDbContextAsync(ct);
        var entity = await db.UploadFileRegionNotes
            .FirstOrDefaultAsync(n => n.Id == noteId && n.UploadFileId == fileId, ct);
        if (entity is null) return NotFound();
        if (!await CanModifyNoteAsync(db, fileId, entity.CreatedByAppUserId, userId, ct)) return Forbid();

        db.UploadFileRegionNotes.Remove(entity);
        await db.SaveChangesAsync(ct);
        _ = TryAuditAsync(_auditLog.LogDeleteAsync(nameof(UploadFileRegionNote), noteId, entity, userId, AppSources.WebApi));
        return NoContent();
    }

    /// <summary>The note's own author, the file's owner, or SuperAdmin.</summary>
    private async Task<bool> CanModifyNoteAsync(BenDataContext db, Guid fileId, Guid noteAuthorId, Guid userId, CancellationToken ct)
    {
        if (noteAuthorId == userId || User.IsInRole(RoleNames.SuperAdmin)) return true;
        var fileOwnerId = await db.UploadFiles.AsNoTracking()
            .Where(f => f.Id == fileId).Select(f => f.AppUserId).FirstOrDefaultAsync(ct);
        return fileOwnerId == userId;
    }
}
