using AutoMapper;
using Ben.Data.Common.Enums;
using Ben.Data.Source.Context;
using Ben.Data.Source.Entities;
using Ben.Data.WebApi.Controllers.Cms;
using Ben.Service.Models.Entities;
using Ben.Service.RepositoryService.GenericInterfaces;
using Ben.Service.RepositoryService.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Ben.Data.WebApi.Controllers.Entities;

/// <summary>
/// Org-scoped address CRUD.  Accessible to org Owner/Administrator and SuperAdmin.
/// Applies geocoding on Create and Update.
/// </summary>
[Route("api/organizations/{orgId:guid}/addresses")]
public sealed class OrganizationAddressCrudController : OrgCmsControllerBase
{
    private readonly IAuditLogService _auditLog;

    public OrganizationAddressCrudController(
        IDbContextFactory<BenDataContext> dbFactory,
        IMapper mapper,
        IOrganizationSecurityService security,
        IAuditLogService auditLog)
        : base(dbFactory, mapper, security)
    {
        _auditLog = auditLog;
    }

    // ── GET ───────────────────────────────────────────────────────────────────

    [HttpGet]
    public async Task<ActionResult<IEnumerable<OrganizationAddressRecord>>> GetAll(
        Guid orgId, CancellationToken ct)
    {
        var userId = GetCurrentUserId();
        if (userId is null) return Unauthorized();
        if (!await IsCmsAuthorizedAsync(userId.Value, orgId, OrganizationSecurityTable.OrganizationAddress, OrganizationSecurityAction.Read, ct))
            return Forbid();

        await using var db = await DbFactory.CreateDbContextAsync(ct);
        var addresses = await db.OrganizationAddresses.AsNoTracking()
            .Where(a => a.OrganizationId == orgId)
            .OrderBy(a => a.SortOrder)
            .ToListAsync(ct);

        return Ok(Mapper.Map<IEnumerable<OrganizationAddressRecord>>(addresses));
    }

    // ── POST ──────────────────────────────────────────────────────────────────

    [HttpPost]
    public async Task<ActionResult<OrganizationAddressRecord>> Create(
        Guid orgId, [FromBody] OrgAddressUpsertRequest request, CancellationToken ct)
    {
        var userId = GetCurrentUserId();
        if (userId is null) return Unauthorized();
        if (!await IsCmsAuthorizedAsync(userId.Value, orgId, OrganizationSecurityTable.OrganizationAddress, OrganizationSecurityAction.Create, ct))
            return Forbid();

        if (string.IsNullOrWhiteSpace(request.StreetAddress1) || string.IsNullOrWhiteSpace(request.City))
            return BadRequest("StreetAddress1 and City are required.");

        await using var db = await DbFactory.CreateDbContextAsync(ct);

        var entity = new OrganizationAddress
        {
            Id                        = Guid.NewGuid(),
            OrganizationId            = orgId,
            OrganizationAddressTypeId = request.OrganizationAddressTypeId,
            StreetAddress1            = request.StreetAddress1.Trim(),
            StreetAddress2            = request.StreetAddress2?.Trim(),
            City                      = request.City.Trim(),
            State                     = request.State.Trim(),
            ZipCode                   = request.ZipCode.Trim(),
            Country                   = request.Country.Trim(),
            Visibility                = request.Visibility,
            PublicDisplayMode         = request.PublicDisplayMode,
            MemberDisplayMode         = request.MemberDisplayMode,
            IsSearchable              = request.IsSearchable,
            SearchVisibility          = request.SearchVisibility,
            SearchRadiusMiles         = request.SearchRadiusMiles,
            SortOrder                 = request.SortOrder,
            Latitude                  = request.Latitude,    // use client coords if available
            Longitude                 = request.Longitude,
            DateCreated               = DateTime.UtcNow,
            CreatedByAppUserId        = userId.Value
        };

        // Only geocode if the client didn't already resolve coordinates
        if (!entity.Latitude.HasValue || !entity.Longitude.HasValue)
            await ApplyGeocodingAsync(entity, ct);

        db.OrganizationAddresses.Add(entity);
        await db.SaveChangesAsync(ct);
        _ = TryAuditAsync(_auditLog.LogCreateAsync(nameof(OrganizationAddress), entity.Id, entity, userId.Value, AppSources.WebApi, ct));

        return CreatedAtAction(nameof(GetAll), new { orgId }, Mapper.Map<OrganizationAddressRecord>(entity));
    }

    // ── PUT ───────────────────────────────────────────────────────────────────

