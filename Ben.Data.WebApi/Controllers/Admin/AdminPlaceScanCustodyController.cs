using Ben.Data.Common.Constants;
using Ben.Data.WebApi.Services;
using Ben.Data.WebApi.Services.Places;
using Ben.Data.WebApi.Services.Venues;
using Ben.Service.Models.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Ben.Data.WebApi.Controllers.Admin;

/// <summary>
/// A SuperAdmin handing a place's room scans to a group, or taking them back (LiDAR plan, phase 1;
/// Ben, 10/28/2026).
/// </summary>
/// <remarks>
/// <para>For a building whose rightful keeper has not proved a venue claim yet — a historical
/// society that runs the house, a family that asked for help. The grant ranks after a verified
/// venue and before the first-scanner fallback (<see cref="PlaceScans.CustodianAsync"/>), so it is
/// refused while a venue holds custody, and a venue verified later closes it.</para>
///
/// <para>Audited like every admin act, and the group is told.</para>
/// </remarks>
[ApiController]
[Authorize(Policy = RoleNames.SuperAdmin)]
[Route("api/admin/places/{placeId:guid}/scan-custody")]
public sealed class AdminPlaceScanCustodyController(
    IDbContextFactory<BenDataContext> dbFactory,
    IAuditLogService auditLog,
    PlatformMessageService messages,
    TimeProvider? clock = null) : BenControllerBase
{
    private DateTime Now => (clock ?? TimeProvider.System).GetUtcNow().UtcDateTime;

    /// <summary>The grant at this place, open or last closed; 204 when there has never been one.</summary>
    [HttpGet]
    public async Task<ActionResult<PlaceScanCustodyRecord?>> Get(Guid placeId, CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var grant = await db.PlaceScanCustodies.AsNoTracking()
            .Include(c => c.Organization)
            .FirstOrDefaultAsync(c => c.PlaceId == placeId, ct);
        return Ok(grant is null ? null : Record(grant));
    }

    /// <summary>Gives the place's scans to a group.</summary>
    [HttpPut]
    public async Task<ActionResult<PlaceScanCustodyRecord>> Grant(
        Guid placeId, [FromBody] GrantScanCustodyRequest request, CancellationToken ct)
    {
        var userId = GetCurrentUserId();
        await using var db = await dbFactory.CreateDbContextAsync(ct);

        var place = await db.Places.AsNoTracking().FirstOrDefaultAsync(p => p.Id == placeId, ct);
        if (place is null) return NotFound("That place doesn't exist.");
        var organization = await db.Organizations.AsNoTracking().FirstOrDefaultAsync(o => o.Id == request.OrganizationId, ct);
        if (organization is null) return NotFound("That group doesn't exist.");

        if (await VenueGrants.VerifiedVenueAtAsync(db, placeId, ct) is { } venue)
            return Conflict($"{venue.Organization.Name} is this place's confirmed venue, so it holds custody.");

        var now = Now;
        var note = request.Note?.Trim() is { Length: > 0 } n ? n[..Math.Min(n.Length, 2000)] : null;
        var grant = await db.PlaceScanCustodies.Include(c => c.Organization).FirstOrDefaultAsync(c => c.PlaceId == placeId, ct);
        var before = grant is null ? null : Record(grant);

        if (grant is null)
        {
            grant = new PlaceScanCustody { Id = Guid.NewGuid(), PlaceId = placeId };
            db.PlaceScanCustodies.Add(grant);
        }
        grant.OrganizationId = organization.Id;
        grant.GrantedUtc = now;
        grant.GrantedByAppUserId = userId;
        grant.Note = note;
        grant.ClosedUtc = null;
        grant.ClosedReason = null;
        await db.SaveChangesAsync(ct);

        var after = Record(grant, organization.Name);
        await TryAuditAsync(() => before is null
            ? auditLog.LogCreateAsync(nameof(PlaceScanCustody), grant.Id, after, userId, AppSources.WebApi)
            : auditLog.LogUpdateAsync(nameof(PlaceScanCustody), grant.Id, before, after, userId, AppSources.WebApi));

        await messages.SendAsync(
            $"{organization.Name} now looks after the room scans at {place.Name}",
            $"<p>A site administrator gave your group the care of the room scans at "
            + $"<strong>{VenueNotices.Safe(place.Name)}</strong>. Your owners and administrators may replace them, "
            + "remove them, and accept or decline scans other people offer.</p>"
            + (note is null ? "" : $"<p>They said: “{VenueNotices.Safe(note)}”</p>"),
            await VenueNotices.PeopleWhoAnswerForAsync(db, organization.Id, ct), userId, ct);

        return Ok(after);
    }

    /// <summary>Takes the grant back. The scans stay; who looks after them falls back to the ordinary rule.</summary>
    [HttpDelete]
    public async Task<IActionResult> Close(Guid placeId, CancellationToken ct)
    {
        var userId = GetCurrentUserId();
        await using var db = await dbFactory.CreateDbContextAsync(ct);

        var grant = await db.PlaceScanCustodies.Include(c => c.Organization)
            .FirstOrDefaultAsync(c => c.PlaceId == placeId && c.ClosedUtc == null, ct);
        if (grant is null) return NoContent();

        var before = Record(grant);
        grant.ClosedUtc = Now;
        grant.ClosedReason = PlaceScanCustody.ClosedByAdmin;
        await db.SaveChangesAsync(ct);

        await TryAuditAsync(() => auditLog.LogUpdateAsync(
            nameof(PlaceScanCustody), grant.Id, before, Record(grant), userId, AppSources.WebApi));
        return NoContent();
    }

    private static PlaceScanCustodyRecord Record(PlaceScanCustody grant, string? organizationName = null)
        => new(grant.PlaceId, grant.OrganizationId,
               organizationName ?? grant.Organization?.Name ?? "",
               grant.GrantedUtc, grant.Note, grant.ClosedUtc, grant.ClosedReason);
}
