using AutoMapper;
using Ben.Data.Source.Entities;
using Ben.Service.Models.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;

namespace Ben.Data.WebApi.Controllers.Entities;

/// <summary>
/// User-owned video projects. Projects are personal by default; optionally linked to a case
/// via the optional <c>caseId</c> query parameter on POST.
/// POST and PUT bodies are raw <c>ProjectFile</c> JSON (as sent by the Ben.Video editor).
/// </summary>
[ApiController]
[Route("api/video-projects")]
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

    // GET /api/video-projects[?caseId=...]
    [HttpGet]
    public async Task<ActionResult<IEnumerable<VideoProjectRecord>>> GetAll(
        [FromQuery] Guid? caseId, CancellationToken ct)
    {
        var userId = GetCurrentUserIdOrThrow();
        await using var db = await _db.CreateDbContextAsync(ct);

        var query = db.VideoProjects.AsNoTracking()
            .Where(p => p.CreatedByAppUserId == userId);

        if (caseId.HasValue)
            query = query.Where(p => p.CaseId == caseId.Value);

        var entities = await query.OrderByDescending(p => p.DateCreated).ToListAsync(ct);
        return Ok(_mapper.Map<IEnumerable<VideoProjectRecord>>(entities));
    }

    // GET /api/video-projects/{id}
    [HttpGet("{id:guid}")]
    public async Task<ActionResult<VideoProjectRecord>> GetById(Guid id, CancellationToken ct)
    {
        var userId = GetCurrentUserIdOrThrow();
        await using var db = await _db.CreateDbContextAsync(ct);
        var entity = await db.VideoProjects.AsNoTracking()
            .FirstOrDefaultAsync(p => p.Id == id && p.CreatedByAppUserId == userId, ct);
        if (entity is null) return NotFound();
        return Ok(_mapper.Map<VideoProjectRecord>(entity));
    }

    // POST /api/video-projects[?caseId=...]
    // Body: raw ProjectFile JSON (projectName + tracks etc.) as sent by Ben.Video editor
    [HttpPost]
    public async Task<ActionResult<VideoProjectRecord>> Create(
        [FromQuery] Guid? caseId,
        [FromBody] JsonElement body,
        CancellationToken ct)
    {
        if (caseId.HasValue && !await CanAccessCaseAsync(caseId.Value, ct)) return Forbid();

        var userId = GetCurrentUserIdOrThrow();
        var name = body.TryGetProperty("projectName", out var n) ? n.GetString() ?? "Untitled Project" : "Untitled Project";
        var projectJson = body.GetRawText();

        await using var db = await _db.CreateDbContextAsync(ct);
        var entity = new VideoProject
        {
            Id                 = Guid.NewGuid(),
            CaseId             = caseId,
            Name               = name,
            ProjectJson        = projectJson,
            DateCreated        = DateTime.UtcNow,
            CreatedByAppUserId = userId,
        };

        db.VideoProjects.Add(entity);
        await db.SaveChangesAsync(ct);
        return CreatedAtAction(nameof(GetById), new { id = entity.Id },
            _mapper.Map<VideoProjectRecord>(entity));
    }

    // PUT /api/video-projects/{id}
    // Body: raw ProjectFile JSON
    [HttpPut("{id:guid}")]
    public async Task<ActionResult<VideoProjectRecord>> Update(
        Guid id, [FromBody] JsonElement body, CancellationToken ct)
    {
        var userId = GetCurrentUserIdOrThrow();
        await using var db = await _db.CreateDbContextAsync(ct);

        var entity = await db.VideoProjects
            .FirstOrDefaultAsync(p => p.Id == id && p.CreatedByAppUserId == userId, ct);
        if (entity is null) return NotFound();

        var name = body.TryGetProperty("projectName", out var n) ? n.GetString() : null;
        entity.Name               = name ?? entity.Name;
        entity.ProjectJson        = body.GetRawText();
        entity.DateUpdated        = DateTime.UtcNow;
        entity.UpdatedByAppUserId = userId;

        await db.SaveChangesAsync(ct);
        return Ok(_mapper.Map<VideoProjectRecord>(entity));
    }

    // DELETE /api/video-projects/{id}
    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        var userId = GetCurrentUserIdOrThrow();
        var isSuperAdmin = User.IsInRole(RoleNames.SuperAdmin);

        await using var db = await _db.CreateDbContextAsync(ct);
        var entity = await db.VideoProjects
            .FirstOrDefaultAsync(p => p.Id == id, ct);
        if (entity is null) return NotFound();

        if (!isSuperAdmin && entity.CreatedByAppUserId != userId) return Forbid();

        db.VideoProjects.Remove(entity);
        await db.SaveChangesAsync(ct);
        return NoContent();
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

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
