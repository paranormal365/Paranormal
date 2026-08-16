using AutoMapper;
using Ben.Data.Common.Enums;
using Ben.Data.Source.Context;
using Ben.Data.Source.Entities;
using Ben.Service.Models.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Ben.Data.WebApi.Services.Access;

namespace Ben.Data.WebApi.Controllers.Entities;

/// <summary>Internal org-side notes on a case. Never exposed to clients.</summary>
[ApiController]
[Route("api/organizations/{orgId:guid}/cases/{caseId:guid}/notes")]
[Authorize]
public sealed class CaseNoteController : BenControllerBase
{
    private readonly IDbContextFactory<BenDataContext> _db;
    private readonly IMapper _mapper;

    public CaseNoteController(IDbContextFactory<BenDataContext> db, IMapper mapper)
    { _db = db; _mapper = mapper; }

    [HttpGet]
    public async Task<ActionResult<IEnumerable<CaseNoteRecord>>> GetAll(
        Guid orgId, Guid caseId, CancellationToken ct)
    {
        if (!await IsOrgMemberAsync(orgId, ct)) return Forbid();
        await using var db = await _db.CreateDbContextAsync(ct);
        var notes = await db.CaseNotes.AsNoTracking()
            .Include(n => n.AuthorAppUser)
            .Where(n => n.CaseId == caseId)
            .OrderByDescending(n => n.IsPinned)
            .ThenByDescending(n => n.DateCreated)
            .ToListAsync(ct);
        return Ok(_mapper.Map<IEnumerable<CaseNoteRecord>>(notes));
    }

    [HttpPost]
    public async Task<ActionResult<CaseNoteRecord>> Create(
        Guid orgId, Guid caseId, [FromBody] UpsertCaseNoteRequest request, CancellationToken ct)
    {
        if (!await IsOrgMemberAsync(orgId, ct)) return Forbid();
        var userId = GetCurrentUserId();
        await using var db = await _db.CreateDbContextAsync(ct);
        if (!await db.Cases.AnyAsync(c => c.Id == caseId && c.OrganizationId == orgId, ct))
            return NotFound("Case not found.");
        if (string.IsNullOrWhiteSpace(request.Body)) return BadRequest("Body is required.");

        var note = new CaseNote
        {
            Id                 = Guid.NewGuid(),
            CaseId             = caseId,
            AuthorAppUserId    = userId,
            Title              = request.Title?.Trim(),
            Body               = request.Body.Trim(),
            IsPinned           = request.IsPinned,
            DateCreated        = DateTime.UtcNow,
            CreatedByAppUserId = userId,
        };
        db.CaseNotes.Add(note);
        await db.SaveChangesAsync(ct);

        await db.Entry(note).Reference(n => n.AuthorAppUser).LoadAsync(ct);
        return CreatedAtAction(nameof(GetAll), new { orgId, caseId },
            _mapper.Map<CaseNoteRecord>(note));
    }

    [HttpPut("{noteId:guid}")]
    public async Task<ActionResult<CaseNoteRecord>> Update(
        Guid orgId, Guid caseId, Guid noteId, [FromBody] UpsertCaseNoteRequest request, CancellationToken ct)
    {
        if (!await IsOrgMemberAsync(orgId, ct)) return Forbid();
        var userId = GetCurrentUserId();
        await using var db = await _db.CreateDbContextAsync(ct);
        var note = await db.CaseNotes.Include(n => n.AuthorAppUser)
            .FirstOrDefaultAsync(n => n.Id == noteId && n.CaseId == caseId, ct);
        if (note is null) return NotFound();

        // Only the author or an org admin can edit
        bool isAuthor = note.AuthorAppUserId == userId;
        if (!isAuthor && !await IsOrgAdminAsync(orgId, ct)) return Forbid();

        if (string.IsNullOrWhiteSpace(request.Body)) return BadRequest("Body is required.");
        note.Title              = request.Title?.Trim();
        note.Body               = request.Body.Trim();
        note.IsPinned           = request.IsPinned;
        note.DateUpdated        = DateTime.UtcNow;
        note.UpdatedByAppUserId = userId == Guid.Empty ? null : userId;
        await db.SaveChangesAsync(ct);
        return Ok(_mapper.Map<CaseNoteRecord>(note));
    }

    [HttpDelete("{noteId:guid}")]
    public async Task<IActionResult> Delete(
        Guid orgId, Guid caseId, Guid noteId, CancellationToken ct)
    {
        if (!await IsOrgMemberAsync(orgId, ct)) return Forbid();
        var userId = GetCurrentUserId();
        await using var db = await _db.CreateDbContextAsync(ct);
        var note = await db.CaseNotes
            .FirstOrDefaultAsync(n => n.Id == noteId && n.CaseId == caseId, ct);
        if (note is null) return NotFound();

        bool isAuthor = note.AuthorAppUserId == userId;
        if (!isAuthor && !await IsOrgAdminAsync(orgId, ct)) return Forbid();

        db.CaseNotes.Remove(note);
        await db.SaveChangesAsync(ct);
        return NoContent();
    }

    private async Task<bool> IsOrgMemberAsync(Guid orgId, CancellationToken ct)
    {
        if (User.IsInRole(Ben.Data.Common.Constants.RoleNames.SuperAdmin)) return true;
        var userId = GetCurrentUserId();
        if (userId == Guid.Empty) return false;
        await using var db = await _db.CreateDbContextAsync(ct);
        return await FileAudienceAccess.IsOrgMemberAsync(db, orgId, userId, ct);
    }

    private async Task<bool> IsOrgAdminAsync(Guid orgId, CancellationToken ct)
    {
        if (User.IsInRole(Ben.Data.Common.Constants.RoleNames.SuperAdmin)) return true;
        var userId = GetCurrentUserId();
        if (userId == Guid.Empty) return false;
        await using var db = await _db.CreateDbContextAsync(ct);
        return await FileAudienceAccess.IsOrgAdminAsync(db, orgId, userId, ct);
    }
}

public sealed record UpsertCaseNoteRequest(string? Title, string Body, bool IsPinned = false);
