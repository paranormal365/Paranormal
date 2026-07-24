using AutoMapper;
using Ben.Data.Common.Enums;
using Ben.Data.Source.Context;
using Ben.Data.Source.Entities;
using Ben.Service.Models.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Ben.Data.WebApi.Controllers.Entities;

/// <summary>
/// Manages an organization's area of operation (radius mode).
/// GET/PUT/DELETE require org Owner/Admin or SuperAdmin.
/// The center coordinates are never returned on public endpoints.
/// </summary>
[ApiController]
[Route("api/organizations/{orgId:guid}/area-of-operation")]
[Authorize]
public sealed class OrganizationAreaOfOperationController : BenControllerBase
{
    private readonly IDbContextFactory<BenDataContext> _db;
    private readonly IMapper _mapper;

    public OrganizationAreaOfOperationController(
        IDbContextFactory<BenDataContext> db, IMapper mapper)
    {
        _db = db;
        _mapper = mapper;
    }

    /// <summary>Returns the area of operation including private coordinates (org admin / SuperAdmin only).</summary>
    [HttpGet]
    public async Task<ActionResult<OrganizationAreaOfOperationRecord>> Get(Guid orgId, CancellationToken ct)
    {
        if (!await IsOrgAdminOrSuperAsync(orgId, ct)) return Forbid();
        await using var db = await _db.CreateDbContextAsync(ct);
        var entity = await db.OrganizationAreaOfOperations
            .AsNoTracking()
            .FirstOrDefaultAsync(a => a.OrganizationId == orgId, ct);
        if (entity is null) return NotFound();
        return Ok(_mapper.Map<OrganizationAreaOfOperationRecord>(entity));
    }

    /// <summary>
    /// Creates or replaces the area of operation for the organization.
    /// Accepts a center address label + pre-geocoded lat/lng + radius.
    /// The lat/lng should be a city/town center — NOT a personal home address.
    /// </summary>
    [HttpPut]
    public async Task<ActionResult<OrganizationAreaOfOperationRecord>> Upsert(
        Guid orgId, [FromBody] UpsertAreaOfOperationRequest request, CancellationToken ct)
    {
        if (!await IsOrgAdminOrSuperAsync(orgId, ct)) return Forbid();
        var userId = GetCurrentUserId();
        await using var db = await _db.CreateDbContextAsync(ct);

        var existing = await db.OrganizationAreaOfOperations
            .FirstOrDefaultAsync(a => a.OrganizationId == orgId, ct);

        if (existing is null)
        {
            existing = new OrganizationAreaOfOperation
            {
                Id                 = Guid.NewGuid(),
                OrganizationId     = orgId,
                DateCreated        = DateTime.UtcNow,
                CreatedByAppUserId = userId,
            };
            db.OrganizationAreaOfOperations.Add(existing);
        }

        existing.RadiusMiles         = request.RadiusMiles;
        existing.CenterLatitude      = request.CenterLatitude;
        existing.CenterLongitude     = request.CenterLongitude;
        existing.DisplayLabel        = request.DisplayLabel?.Trim();
        existing.DateUpdated         = DateTime.UtcNow;
        existing.UpdatedByAppUserId  = userId == Guid.Empty ? null : userId;

        // Also update org-level acceptance flags if provided
        var org = await db.Organizations.FirstOrDefaultAsync(o => o.Id == orgId, ct);
        if (org is not null)
        {
            org.IsAcceptingClients         = request.IsAcceptingClients;
            org.AcceptsClientsOutsideRange = request.AcceptsClientsOutsideRange;
            org.DateUpdated                = DateTime.UtcNow;
            org.UpdatedByAppUserId         = userId == Guid.Empty ? null : userId;
        }

        await db.SaveChangesAsync(ct);
        return Ok(_mapper.Map<OrganizationAreaOfOperationRecord>(existing));
    }

    /// <summary>Removes the area of operation configuration.</summary>
    [HttpDelete]
    public async Task<IActionResult> Delete(Guid orgId, CancellationToken ct)
    {
        if (!await IsOrgAdminOrSuperAsync(orgId, ct)) return Forbid();
        await using var db = await _db.CreateDbContextAsync(ct);
        var entity = await db.OrganizationAreaOfOperations
            .FirstOrDefaultAsync(a => a.OrganizationId == orgId, ct);
        if (entity is null) return NotFound();
        db.OrganizationAreaOfOperations.Remove(entity);
        await db.SaveChangesAsync(ct);
        return NoContent();
    }

    /// <summary>Updates only the acceptance flags without touching the area geometry.</summary>
    [HttpPut("acceptance")]
    public async Task<IActionResult> UpdateAcceptance(
        Guid orgId, [FromBody] UpdateClientAcceptanceRequest request, CancellationToken ct)
    {
        if (!await IsOrgAdminOrSuperAsync(orgId, ct)) return Forbid();
        var userId = GetCurrentUserId();
        await using var db = await _db.CreateDbContextAsync(ct);
        var org = await db.Organizations.FirstOrDefaultAsync(o => o.Id == orgId, ct);
        if (org is null) return NotFound();
        org.IsAcceptingClients         = request.IsAcceptingClients;
        org.AcceptsClientsOutsideRange = request.AcceptsClientsOutsideRange;
        org.DateUpdated                = DateTime.UtcNow;
        org.UpdatedByAppUserId         = userId == Guid.Empty ? null : userId;
        await db.SaveChangesAsync(ct);
        return NoContent();
    }

    // ── Auth helpers ──────────────────────────────────────────────────────────

    private async Task<bool> IsOrgAdminOrSuperAsync(Guid orgId, CancellationToken ct)
    {
        if (User.IsInRole(Ben.Data.Common.Constants.RoleNames.SuperAdmin)) return true;
        var userId = GetCurrentUserId();
        if (userId == Guid.Empty) return false;
        await using var db = await _db.CreateDbContextAsync(ct);
        return await db.OrganizationUserMemberships.AnyAsync(
            m => m.OrganizationId == orgId
              && m.AppUserId == userId
              && m.IsActive
              && (m.Role == OrganizationMemberRole.Owner || m.Role == OrganizationMemberRole.Administrator),
            ct);
    }
}

// ── Request records ───────────────────────────────────────────────────────────

public sealed record UpsertAreaOfOperationRequest(
    decimal RadiusMiles,
    decimal CenterLatitude,
    decimal CenterLongitude,
    string? DisplayLabel,
    bool IsAcceptingClients,
    bool AcceptsClientsOutsideRange);

public sealed record UpdateClientAcceptanceRequest(
    bool IsAcceptingClients,
    bool AcceptsClientsOutsideRange);