    [HttpPut("{addressId:guid}")]
    public async Task<ActionResult<OrganizationAddressRecord>> Update(
        Guid orgId, Guid addressId, [FromBody] OrgAddressUpsertRequest request, CancellationToken ct)
    {
        var userId = GetCurrentUserId();
        if (userId is null) return Unauthorized();
        if (!await IsCmsAuthorizedAsync(userId.Value, orgId, OrganizationSecurityTable.OrganizationAddress, OrganizationSecurityAction.Update, ct))
            return Forbid();

        await using var db = await DbFactory.CreateDbContextAsync(ct);
        var before = await db.OrganizationAddresses.AsNoTracking()
            .FirstOrDefaultAsync(a => a.Id == addressId && a.OrganizationId == orgId, ct);
        if (before is null) return NotFound();

        var entity = await db.OrganizationAddresses
            .FirstOrDefaultAsync(a => a.Id == addressId && a.OrganizationId == orgId, ct);

        entity!.OrganizationAddressTypeId = request.OrganizationAddressTypeId;
        entity.StreetAddress1            = request.StreetAddress1.Trim();
        entity.StreetAddress2            = request.StreetAddress2?.Trim();
        entity.City                      = request.City.Trim();
        entity.State                     = request.State.Trim();
        entity.ZipCode                   = request.ZipCode.Trim();
        entity.Country                   = request.Country.Trim();
        entity.Visibility                = request.Visibility;
        entity.PublicDisplayMode         = request.PublicDisplayMode;
        entity.MemberDisplayMode         = request.MemberDisplayMode;
        entity.IsSearchable              = request.IsSearchable;
        entity.SearchVisibility          = request.SearchVisibility;
        entity.SearchRadiusMiles         = request.SearchRadiusMiles;
        entity.SortOrder                 = request.SortOrder;
        // Use client-provided coords if available; otherwise re-geocode
        if (request.Latitude.HasValue && request.Longitude.HasValue)
        {
            entity.Latitude  = request.Latitude;
            entity.Longitude = request.Longitude;
        }
        else
        {
            await ApplyGeocodingAsync(entity, ct);
        }
        entity.DateUpdated               = DateTime.UtcNow;
        entity.UpdatedByAppUserId        = userId.Value;

        await db.SaveChangesAsync(ct);
        _ = TryAuditAsync(_auditLog.LogUpdateAsync(nameof(OrganizationAddress), addressId, before, entity, userId.Value, AppSources.WebApi, ct));

        return Ok(Mapper.Map<OrganizationAddressRecord>(entity));
    }

    // ── DELETE ────────────────────────────────────────────────────────────────

    [HttpDelete("{addressId:guid}")]
    public async Task<IActionResult> Delete(Guid orgId, Guid addressId, CancellationToken ct)
    {
        var userId = GetCurrentUserId();
        if (userId is null) return Unauthorized();
        if (!await IsCmsAuthorizedAsync(userId.Value, orgId, OrganizationSecurityTable.OrganizationAddress, OrganizationSecurityAction.Delete, ct))
            return Forbid();

        await using var db = await DbFactory.CreateDbContextAsync(ct);
        var entity = await db.OrganizationAddresses
            .FirstOrDefaultAsync(a => a.Id == addressId && a.OrganizationId == orgId, ct);
        if (entity is null) return NotFound();

        db.OrganizationAddresses.Remove(entity);
        await db.SaveChangesAsync(ct);
        _ = TryAuditAsync(_auditLog.LogDeleteAsync(nameof(OrganizationAddress), addressId, entity, userId.Value, AppSources.WebApi, ct));

        return NoContent();
    }

    // ── SpecificMembers access list ───────────────────────────────────────────

    [HttpGet("{addressId:guid}/member-access")]
    public async Task<ActionResult<IEnumerable<OrganizationAddressMemberAccessRecord>>> GetMemberAccess(
        Guid orgId, Guid addressId, CancellationToken ct)
    {
        var userId = GetCurrentUserId();
        if (userId is null) return Unauthorized();
        if (!await IsCmsAuthorizedAsync(userId.Value, orgId, OrganizationSecurityTable.OrganizationAddressMemberAccess, OrganizationSecurityAction.Read, ct))
            return Forbid();

        await using var db = await DbFactory.CreateDbContextAsync(ct);
        if (!await db.OrganizationAddresses.AnyAsync(a => a.Id == addressId && a.OrganizationId == orgId, ct))
            return NotFound();
        var items = await db.OrganizationAddressMemberAccesses.AsNoTracking()
            .Where(x => x.OrganizationAddressId == addressId)
            .ToListAsync(ct);
        return Ok(Mapper.Map<IEnumerable<OrganizationAddressMemberAccessRecord>>(items));
    }

