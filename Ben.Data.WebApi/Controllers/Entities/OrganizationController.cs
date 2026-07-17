using AutoMapper;
using Ben.Data.Common.Constants;
using Ben.Data.Common.Enums;
using Ben.Data.Source.Context;
using Ben.Data.Source.Entities;
using Ben.Data.WebApi.Controllers.Admin;
using Ben.Service.Models.Admin;
using Ben.Service.Models.Entities;
using Ben.Service.RepositoryService.GenericInterfaces;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;

namespace Ben.Data.WebApi.Controllers.Entities;

[Route("api/organizations")]
public sealed class OrganizationController : EntityReadControllerBase<Organization, OrganizationRecord>
{
    private readonly IDbContextFactory<BenDataContext> _dbFactory;
    private readonly IMapper _mapper2;
    private readonly IOrganizationSecurityService _security;

    public OrganizationController(
        IDbContextFactory<BenDataContext> dbContextFactory,
        IMapper mapper,
        IOrganizationSecurityService security)
        : base(dbContextFactory, mapper)
    {
        _dbFactory = dbContextFactory;
        _mapper2   = mapper;
        _security  = security;
    }

    // ── Suppress base read-only GET endpoints ─────────────────────────────────

    [NonAction]
    public override Task<ActionResult<IEnumerable<OrganizationRecord>>> GetAll(CancellationToken cancellationToken)
        => throw new NotSupportedException();

    [NonAction]
    public override Task<ActionResult<OrganizationRecord>> GetById(Guid id, CancellationToken cancellationToken)
        => throw new NotSupportedException();

    // ── Permission-aware GET endpoints ────────────────────────────────────────

    /// <summary>
    /// Returns organizations visible to the current user with per-org CanEdit and CanDelete flags.
    /// SuperAdmins see all organizations; others see only orgs they are active members of.
    /// </summary>
    [HttpGet]
    public async Task<ActionResult<IEnumerable<OrganizationListItemResponse>>> GetAllWithPermissions(CancellationToken ct)
    {
        var userId       = GetCurrentUserId();
        if (userId is null) return Unauthorized();
        var isSuperAdmin = User.IsInRole(RoleNames.SuperAdmin);
        var orgs = await _security.GetOrganizationsForUserAsync(userId.Value, ct);

        var result = new List<OrganizationListItemResponse>(orgs.Count);
        foreach (var org in orgs)
        {
            bool canEdit, canDelete;
            if (isSuperAdmin)
            {
                canEdit   = true;
                canDelete = true;
            }
            else
            {
                canEdit   = await _security.HasAccessAsync(userId.Value, org.Id, OrganizationSecurityTable.Organization, OrganizationSecurityAction.Update, ct);
                canDelete = await _security.HasAccessAsync(userId.Value, org.Id, OrganizationSecurityTable.Organization, OrganizationSecurityAction.Delete, ct);
            }
            result.Add(new OrganizationListItemResponse(org.Id, org.Name, org.UrlName, org.DateCreated, canEdit, canDelete));
        }
        return Ok(result);
    }

    /// <summary>Returns a single organization for the edit form. Requires Read access or SuperAdmin.</summary>
    [HttpGet("{id:guid}")]
    public async Task<ActionResult<OrganizationAdminRecord>> GetByIdWithPermissions(Guid id, CancellationToken ct)
    {
        var userId       = GetCurrentUserId();
        if (userId is null) return Unauthorized();
        var isSuperAdmin = User.IsInRole(RoleNames.SuperAdmin);

        if (!isSuperAdmin)
        {
            var canRead = await _security.HasAccessAsync(userId.Value, id, OrganizationSecurityTable.Organization, OrganizationSecurityAction.Read, ct);
            if (!canRead) return Forbid();
        }

        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var org = await db.Organizations.AsNoTracking().FirstOrDefaultAsync(o => o.Id == id, ct);
        if (org is null) return NotFound();

        return Ok(_mapper2.Map<OrganizationAdminRecord>(org));
    }

    // ── Mutating endpoints ────────────────────────────────────────────────────

