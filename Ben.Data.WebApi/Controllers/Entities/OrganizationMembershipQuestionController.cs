using AutoMapper;
using Ben.Data.Common.Constants;
using Ben.Data.Common.Enums;
using Ben.Data.Source.Context;
using Ben.Data.Source.Entities;
using Ben.Service.Models.Entities;
using Ben.Service.RepositoryService.GenericInterfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Ben.Data.WebApi.Controllers.Entities;

/// <summary>
/// Manages the custom questions an organization requires applicants to answer.
/// Requires MembershipRequests-Update permission or SuperAdmin.
/// </summary>
[ApiController]
[Route("api/organizations/{orgId:guid}/membership-questions")]
[Authorize]
public sealed class OrganizationMembershipQuestionController : ControllerBase
{
    private readonly IDbContextFactory<BenDataContext> _db;
    private readonly IMapper _mapper;
    private readonly IOrganizationSecurityService _security;

    public OrganizationMembershipQuestionController(
        IDbContextFactory<BenDataContext> db, IMapper mapper,
        IOrganizationSecurityService security)
    {
        _db = db; _mapper = mapper; _security = security;
    }

    [HttpGet]
    public async Task<ActionResult<IEnumerable<OrganizationMembershipQuestionRecord>>> GetAll(
        Guid orgId, CancellationToken ct)
    {
        if (!await CanManageAsync(orgId, ct)) return Forbid();
        await using var db = await _db.CreateDbContextAsync(ct);
        var questions = await db.OrganizationMembershipQuestions
            .AsNoTracking()
            .Where(q => q.OrganizationId == orgId)
            .OrderBy(q => q.SortOrder)
            .ToListAsync(ct);
        return Ok(_mapper.Map<IEnumerable<OrganizationMembershipQuestionRecord>>(questions));
    }

    [HttpPost]
    public async Task<ActionResult<OrganizationMembershipQuestionRecord>> Create(
        Guid orgId, [FromBody] UpsertMembershipQuestionRequest request, CancellationToken ct)
    {
        if (!await CanManageAsync(orgId, ct)) return Forbid();
        var userId = GetUserId();
        await using var db = await _db.CreateDbContextAsync(ct);
        var entity = new OrganizationMembershipQuestion
        {
            Id = Guid.NewGuid(), OrganizationId = orgId,
            QuestionText = request.QuestionText.Trim(),
            IsRequired   = request.IsRequired,
            SortOrder    = request.SortOrder,
            IsActive     = request.IsActive,
            DateCreated  = DateTime.UtcNow, CreatedByAppUserId = userId,
        };
        db.OrganizationMembershipQuestions.Add(entity);
        await db.SaveChangesAsync(ct);
        return CreatedAtAction(nameof(GetAll), new { orgId },
            _mapper.Map<OrganizationMembershipQuestionRecord>(entity));
    }

    [HttpPut("{id:guid}")]
    public async Task<ActionResult<OrganizationMembershipQuestionRecord>> Update(
        Guid orgId, Guid id, [FromBody] UpsertMembershipQuestionRequest request, CancellationToken ct)
    {
        if (!await CanManageAsync(orgId, ct)) return Forbid();
        var userId = GetUserId();
        await using var db = await _db.CreateDbContextAsync(ct);
        var entity = await db.OrganizationMembershipQuestions
            .FirstOrDefaultAsync(q => q.Id == id && q.OrganizationId == orgId, ct);
        if (entity is null) return NotFound();
        entity.QuestionText        = request.QuestionText.Trim();
        entity.IsRequired          = request.IsRequired;
        entity.SortOrder           = request.SortOrder;
        entity.IsActive            = request.IsActive;
        entity.DateUpdated         = DateTime.UtcNow;
        entity.UpdatedByAppUserId  = userId == Guid.Empty ? null : userId;
        await db.SaveChangesAsync(ct);
        return Ok(_mapper.Map<OrganizationMembershipQuestionRecord>(entity));
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid orgId, Guid id, CancellationToken ct)
    {
        if (!await CanManageAsync(orgId, ct)) return Forbid();
        await using var db = await _db.CreateDbContextAsync(ct);
        var entity = await db.OrganizationMembershipQuestions
            .FirstOrDefaultAsync(q => q.Id == id && q.OrganizationId == orgId, ct);
        if (entity is null) return NotFound();
        db.OrganizationMembershipQuestions.Remove(entity);
        await db.SaveChangesAsync(ct);
        return NoContent();
    }

    private Guid GetUserId()
    {
        var value = User.FindFirst(Ben.Data.WebApi.Services.EntraClaimsTransformation.AppUserIdClaimType)?.Value
                 ?? User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
        return Guid.TryParse(value, out var id) ? id : Guid.Empty;
    }

    private async Task<bool> CanManageAsync(Guid orgId, CancellationToken ct)
    {
        if (User.IsInRole(RoleNames.SuperAdmin)) return true;
        var userId = GetUserId();
        if (userId == Guid.Empty) return false;
        return await _security.HasAccessAsync(userId, orgId,
            OrganizationSecurityTable.MembershipRequests, OrganizationSecurityAction.Update, ct);
    }
}

public sealed record UpsertMembershipQuestionRequest(
    string QuestionText,
    bool IsRequired,
    int SortOrder,
    bool IsActive);
