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
[Route("api/admin/upload-file-types")]
public sealed class AdminUploadFileTypeController : BenControllerBase
{
    private readonly IDbContextFactory<BenDataContext> _dbContextFactory;
    private readonly IMapper _mapper;

    public AdminUploadFileTypeController(IDbContextFactory<BenDataContext> dbContextFactory, IMapper mapper)
    {
        _dbContextFactory = dbContextFactory;
        _mapper = mapper;
    }

    [HttpGet]
    public async Task<ActionResult<IEnumerable<UploadFileTypeRecord>>> GetAll(CancellationToken cancellationToken)
    {
        await using var db = await _dbContextFactory.CreateDbContextAsync(cancellationToken);
        var types = await db.UploadFileTypes.AsNoTracking()
            .OrderBy(t => t.SortOrder).ThenBy(t => t.Name)
            .ToListAsync(cancellationToken);
        return Ok(_mapper.Map<IEnumerable<UploadFileTypeRecord>>(types));
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<UploadFileTypeRecord>> GetById(Guid id, CancellationToken cancellationToken)
    {
        await using var db = await _dbContextFactory.CreateDbContextAsync(cancellationToken);
        var entity = await db.UploadFileTypes.AsNoTracking()
            .FirstOrDefaultAsync(t => t.Id == id, cancellationToken);
        return entity is null ? NotFound() : Ok(_mapper.Map<UploadFileTypeRecord>(entity));
    }

    /// <summary>Returns all file types including their allowed extension patterns.</summary>
    [HttpGet("with-extensions")]
    public async Task<ActionResult<IEnumerable<UploadFileTypeWithExtensionsResponse>>> GetAllWithExtensions(CancellationToken cancellationToken)
    {
        await using var db = await _dbContextFactory.CreateDbContextAsync(cancellationToken);
        var types = await db.UploadFileTypes
            .AsNoTracking()
            .Include(t => t.AllowedExtensions)
            .OrderBy(t => t.SortOrder).ThenBy(t => t.Name)
            .ToListAsync(cancellationToken);

        var result = types.Select(t => new UploadFileTypeWithExtensionsResponse(
            _mapper.Map<UploadFileTypeRecord>(t),
            t.AllowedExtensions.Select(e => _mapper.Map<UploadFileTypeExtensionRecord>(e)).ToList()
        ));

        return Ok(result);
    }

    [HttpPost]
    public async Task<ActionResult<UploadFileTypeRecord>> Create(
        [FromBody] AdminCreateUploadFileTypeRequest request,
        CancellationToken cancellationToken)
    {
        await using var db = await _dbContextFactory.CreateDbContextAsync(cancellationToken);

        var entity = new UploadFileType
        {
            Id = Guid.NewGuid(),
            Name = request.Name,
            Description = request.Description,
            IconClass = request.IconClass,
            ColorClass = request.ColorClass,
            IsActive = request.IsActive,
            IsPublic = request.IsPublic,
            SortOrder = request.SortOrder,
            AllowAllExtensions = request.AllowAllExtensions,
            DateCreated = DateTime.UtcNow,
            CreatedByAppUserId = request.CreatedByAppUserId
        };

        db.UploadFileTypes.Add(entity);
        await db.SaveChangesAsync(cancellationToken);

        return CreatedAtAction(nameof(GetById), new { id = entity.Id }, _mapper.Map<UploadFileTypeRecord>(entity));
    }

    [HttpPut("{id:guid}")]
    public async Task<ActionResult<UploadFileTypeRecord>> Update(
        Guid id,
        [FromBody] AdminUpdateUploadFileTypeRequest request,
        CancellationToken cancellationToken)
    {
        await using var db = await _dbContextFactory.CreateDbContextAsync(cancellationToken);

        var entity = await db.UploadFileTypes.FirstOrDefaultAsync(t => t.Id == id, cancellationToken);
        if (entity is null)
            return NotFound();

        entity.Name = request.Name;
        entity.Description = request.Description;
        entity.IconClass = request.IconClass;
        entity.ColorClass = request.ColorClass;
        entity.IsActive = request.IsActive;
        entity.IsPublic = request.IsPublic;
        entity.SortOrder = request.SortOrder;
        entity.AllowAllExtensions = request.AllowAllExtensions;
        entity.DateUpdated = DateTime.UtcNow;
        entity.UpdatedByAppUserId = request.UpdatedByAppUserId;

        await db.SaveChangesAsync(cancellationToken);

        return Ok(_mapper.Map<UploadFileTypeRecord>(entity));
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken)
    {
        await using var db = await _dbContextFactory.CreateDbContextAsync(cancellationToken);
        var entity = await db.UploadFileTypes.FirstOrDefaultAsync(t => t.Id == id, cancellationToken);
        if (entity is null)
            return NotFound();

        db.UploadFileTypes.Remove(entity);
        await db.SaveChangesAsync(cancellationToken);
        return NoContent();
    }
}

public record UploadFileTypeWithExtensionsResponse(
    UploadFileTypeRecord FileType,
    IReadOnlyList<UploadFileTypeExtensionRecord> Extensions
);

public record AdminCreateUploadFileTypeRequest(
    string Name,
    string? Description,
    string? IconClass,
    string? ColorClass,
    bool IsActive,
    bool IsPublic,
    int SortOrder,
    bool AllowAllExtensions,
    Guid CreatedByAppUserId);

public record AdminUpdateUploadFileTypeRequest(
    string Name,
    string? Description,
    string? IconClass,
    string? ColorClass,
    bool IsActive,
    bool IsPublic,
    int SortOrder,
    bool AllowAllExtensions,
    Guid? UpdatedByAppUserId);
