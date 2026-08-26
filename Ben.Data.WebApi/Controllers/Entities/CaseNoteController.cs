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

    private readonly Services.Billing.SubscriptionLimitGuard _limits;

    public CaseNoteController(IDbContextFactory<BenDataContext> db, IMapper mapper, Services.Billing.SubscriptionLimitGuard limits,
        Ben.Service.RepositoryService.GenericInterfaces.IOrganizationSecurityService security)
    { _db = db; _mapper = mapper; _limits = limits; _security = security; }

    private readonly Ben.Service.RepositoryService.GenericInterfaces.IOrganizationSecurityService _security;

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
        if (!await MayAsync(orgId, Ben.Data.Common.Enums.OrganizationSecurityAction.Create, ct)) return Forbid();
        var userId = GetCurrentUserId();
        await using var db = await _db.CreateDbContextAsync(ct);
        if (!await db.Cases.AnyAsync(c => c.Id == caseId && c.OrganizationId == orgId, ct))
            return NotFound("Case not found.");
        if (string.IsNullOrWhiteSpace(request.Body)) return BadRequest("Body is required.");
        if (await _limits.WhyReadOnlyAsync(orgId, ct) is { } readOnly) return BadRequest(readOnly);

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
        if (!await MayAsync(orgId, Ben.Data.Common.Enums.OrganizationSecurityAction.Update, ct)) return Forbid();
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
        if (!await MayAsync(orgId, Ben.Data.Common.Enums.OrganizationSecurityAction.Delete, ct)) return Forbid();
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

    // Item 156 Phase D: bare membership stopped being the rule here — see CaseFileController.
    /// <summary>
    /// May the caller take this action on this case's notes?
    /// </summary>
    /// <remarks>
    /// <para><b>Create, update and delete used to ask for Case.READ</b> — through a helper called
    /// <c>IsOrgMemberAsync</c>, which is neither what it asked nor what it meant. Anybody who
    /// could see a case could rewrite and destroy its notes.</para>
    ///
    /// <para>That was survivable while every member was auto-granted case read anyway. It is not
    /// survivable now: Ben ended the grandfathering on 2026-08-26, so a read grant is a
    /// deliberate act and has to mean READ. Owners and administrators still pass above this.</para>
    /// </remarks>
    private Task<bool> MayAsync(Guid orgId, Ben.Data.Common.Enums.OrganizationSecurityAction action, CancellationToken ct)
        => User.IsInRole(Ben.Data.Common.Constants.RoleNames.SuperAdmin)
            ? Task.FromResult(true)
            : _security.MayAsync(GetCurrentUserId(), orgId,
                Ben.Data.Common.Enums.OrganizationPermissionArea.Cases, action, ct);

    private async Task<bool> IsOrgMemberAsync(Guid orgId, CancellationToken ct)
        => User.IsInRole(Ben.Data.Common.Constants.RoleNames.SuperAdmin)
        || await _security.HasAccessAsync(GetCurrentUserId(), orgId,
               Ben.Data.Common.Enums.OrganizationSecurityTable.Case,
               Ben.Data.Common.Enums.OrganizationSecurityAction.Read, ct);

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
