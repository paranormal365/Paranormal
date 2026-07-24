using AutoMapper;
using Ben.Data.Source.Context;
using Ben.Data.Source.Entities;
using Ben.Service.Models.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Ben.Data.WebApi.Controllers.Entities;

[ApiController]
[Authorize(Policy = RoleNames.SuperAdmin)]
[Route("api/admin/upload-file-type-extensions")]
public sealed class AdminUploadFileTypeExtensionController : BenControllerBase
{
    private readonly IDbContextFactory<BenDataContext> _dbContextFactory;
    private readonly IMapper _mapper;
    private readonly IAuditLogService _auditLog;

    public AdminUploadFileTypeExtensionController(IDbContextFactory<BenDataContext> dbContextFactory, IMapper mapper, IAuditLogService auditLog)
    {
        _dbContextFactory = dbContextFactory;
        _mapper = mapper;
        _auditLog = auditLog;
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<UploadFileTypeExtensionRecord>> GetById(Guid id, CancellationToken cancellationToken)
    {
        await using var db = await _dbContextFactory.CreateDbContextAsync(cancellationToken);
        var entity = await db.UploadFileTypeExtensions.AsNoTracking()
            .FirstOrDefaultAsync(e => e.Id == id, cancellationToken);
        return entity is null ? NotFound() : Ok(_mapper.Map<UploadFileTypeExtensionRecord>(entity));
    }

    [HttpPost]
    public async Task<ActionResult<UploadFileTypeExtensionRecord>> Create(
        [FromBody] CreateUploadFileTypeExtensionRequest request,
        CancellationToken cancellationToken)
    {
        await using var db = await _dbContextFactory.CreateDbContextAsync(cancellationToken);

        var typeExists = await db.UploadFileTypes.AnyAsync(t => t.Id == request.UploadFileTypeId, cancellationToken);
        if (!typeExists)
            return BadRequest("UploadFileType not found.");

        var entity = new UploadFileTypeExtension
        {
            Id = Guid.NewGuid(),
            UploadFileTypeId = request.UploadFileTypeId,
            Pattern = request.Pattern.Trim().ToLowerInvariant(),
            DateCreated = DateTime.UtcNow,
            CreatedByAppUserId = request.CreatedByAppUserId
        };

        db.UploadFileTypeExtensions.Add(entity);
        await db.SaveChangesAsync(cancellationToken);
        _ = TryAuditAsync(_auditLog.LogCreateAsync(nameof(UploadFileTypeExtension), entity.Id, entity, GetCurrentUserId(), AppSources.WebApi, cancellationToken));

        return CreatedAtAction(nameof(GetById), new { id = entity.Id }, _mapper.Map<UploadFileTypeExtensionRecord>(entity));
    }

    [HttpPut("{id:guid}")]
    public async Task<ActionResult<UploadFileTypeExtensionRecord>> Update(
        Guid id,
        [FromBody] UpdateUploadFileTypeExtensionRequest request,
        CancellationToken cancellationToken)
    {
        await using var db = await _dbContextFactory.CreateDbContextAsync(cancellationToken);

        var before = await db.UploadFileTypeExtensions.AsNoTracking().FirstOrDefaultAsync(e => e.Id == id, cancellationToken);
        if (before is null) return NotFound();
        var entity = await db.UploadFileTypeExtensions.FirstOrDefaultAsync(e => e.Id == id, cancellationToken);

        entity!.Pattern = request.Pattern.Trim().ToLowerInvariant();
        await db.SaveChangesAsync(cancellationToken);
        _ = TryAuditAsync(_auditLog.LogUpdateAsync(nameof(UploadFileTypeExtension), id, before, entity, GetCurrentUserId(), AppSources.WebApi, cancellationToken));

        return Ok(_mapper.Map<UploadFileTypeExtensionRecord>(entity));
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken)
    {
        await using var db = await _dbContextFactory.CreateDbContextAsync(cancellationToken);
        var entity = await db.UploadFileTypeExtensions.FirstOrDefaultAsync(e => e.Id == id, cancellationToken);
        if (entity is null)
            return NotFound();

        db.UploadFileTypeExtensions.Remove(entity);
        await db.SaveChangesAsync(cancellationToken);
        _ = TryAuditAsync(_auditLog.LogDeleteAsync(nameof(UploadFileTypeExtension), id, entity, GetCurrentUserId(), AppSources.WebApi, cancellationToken));
        return NoContent();
    }
}

public record CreateUploadFileTypeExtensionRequest(
    Guid UploadFileTypeId,
    string Pattern,
    Guid CreatedByAppUserId
);

public record UpdateUploadFileTypeExtensionRequest(string Pattern);
