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
/// Manages the map display configuration for a single OrganizationAddress.
/// One config row per address; absent row = not shown on map.
/// Requires Organization-Update permission or SuperAdmin.
/// </summary>
[Route("api/organizations/{orgId:guid}/addresses/{addressId:guid}/map-config")]
[Authorize]
public sealed class OrganizationAddressMapConfigController : ControllerBase
{
    private readonly IDbContextFactory<BenDataContext> _dbFactory;
    private readonly IMapper _mapper;
    private readonly IOrganizationSecurityService _security;

    public OrganizationAddressMapConfigController(
        IDbContextFactory<BenDataContext> dbFactory,
        IMapper mapper,
        IOrganizationSecurityService security)
    {
        _dbFactory = dbFactory;
        _mapper    = mapper;
        _security  = security;
    }

    private Guid? CurrentUserId()
    {
        var c = User.FindFirst("app_user_id")?.Value;
        if (c is not null && Guid.TryParse(c, out var id1)) return id1;
        var s = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
        return s is not null && Guid.TryParse(s, out var id2) ? id2 : null;
    }

    // ── GET /api/organizations/{orgId}/addresses/{addressId}/map-config ──────
    /// <summary>Returns the map config for the address, or a default record if none is saved.</summary>
    [HttpGet]
    public async Task<ActionResult<AddressMapConfigRecord?>> Get(
        Guid orgId, Guid addressId, CancellationToken ct)
    {
        var userId       = CurrentUserId();
        if (userId is null) return Unauthorized();
        var isSuperAdmin = User.IsInRole(RoleNames.SuperAdmin);

        if (!isSuperAdmin)
        {
            var canRead = await _security.HasAccessAsync(userId.Value, orgId,
                OrganizationSecurityTable.OrganizationAddress, OrganizationSecurityAction.Read, ct);
            if (!canRead) return Forbid();
        }

        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var cfg = await db.OrganizationAddressMapConfigs.AsNoTracking()
            .FirstOrDefaultAsync(c => c.OrganizationAddressId == addressId, ct);

        if (cfg is null) return NotFound();
        return Ok(_mapper.Map<AddressMapConfigRecord>(cfg));
    }

    // ── PUT /api/organizations/{orgId}/addresses/{addressId}/map-config ──────
    /// <summary>Upserts the map config for the address.</summary>
    [HttpPut]
    public async Task<ActionResult<AddressMapConfigRecord>> Upsert(
        Guid orgId, Guid addressId,
        [FromBody] UpsertAddressMapConfigRequest request,
        CancellationToken ct)
    {
        var userId       = CurrentUserId();
        if (userId is null) return Unauthorized();
        var isSuperAdmin = User.IsInRole(RoleNames.SuperAdmin);

        if (!isSuperAdmin)
        {
            var canUpdate = await _security.HasAccessAsync(userId.Value, orgId,
                OrganizationSecurityTable.OrganizationAddress, OrganizationSecurityAction.Update, ct);
            if (!canUpdate) return Forbid();
        }

        await using var db = await _dbFactory.CreateDbContextAsync(ct);

        // Verify the address belongs to this org
        if (!await db.OrganizationAddresses.AnyAsync(
                a => a.Id == addressId && a.OrganizationId == orgId, ct))
            return NotFound("Address not found in this organization.");

        var cfg = await db.OrganizationAddressMapConfigs
            .FirstOrDefaultAsync(c => c.OrganizationAddressId == addressId, ct);

        var now = DateTime.UtcNow;
        if (cfg is null)
        {
            cfg = new OrganizationAddressMapConfig
            {
                Id                    = Guid.NewGuid(),
                OrganizationAddressId = addressId,
                DateCreated           = now,
                CreatedByAppUserId    = userId.Value,
            };
            db.OrganizationAddressMapConfigs.Add(cfg);
        }
        else
        {
            cfg.DateUpdated        = now;
            cfg.UpdatedByAppUserId = userId.Value;
        }

        cfg.IsOnMap             = request.IsOnMap;
        cfg.ShowMarker          = request.ShowMarker;
        cfg.ShowRegion          = request.ShowRegion;
        cfg.RegionRadiusMiles   = Math.Max(0.1, request.RegionRadiusMiles);
        cfg.MarkerColor         = request.MarkerColor?.Trim() ?? "#e63535";
        cfg.MarkerIconKey       = string.IsNullOrWhiteSpace(request.MarkerIconKey) ? null : request.MarkerIconKey.Trim();
        cfg.RegionFillColor     = request.RegionFillColor?.Trim() ?? "#3388ff";
        cfg.RegionFillOpacity   = Math.Clamp(request.RegionFillOpacity, 0.0, 1.0);
        cfg.RegionStrokeColor   = request.RegionStrokeColor?.Trim() ?? "#1155cc";
        cfg.RegionStrokeOpacity = Math.Clamp(request.RegionStrokeOpacity, 0.0, 1.0);
        cfg.RegionStrokeWidth   = Math.Max(0.0, request.RegionStrokeWidth);

        await db.SaveChangesAsync(ct);
        return Ok(_mapper.Map<AddressMapConfigRecord>(cfg));
    }

    // ── DELETE /api/organizations/{orgId}/addresses/{addressId}/map-config ───
    /// <summary>Removes the map config (resets address to "not on map").</summary>
    [HttpDelete]
    public async Task<IActionResult> Delete(
        Guid orgId, Guid addressId, CancellationToken ct)
    {
        var userId       = CurrentUserId();
        if (userId is null) return Unauthorized();
        var isSuperAdmin = User.IsInRole(RoleNames.SuperAdmin);

        if (!isSuperAdmin)
        {
            var canUpdate = await _security.HasAccessAsync(userId.Value, orgId,
                OrganizationSecurityTable.OrganizationAddress, OrganizationSecurityAction.Update, ct);
            if (!canUpdate) return Forbid();
        }

        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var cfg = await db.OrganizationAddressMapConfigs
            .FirstOrDefaultAsync(c => c.OrganizationAddressId == addressId, ct);
        if (cfg is null) return NoContent();

        db.OrganizationAddressMapConfigs.Remove(cfg);
        await db.SaveChangesAsync(ct);
        return NoContent();
    }
}

public sealed record UpsertAddressMapConfigRequest(
    bool IsOnMap,
    bool ShowMarker,
    bool ShowRegion,
    double RegionRadiusMiles,
    string? MarkerColor,
    string? MarkerIconKey,
    string? RegionFillColor,
    double RegionFillOpacity,
    string? RegionStrokeColor,
    double RegionStrokeOpacity,
    double RegionStrokeWidth);