    [HttpPost("{addressId:guid}/member-access")]
    public async Task<ActionResult<OrganizationAddressMemberAccessRecord>> AddMemberAccess(
        Guid orgId, Guid addressId, [FromBody] AddAddressMemberAccessRequest request, CancellationToken ct)
    {
        var userId = GetCurrentUserId();
        if (userId is null) return Unauthorized();
        if (!await IsCmsAuthorizedAsync(userId.Value, orgId, OrganizationSecurityTable.OrganizationAddressMemberAccess, OrganizationSecurityAction.Create, ct))
            return Forbid();

        await using var db = await DbFactory.CreateDbContextAsync(ct);
        if (!await db.OrganizationAddresses.AnyAsync(a => a.Id == addressId && a.OrganizationId == orgId, ct))
            return NotFound();
        if (!await db.OrganizationUserMemberships.AnyAsync(m => m.Id == request.OrganizationUserMembershipId && m.OrganizationId == orgId, ct))
            return BadRequest("Membership not found in this organisation.");
        if (await db.OrganizationAddressMemberAccesses.AnyAsync(x => x.OrganizationAddressId == addressId && x.OrganizationUserMembershipId == request.OrganizationUserMembershipId, ct))
            return Conflict("This member already has access.");

        var entity = new OrganizationAddressMemberAccess
        {
            Id = Guid.NewGuid(), OrganizationAddressId = addressId,
            OrganizationUserMembershipId = request.OrganizationUserMembershipId,
            DateCreated = DateTime.UtcNow, CreatedByAppUserId = userId.Value
        };
        db.OrganizationAddressMemberAccesses.Add(entity);
        await db.SaveChangesAsync(ct);
        _ = TryAuditAsync(_auditLog.LogCreateAsync(nameof(OrganizationAddressMemberAccess), entity.Id, entity, userId.Value, AppSources.WebApi, ct));
        return CreatedAtAction(nameof(GetMemberAccess), new { orgId, addressId }, Mapper.Map<OrganizationAddressMemberAccessRecord>(entity));
    }

    [HttpDelete("{addressId:guid}/member-access/{accessId:guid}")]
    public async Task<IActionResult> RemoveMemberAccess(Guid orgId, Guid addressId, Guid accessId, CancellationToken ct)
    {
        var userId = GetCurrentUserId();
        if (userId is null) return Unauthorized();
        if (!await IsCmsAuthorizedAsync(userId.Value, orgId, OrganizationSecurityTable.OrganizationAddressMemberAccess, OrganizationSecurityAction.Delete, ct))
            return Forbid();

        await using var db = await DbFactory.CreateDbContextAsync(ct);
        if (!await db.OrganizationAddresses.AnyAsync(a => a.Id == addressId && a.OrganizationId == orgId, ct))
            return NotFound();
        var entity = await db.OrganizationAddressMemberAccesses.FirstOrDefaultAsync(x => x.Id == accessId && x.OrganizationAddressId == addressId, ct);
        if (entity is null) return NotFound();
        db.OrganizationAddressMemberAccesses.Remove(entity);
        await db.SaveChangesAsync(ct);
        _ = TryAuditAsync(_auditLog.LogDeleteAsync(nameof(OrganizationAddressMemberAccess), accessId, entity, userId.Value, AppSources.WebApi, ct));
        return NoContent();
    }

    // ── Geocoding helper ──────────────────────────────────────────────────────

    private static async Task ApplyGeocodingAsync(OrganizationAddress entity, CancellationToken ct)
    {
        var result = await AddressGeocodingService.TryResolveCoordinatesAsync(
            entity.StreetAddress1, entity.StreetAddress2,
            entity.City, entity.State, entity.ZipCode, entity.Country, ct);
        entity.Latitude              = result.Latitude;
        entity.Longitude             = result.Longitude;
        entity.GeocodingResponseJson = result.RawResponseJson;
        entity.GeocodingResultType   = result.ResultType;
    }
}

public sealed record OrgAddressUpsertRequest(
    Guid   OrganizationAddressTypeId,
    string StreetAddress1,
    string? StreetAddress2,
    string City,
    string State,
    string ZipCode,
    string Country,
    int    SortOrder,
    OrganizationAddressVisibility  Visibility        = OrganizationAddressVisibility.Private,
    OrganizationAddressDisplayMode PublicDisplayMode = OrganizationAddressDisplayMode.Hidden,
    OrganizationAddressDisplayMode MemberDisplayMode = OrganizationAddressDisplayMode.FullAddressAndMap,
    bool   IsSearchable      = false,
    OrganizationAddressVisibility  SearchVisibility  = OrganizationAddressVisibility.Public,
    double? SearchRadiusMiles = null,
    decimal? Latitude  = null,
    decimal? Longitude = null);

public sealed record AddAddressMemberAccessRequest(Guid OrganizationUserMembershipId);
