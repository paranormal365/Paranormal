using AutoMapper;
using Ben.Data.Source.Entities;
using Ben.Service.Models.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Ben.Data.WebApi.Controllers.Entities;

[ApiController]
[Route("api/cases/{caseId:guid}/video-projects")]
[Authorize]
public sealed class VideoProjectController : BenControllerBase
{
    private readonly IDbContextFactory<BenDataContext> _db;
    private readonly IMapper _mapper;

    public VideoProjectController(IDbContextFactory<BenDataContext> db, IMapper mapper)
    {
        _db = db;
        _mapper = mapper;
    }

    [HttpGet]
    public async Task<ActionResult<IEnumerable<VideoProjectRecord>>> GetAll(
        Guid caseId, CancellationToken ct)
    {
        if (!await CanAccessCaseAsync(caseId, ct)) return Forbid();

        await using var db = await _db.CreateDbContextAsync(ct);
        var entities = await db.VideoProjects.AsNoTracking()
            .Where(p => p.CaseId == caseId)
            .OrderByDescending(p => p.DateCreated)
            .ToListAsync(ct);
        return Ok(_mapper.Map<IEnumerable<VideoProjectRecord>>(entities));
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<VideoProjectRecord>> GetById(
        Guid caseId, Guid id, CancellationToken ct)
    {
        if (!await CanAccessCaseAsync(caseId, ct)) return Forbid();

        await using var db = await _db.CreateDbContextAsync(ct);
        var entity = await db.VideoProjects.AsNoTracking()
            .FirstOrDefaultAsync(p => p.Id == id && p.CaseId == caseId, ct);
        if (entity is null) return NotFound();
        return Ok(_mapper.Map<VideoProjectRecord>(entity));
    }

    [HttpPost]
    public async Task<ActionResult<VideoProjectRecord>> Create(
        Guid caseId, [FromBody] VideoProjectRequest request, CancellationToken ct)
    {
        if (!await CanAccessCaseAsync(caseId, ct)) return Forbid();

        var userId = GetCurrentUserIdOrThrow();
        await using var db = await _db.CreateDbContextAsync(ct);

        var entity = new VideoProject
        {
            Id                 = Guid.NewGuid(),
            CaseId             = caseId,
            Name               = request.Name,
            ProjectJson        = request.ProjectJson,
            DateCreated        = DateTime.UtcNow,
            CreatedByAppUserId = userId,
        };

        db.VideoProjects.Add(entity);
        await db.SaveChangesAsync(ct);
        return CreatedAtAction(nameof(GetById), new { caseId, id = entity.Id },
            _mapper.Map<VideoProjectRecord>(entity));
    }

    [HttpPut("{id:guid}")]
    public async Task<ActionResult<VideoProjectRecord>> Update(
        Guid caseId, Guid id, [FromBody] VideoProjectRequest request, CancellationToken ct)
    {
        if (!await CanAccessCaseAsync(caseId, ct)) return Forbid();

        var userId = GetCurrentUserIdOrThrow();
        await using var db = await _db.CreateDbContextAsync(ct);

        var entity = await db.VideoProjects
            .FirstOrDefaultAsync(p => p.Id == id && p.CaseId == caseId, ct);
        if (entity is null) return NotFound();

        entity.Name          = request.Name;
        entity.ProjectJson   = request.ProjectJson;
        entity.DateUpdated   = DateTime.UtcNow;
        entity.UpdatedByAppUserId = userId;

        await db.SaveChangesAsync(ct);
        return Ok(_mapper.Map<VideoProjectRecord>(entity));
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(
        Guid caseId, Guid id, CancellationToken ct)
    {
        if (!await CanAccessCaseAsync(caseId, ct)) return Forbid();

        await using var db = await _db.CreateDbContextAsync(ct);
        var entity = await db.VideoProjects
            .FirstOrDefaultAsync(p => p.Id == id && p.CaseId == caseId, ct);
        if (entity is null) return NotFound();

        // Only the creator or SuperAdmin may delete.
        var userId = GetCurrentUserIdOrThrow();
        var isSuperAdmin = User.IsInRole(RoleNames.SuperAdmin);
        if (!isSuperAdmin && entity.CreatedByAppUserId != userId)
            return Forbid();

        db.VideoProjects.Remove(entity);
        await db.SaveChangesAsync(ct);
        return NoContent();
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    /// <summary>True when the current user is a member of the org that owns the case, or is SuperAdmin.</summary>
    private async Task<bool> CanAccessCaseAsync(Guid caseId, CancellationToken ct)
    {
        if (User.IsInRole(RoleNames.SuperAdmin)) return true;

        var userId = GetCurrentUserIdOrNull();
        if (userId is null) return false;

        await using var db = await _db.CreateDbContextAsync(ct);
        var orgId = await db.Cases.AsNoTracking()
            .Where(c => c.Id == caseId)
            .Select(c => (Guid?)c.OrganizationId)
            .FirstOrDefaultAsync(ct);

        if (orgId is null) return false;

        return await db.OrganizationUserMemberships.AsNoTracking()
            .AnyAsync(m => m.OrganizationId == orgId && m.AppUserId == userId.Value, ct);
    }
}
