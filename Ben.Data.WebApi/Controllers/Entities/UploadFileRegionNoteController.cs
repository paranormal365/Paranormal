using AutoMapper;
using Ben.Data.Source.Context;
using Ben.Data.Source.Entities;
using Ben.Service.Models.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Ben.Data.WebApi.Controllers.Entities;

/// <summary>CRUD for region notes attached to an UploadFile.</summary>
[ApiController]
[Route("api/upload-files/{fileId:guid}/region-notes")]
[Authorize]
public sealed class UploadFileRegionNoteController : ControllerBase
{
    private readonly IDbContextFactory<BenDataContext> _dbContextFactory;
    private readonly IMapper _mapper;

    public UploadFileRegionNoteController(IDbContextFactory<BenDataContext> dbContextFactory, IMapper mapper)
    {
        _dbContextFactory = dbContextFactory;
        _mapper = mapper;
    }

    [HttpGet]
    public async Task<ActionResult<IEnumerable<UploadFileRegionNoteRecord>>> GetAll(
        Guid fileId, CancellationToken ct)
    {
        await using var db = await _dbContextFactory.CreateDbContextAsync(ct);
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
        await using var db = await _dbContextFactory.CreateDbContextAsync(ct);
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
        var userId = GetCurrentUserId();
        if (userId == Guid.Empty) return Unauthorized();

        await using var db = await _dbContextFactory.CreateDbContextAsync(ct);
        if (!await db.UploadFiles.AnyAsync(f => f.Id == fileId, ct))
            return NotFound("File not found.");

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
        return CreatedAtAction(nameof(GetById), new { fileId, noteId = entity.Id },
            _mapper.Map<UploadFileRegionNoteRecord>(entity));
    }

    [HttpPut("{noteId:guid}")]
    public async Task<ActionResult<UploadFileRegionNoteRecord>> Update(
        Guid fileId, Guid noteId,
        [FromBody] UpdateRegionNoteRequest request,
        CancellationToken ct)
    {
        var userId = GetCurrentUserId();
        await using var db = await _dbContextFactory.CreateDbContextAsync(ct);
        var entity = await db.UploadFileRegionNotes
            .FirstOrDefaultAsync(n => n.Id == noteId && n.UploadFileId == fileId, ct);
        if (entity is null) return NotFound();

        entity.TimeOffset         = request.TimeOffset;
        entity.NoteHtml           = request.NoteHtml;
        entity.IsPublic           = request.IsPublic;
        entity.DateUpdated        = DateTime.UtcNow;
        entity.UpdatedByAppUserId = userId == Guid.Empty ? null : userId;

        await db.SaveChangesAsync(ct);
        return Ok(_mapper.Map<UploadFileRegionNoteRecord>(entity));
    }

    [HttpDelete("{noteId:guid}")]
    public async Task<IActionResult> Delete(Guid fileId, Guid noteId, CancellationToken ct)
    {
        await using var db = await _dbContextFactory.CreateDbContextAsync(ct);
        var entity = await db.UploadFileRegionNotes
            .FirstOrDefaultAsync(n => n.Id == noteId && n.UploadFileId == fileId, ct);
        if (entity is null) return NotFound();
        db.UploadFileRegionNotes.Remove(entity);
        await db.SaveChangesAsync(ct);
        return NoContent();
    }

    private Guid GetCurrentUserId()
    {
        var appUserIdClaim = User.FindFirst("app_user_id")?.Value;
        if (Guid.TryParse(appUserIdClaim, out var id)) return id;
        var sub = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value
               ?? User.FindFirst("sub")?.Value;
        return Guid.TryParse(sub, out var subId) ? subId : Guid.Empty;
    }
}