    /// <summary>Updates Name and UrlName. Requires Update access or SuperAdmin.</summary>
    [HttpPut("{id:guid}")]
    public async Task<ActionResult<OrganizationAdminRecord>> Update(
        Guid id, [FromBody] AdminUpdateOrganizationRequest request, CancellationToken ct)
    {
        var userId       = GetCurrentUserId();
        if (userId is null) return Unauthorized();
        var isSuperAdmin = User.IsInRole(RoleNames.SuperAdmin);

        if (!isSuperAdmin)
        {
            var canEdit = await _security.HasAccessAsync(userId.Value, id, OrganizationSecurityTable.Organization, OrganizationSecurityAction.Update, ct);
            if (!canEdit) return Forbid();
        }

        if (string.IsNullOrWhiteSpace(request.Name))    return BadRequest("Name is required.");
        if (string.IsNullOrWhiteSpace(request.UrlName)) return BadRequest("UrlName is required.");

        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var org = await db.Organizations.FirstOrDefaultAsync(o => o.Id == id, ct);
        if (org is null) return NotFound();

        org.Name               = request.Name.Trim();
        org.UrlName            = request.UrlName.Trim().ToLowerInvariant();
        org.DateUpdated        = DateTime.UtcNow;
        org.UpdatedByAppUserId = userId.Value;

        await db.SaveChangesAsync(ct);
        return Ok(_mapper2.Map<OrganizationAdminRecord>(org));
    }

    /// <summary>Deletes an organization. Requires Delete access or SuperAdmin.</summary>
    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        var userId       = GetCurrentUserId();
        if (userId is null) return Unauthorized();
        var isSuperAdmin = User.IsInRole(RoleNames.SuperAdmin);

        if (!isSuperAdmin)
        {
            var canDelete = await _security.HasAccessAsync(userId.Value, id, OrganizationSecurityTable.Organization, OrganizationSecurityAction.Delete, ct);
            if (!canDelete) return Forbid();
        }

        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var org = await db.Organizations.FirstOrDefaultAsync(o => o.Id == id, ct);
        if (org is null) return NotFound();

        db.Organizations.Remove(org);
        await db.SaveChangesAsync(ct);
        return NoContent();
    }

    /// <summary>Creates a new organization. SuperAdmin only (checked via DB role, supports both local and Entra tokens).</summary>
    [HttpPost]
    public async Task<ActionResult<OrganizationAdminRecord>> Create(
        [FromBody] AdminCreateOrganizationRequest request, CancellationToken ct)
    {
        var userId = GetCurrentUserId();
        if (userId is null) return Unauthorized();

        if (!User.IsInRole(RoleNames.SuperAdmin)) return Forbid();

        if (string.IsNullOrWhiteSpace(request.Name))    return BadRequest("Name is required.");
        if (string.IsNullOrWhiteSpace(request.UrlName)) return BadRequest("UrlName is required.");

        await using var db = await _dbFactory.CreateDbContextAsync(ct);

        var urlName = request.UrlName.Trim().ToLowerInvariant();
        if (await db.Organizations.AnyAsync(o => o.UrlName == urlName, ct))
            return BadRequest($"UrlName '{urlName}' is already in use.");

        var org = new Organization
        {
            Name               = request.Name.Trim(),
            UrlName            = urlName,
            DateCreated        = DateTime.UtcNow,
            CreatedByAppUserId = userId.Value
        };

        db.Organizations.Add(org);
        await db.SaveChangesAsync(ct);

        return CreatedAtAction(nameof(GetByIdWithPermissions), new { id = org.Id },
            _mapper2.Map<OrganizationAdminRecord>(org));
    }

    private Guid? GetCurrentUserId()
    {
        var value = User.FindFirstValue(Services.EntraClaimsTransformation.AppUserIdClaimType)
                    ?? User.FindFirstValue(ClaimTypes.NameIdentifier);
        return Guid.TryParse(value, out var id) ? id : null;
    }
}

public sealed record OrganizationListItemResponse(
    Guid Id,
    string Name,
    string UrlName,
    DateTime DateCreated,
    bool CanEdit,
    bool CanDelete);

public sealed record AdminUpdateOrganizationRequest(string Name, string UrlName);

