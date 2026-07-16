using AutoMapper;
using Ben.Data.Common.Helpers;
using Ben.Data.Source.Context;
using Ben.Data.Source.Entities;
using Ben.Service.Models.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Ben.Data.WebApi.Controllers.Entities;

[ApiController]
[Route("api/upload-files")]
[Authorize]
public sealed class UploadFileController : ControllerBase
{
    private readonly IDbContextFactory<BenDataContext> _dbContextFactory;
    private readonly IMapper _mapper;

    public UploadFileController(IDbContextFactory<BenDataContext> dbContextFactory, IMapper mapper)
    {
        _dbContextFactory = dbContextFactory;
        _mapper = mapper;
    }

    [HttpGet]
    public async Task<ActionResult<IEnumerable<UploadFileRecord>>> GetAll(CancellationToken cancellationToken)
    {
        await using var db = await _dbContextFactory.CreateDbContextAsync(cancellationToken);
        var entities = await db.UploadFiles.AsNoTracking()
            .OrderByDescending(f => f.DateCreated)
            .ToListAsync(cancellationToken);
        return Ok(_mapper.Map<IEnumerable<UploadFileRecord>>(entities));
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<UploadFileRecord>> GetById(Guid id, CancellationToken cancellationToken)
    {
        await using var db = await _dbContextFactory.CreateDbContextAsync(cancellationToken);
        var entity = await db.UploadFiles.AsNoTracking()
            .FirstOrDefaultAsync(f => f.Id == id, cancellationToken);
        if (entity is null) return NotFound();
        return Ok(_mapper.Map<UploadFileRecord>(entity));
    }

    [HttpGet("{id:guid}/download")]
    public async Task<IActionResult> Download(Guid id, CancellationToken cancellationToken)
    {
        await using var db = await _dbContextFactory.CreateDbContextAsync(cancellationToken);
        var entity = await db.UploadFiles.AsNoTracking()
            .FirstOrDefaultAsync(f => f.Id == id, cancellationToken);
        if (entity is null) return NotFound();
        return File(entity.FileData, entity.ContentType, entity.FileName);
    }

    [HttpPost]
    public async Task<ActionResult<UploadFileRecord>> Upload(
        [FromForm] Guid uploadFileTypeId,
        [FromForm] Guid appUserId,
        [FromForm] string? description,
        [FromForm] bool isPublic,
        IFormFile file,
        CancellationToken cancellationToken)
    {
        if (file.Length == 0)
            return BadRequest("File is empty.");

        await using var db = await _dbContextFactory.CreateDbContextAsync(cancellationToken);

        // Validate file extension against the selected type's allowed patterns
        var fileType = await db.UploadFileTypes
            .Include(t => t.AllowedExtensions)
            .AsNoTracking()
            .FirstOrDefaultAsync(t => t.Id == uploadFileTypeId, cancellationToken);

        if (fileType is null)
            return BadRequest("Upload file type not found.");

        if (!fileType.AllowAllExtensions)
        {
            var ext = Path.GetExtension(file.FileName);
            var patterns = fileType.AllowedExtensions.Select(e => e.Pattern);
            if (!FileExtensionPatternMatcher.IsAllowedByPatterns(patterns, ext))
                return BadRequest($"File extension '{ext}' is not permitted for file type '{fileType.Name}'.");
        }

        using var ms = new MemoryStream();
        await file.CopyToAsync(ms, cancellationToken);

        var entity = new UploadFile
        {
            Id = Guid.NewGuid(),
            UploadFileTypeId = uploadFileTypeId,
            AppUserId = appUserId,
            FileName = file.FileName,
            StoredFileName = $"{Guid.NewGuid()}{Path.GetExtension(file.FileName)}",
            ContentType = file.ContentType,
            FileSize = file.Length,
            FileData = ms.ToArray(),
            Description = description,
            IsPublic = isPublic,
            SortOrder = 0,
            DateCreated = DateTime.UtcNow,
            CreatedByAppUserId = appUserId
        };

        db.UploadFiles.Add(entity);
        await db.SaveChangesAsync(cancellationToken);

        return CreatedAtAction(nameof(GetById), new { id = entity.Id }, _mapper.Map<UploadFileRecord>(entity));
    }

    [HttpPut("{id:guid}")]
    public async Task<ActionResult<UploadFileRecord>> Update(
        Guid id,
        [FromBody] UpdateUploadFileRequest request,
        CancellationToken cancellationToken)
    {
        await using var db = await _dbContextFactory.CreateDbContextAsync(cancellationToken);
        var entity = await db.UploadFiles.FirstOrDefaultAsync(f => f.Id == id, cancellationToken);
        if (entity is null) return NotFound();

        entity.UploadFileTypeId = request.UploadFileTypeId;
        entity.Description = request.Description;
        entity.IsPublic = request.IsPublic;
        entity.SortOrder = request.SortOrder;
        entity.DateUpdated = DateTime.UtcNow;
        entity.UpdatedByAppUserId = request.UpdatedByAppUserId;

        await db.SaveChangesAsync(cancellationToken);
        return Ok(_mapper.Map<UploadFileRecord>(entity));
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken)
    {
        await using var db = await _dbContextFactory.CreateDbContextAsync(cancellationToken);
        var entity = await db.UploadFiles.FirstOrDefaultAsync(f => f.Id == id, cancellationToken);
        if (entity is null) return NotFound();

        db.UploadFiles.Remove(entity);
        await db.SaveChangesAsync(cancellationToken);
        return NoContent();
    }
}

public sealed record UpdateUploadFileRequest(
    Guid UploadFileTypeId,
    string? Description,
    bool IsPublic,
    int SortOrder,
    Guid? UpdatedByAppUserId);
