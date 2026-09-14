using AutoMapper;
using Ben.Data.Source.Context;
using Ben.Data.WebApi.Controllers.Cms;
using Ben.Data.WebApi.Services.Events;
using Ben.Service.Models.Entities;
using Ben.Service.RepositoryService.GenericInterfaces;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Ben.Data.WebApi.Controllers.Entities;

/// <summary>
/// The host's numbers for one event, on one card (item 235 phase 17a, audit finding A5).
/// </summary>
/// <remarks>
/// Counts only — no names, no addresses — but counts of bookings, so it takes the right to read the board: a member
/// who may not see who asked should not learn how many did.
/// </remarks>
[Route("api/organizations/{orgId:guid}/events/{eventId:guid}/summary")]
public sealed class HostedEventSummaryController : OrgCmsControllerBase
{
    private readonly Services.Access.HostedEventAccess _access;

    public HostedEventSummaryController(
        IDbContextFactory<BenDataContext> dbFactory, IMapper mapper, IOrganizationSecurityService security,
        Services.Access.HostedEventAccess access)
        : base(dbFactory, mapper, security)
    {
        _access = access;
    }

    [HttpGet]
    public async Task<ActionResult<HostedEventSummaryRecord>> Get(Guid orgId, Guid eventId, CancellationToken ct)
    {
        var userId = GetCurrentUserId();
        if (userId is null) return Unauthorized();

        await using var db = await DbFactory.CreateDbContextAsync(ct);
        if (!await _access.CanReadBookingsAsync(userId.Value, orgId, eventId, db, ct)) return Forbid();
        if (!await db.HostedEvents.AnyAsync(e => e.Id == eventId && e.OrganizationId == orgId, ct)) return NotFound();

        return Ok(await HostedEventSummary.ReadAsync(db, eventId, ct));
    }
}
