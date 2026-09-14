using AutoMapper;
using Ben.Data.Common.Constants;
using Ben.Data.Common.Enums;
using Ben.Data.Source.Context;
using Ben.Data.Source.Entities;
using Ben.Data.WebApi.Controllers.Cms;
using Ben.Data.WebApi.Services;
using Ben.Data.WebApi.Services.Events;
using Ben.Service.Models.Admin;
using Ben.Service.RepositoryService.GenericInterfaces;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Ben.Data.WebApi.Controllers.Entities;

/// <summary>
/// The organizer's side of IsHaunted removing their event: what happened, and the appeal (item 235 phase 17b).
/// </summary>
/// <remarks>
/// Whoever may edit the group's events may read it and appeal — the people who wrote the page and can change what was
/// wrong with it. The reviewer's private note is never in the answer.
/// </remarks>
[Route("api/organizations/{orgId:guid}/events/{eventId:guid}/removal")]
public sealed class HostedEventRemovalController : OrgCmsControllerBase
{
    private readonly Services.Access.HostedEventAccess _access;
    private readonly PlatformMessageService _messages;

    public HostedEventRemovalController(
        IDbContextFactory<BenDataContext> dbFactory, IMapper mapper, IOrganizationSecurityService security,
        Services.Access.HostedEventAccess access, PlatformMessageService messages)
        : base(dbFactory, mapper, security)
    {
        _access = access;
        _messages = messages;
    }

    /// <summary>The newest removal of this event, or nothing when it has never been removed.</summary>
    [HttpGet]
    public async Task<ActionResult<HostedEventRemovalRecord?>> Get(Guid orgId, Guid eventId, CancellationToken ct)
    {
        var userId = GetCurrentUserId();
        if (userId is null) return Unauthorized();

        await using var db = await DbFactory.CreateDbContextAsync(ct);
        if (!await _access.CanEditEventAsync(userId.Value, orgId, ct)) return Forbid();
        if (!await db.HostedEvents.AnyAsync(e => e.Id == eventId && e.OrganizationId == orgId, ct)) return NotFound();

        return Ok(ToRecord(await HostedEventRemovals.LatestAsync(db, eventId, ct)));
    }

    [HttpPost("appeal")]
    [Microsoft.AspNetCore.RateLimiting.EnableRateLimiting(Services.RateLimiting.HostedBookingPolicy)]
    public async Task<ActionResult<HostedEventRemovalRecord?>> Appeal(
        Guid orgId, Guid eventId, [FromBody] AppealHostedEventRemovalRequest request, CancellationToken ct)
    {
        var userId = GetCurrentUserId();
        if (userId is null) return Unauthorized();

        await using var db = await DbFactory.CreateDbContextAsync(ct);
        if (!await _access.CanEditEventAsync(userId.Value, orgId, ct)) return Forbid();
        var hosted = await db.HostedEvents.Include(e => e.Organization)
            .FirstOrDefaultAsync(e => e.Id == eventId && e.OrganizationId == orgId, ct);
        if (hosted is null) return NotFound();

        var removal = await HostedEventRemovals.LatestAsync(db, eventId, ct);
        // The audit log compares two copies of the same type, so the before-picture is the row itself.
        var before = removal is null ? null : db.Entry(removal).CurrentValues.ToObject();
        if (HostedEventRemovals.Appeal(hosted, removal, userId.Value, request.Message, DateTime.UtcNow) is { } refusal)
            return BadRequest(refusal);

        await db.SaveChangesAsync(ct);
        if (HttpContext?.RequestServices?.GetService<IAuditLogService>() is { } audit)
            await TryAuditAsync(audit.LogUpdateAsync(nameof(HostedEventRemoval), removal!.Id,
                before!, removal, userId.Value, AppSources.WebApi));

        // The site's own people hear about it in their messages; the queue is on the events list.
        var superAdmins = await (
            from userRole in db.Set<Microsoft.AspNetCore.Identity.IdentityUserRole<Guid>>()
            join role in db.Set<Microsoft.AspNetCore.Identity.IdentityRole<Guid>>() on userRole.RoleId equals role.Id
            where role.Name == RoleNames.SuperAdmin
            select userRole.UserId).Distinct().ToListAsync(ct);
        await _messages.SendAsync(
            $"An appeal about {hosted.Name}",
            $"<p>{System.Net.WebUtility.HtmlEncode(hosted.Organization.Name)} has appealed the removal of "
            + $"<strong>{System.Net.WebUtility.HtmlEncode(hosted.Name)}</strong>.</p><p><a href=\"/admin/events#appeals\">Answer it</a></p>",
            superAdmins, userId.Value, ct);

        return Ok(ToRecord(removal));
    }

    private static HostedEventRemovalRecord? ToRecord(HostedEventRemoval? r) => r is null
        ? null
        : new HostedEventRemovalRecord(r.RemovedUtc, r.CreditReturned, r.AppealState, r.AppealMessage, r.AppealedUtc,
            r.DecisionNote, r.DecidedUtc);
}
