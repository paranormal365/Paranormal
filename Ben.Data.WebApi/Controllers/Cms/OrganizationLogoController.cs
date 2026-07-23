using AutoMapper;
using Ben.Data.Common.Enums;
using Ben.Data.Source.Context;
using Ben.Data.Source.Entities;
using Ben.Service.Models.Entities;
using Ben.Service.RepositoryService.GenericInterfaces;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Ben.Data.WebApi.Controllers.Cms;

/// <summary>Manage logos associated with an organization.</summary>
[Route("api/organizations/{orgId:guid}/logos")]
public sealed class OrganizationLogoController : OrgCmsControllerBase
{
    private readonly IAuditLogService _auditLog;

    public OrganizationLogoController(
        IDbContextFactory<BenDataContext> dbFactory,
        IMapper mapper,
        IOrganizationSecurityService security,
        IAuditLogService auditLog)
        : base(dbFactory, mapper, security)
    {
        _auditLog = auditLog;
    }

    [HttpGet]
    public async Task<ActionResult<IEnumerable<OrganizationLogoRecord>>> GetAll(
        Guid orgId, CancellationToken ct)
    {
        var userId = GetCurrentUserId();
        if (userId is null) return Unauthorized();
        if (!await IsCmsAuthorizedAsync(userId.Value, orgId, OrganizationSecurityTable.OrganizationPage, OrganizationSecurityAction.Read, ct))
            return Forbid();

        await using var db = await DbFactory.CreateDbContextAsync(ct);
        var logos = await db.OrganizationLogos
            .AsNoTracking()
            .Where(l => l.OrganizationId == orgId)
            .OrderBy(l => l.SortOrder)
            .ToListAsync(ct);

        return Ok(Mapper.Map<IEnumerable<OrganizationLogoRecord>>(logos));
    }

    [HttpPost]
    public async Task<ActionResult<OrganizationLogoRecord>> Create(
        Guid orgId, [FromBody] CreateOrgLogoRequest request, CancellationToken ct)
    {
        var userId = GetCurrentUserId();
        if (userId is null) return Unauthorized();
        if (!await IsCmsAuthorizedAsync(userId.Value, orgId, OrganizationSecurityTable.OrganizationPage, OrganizationSecurityAction.Update, ct))
            return Forbid();

        await using var db = await DbFactory.CreateDbContextAsync(ct);

        // Verify the upload file exists
        if (!await db.UploadFiles.AnyAsync(f => f.Id == request.UploadFileId, ct))
            return BadRequest("UploadFile not found.");

        // Deactivate other logos when setting this one as active
        if (request.IsActive)
        {
            var existing = await db.OrganizationLogos
                .Where(l => l.OrganizationId == orgId && l.IsActive)
                .ToListAsync(ct);
            foreach (var e in existing) e.IsActive = false;
        }

        var logo = new OrganizationLogo
        {
            OrganizationId     = orgId,
            UploadFileId       = request.UploadFileId,
            AltText            = request.AltText?.Trim(),
            IsActive           = request.IsActive,
            SortOrder          = request.SortOrder,
            DateCreated        = DateTime.UtcNow,
            CreatedByAppUserId = userId.Value
        };

        db.OrganizationLogos.Add(logo);
        await db.SaveChangesAsync(ct);
        _ = TryAuditAsync(_auditLog.LogCreateAsync(nameof(OrganizationLogo), logo.Id, logo, userId.Value, AppSources.WebApi, ct));
        return CreatedAtAction(nameof(GetAll), new { orgId }, Mapper.Map<OrganizationLogoRecord>(logo));
    }

    [HttpPut("{logoId:guid}")]
    public async Task<ActionResult<OrganizationLogoRecord>> Update(
        Guid orgId, Guid logoId, [FromBody] UpdateOrgLogoRequest request, CancellationToken ct)
    {
        var userId = GetCurrentUserId();
        if (userId is null) return Unauthorized();
        if (!await IsCmsAuthorizedAsync(userId.Value, orgId, OrganizationSecurityTable.OrganizationPage, OrganizationSecurityAction.Update, ct))
            return Forbid();

        await using var db = await DbFactory.CreateDbContextAsync(ct);
        var before = await db.OrganizationLogos.AsNoTracking()
            .FirstOrDefaultAsync(l => l.Id == logoId && l.OrganizationId == orgId, ct);
        if (before is null) return NotFound();
        var logo = await db.OrganizationLogos
            .FirstOrDefaultAsync(l => l.Id == logoId && l.OrganizationId == orgId, ct);

        if (request.IsActive && !logo!.IsActive)
        {
            var others = await db.OrganizationLogos
                .Where(l => l.OrganizationId == orgId && l.IsActive && l.Id != logoId)
                .ToListAsync(ct);
            foreach (var o in others) o.IsActive = false;
        }

        logo!.AltText            = request.AltText?.Trim();
        logo.IsActive           = request.IsActive;
        logo.SortOrder          = request.SortOrder;
        logo.DateUpdated        = DateTime.UtcNow;
        logo.UpdatedByAppUserId = userId.Value;

        await db.SaveChangesAsync(ct);
        _ = TryAuditAsync(_auditLog.LogUpdateAsync(nameof(OrganizationLogo), logoId, before, logo, userId.Value, AppSources.WebApi, ct));
        return Ok(Mapper.Map<OrganizationLogoRecord>(logo));
    }

    [HttpDelete("{logoId:guid}")]
    public async Task<IActionResult> Delete(Guid orgId, Guid logoId, CancellationToken ct)
    {
        var userId = GetCurrentUserId();
        if (userId is null) return Unauthorized();
        if (!await IsCmsAuthorizedAsync(userId.Value, orgId, OrganizationSecurityTable.OrganizationPage, OrganizationSecurityAction.Delete, ct))
            return Forbid();

        await using var db = await DbFactory.CreateDbContextAsync(ct);
        var logo = await db.OrganizationLogos
            .FirstOrDefaultAsync(l => l.Id == logoId && l.OrganizationId == orgId, ct);
        if (logo is null) return NotFound();

        db.OrganizationLogos.Remove(logo);
        await db.SaveChangesAsync(ct);
        _ = TryAuditAsync(_auditLog.LogDeleteAsync(nameof(OrganizationLogo), logoId, logo, userId.Value, AppSources.WebApi, ct));
        return NoContent();
    }
}

public sealed record CreateOrgLogoRequest(Guid UploadFileId, string? AltText, bool IsActive, int SortOrder);
public sealed record UpdateOrgLogoRequest(string? AltText, bool IsActive, int SortOrder);
