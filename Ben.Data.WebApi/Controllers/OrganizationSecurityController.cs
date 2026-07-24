using Ben.Data.Common.Enums;
using Ben.Data.Source.Context;
using Ben.Service.RepositoryService.GenericInterfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Ben.Data.WebApi.Controllers;

[ApiController]
[Authorize]
[Route("api/organizations/{organizationId:guid}/security")]
public class OrganizationSecurityController(IOrganizationSecurityService organizationSecurityService,
    IDbContextFactory<BenDataContext> dbFactory) : BenControllerBase
{
    private readonly IOrganizationSecurityService _organizationSecurityService = organizationSecurityService;

    [HttpGet("my-access")]
    public async Task<ActionResult<bool>> CheckMyAccess(
        Guid organizationId,
        [FromQuery] OrganizationSecurityTable table,
        [FromQuery] OrganizationSecurityAction action,
        CancellationToken cancellationToken)
    {
        var appUserId = GetCurrentUserIdOrThrow();
        var hasAccess = await _organizationSecurityService.HasAccessAsync(appUserId, organizationId, table, action, cancellationToken);
        return Ok(hasAccess);
    }

    [HttpPost("check-access")]
    public async Task<ActionResult<bool>> CheckAccess(
        Guid organizationId,
        [FromBody] CheckOrganizationAccessRequest request,
        CancellationToken cancellationToken)
    {
        var actingUserId = GetCurrentUserIdOrThrow();

        // Users can check themselves; org admins/superadmin can check others.
        if (actingUserId != request.AppUserId)
        {
            await _organizationSecurityService.GetOrganizationUsersAsync(organizationId, actingUserId, cancellationToken);
        }

        var hasAccess = await _organizationSecurityService.HasAccessAsync(
            request.AppUserId,
            organizationId,
            request.Table,
            request.Action,
            cancellationToken);

        return Ok(hasAccess);
    }

    [HttpGet("users")]
    public async Task<ActionResult<IEnumerable<OrganizationUserMembershipResponse>>> GetOrganizationUsers(
        Guid organizationId,
        CancellationToken cancellationToken)
    {
        var actingUserId = GetCurrentUserIdOrThrow();
        var members = await _organizationSecurityService.GetOrganizationUsersAsync(organizationId, actingUserId, cancellationToken);

        // Fetch display names in one query
        var userIds = members.Select(m => m.AppUserId).ToHashSet();
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var names = await db.AppUsers
            .Where(u => userIds.Contains(u.Id))
            .Select(u => new { u.Id, Label = u.DisplayName ?? u.Email ?? u.UserName ?? u.Id.ToString() })
            .ToDictionaryAsync(u => u.Id, u => u.Label, cancellationToken);

        return Ok(members.Select(m => new OrganizationUserMembershipResponse
        {
            MembershipId   = m.Id,
            OrganizationId = m.OrganizationId,
            AppUserId      = m.AppUserId,
            DisplayName    = names.GetValueOrDefault(m.AppUserId),
            Role           = m.Role,
            IsActive       = m.IsActive,
            DateCreated    = m.DateCreated,
            DateUpdated    = m.DateUpdated
        }));
    }

    [HttpPut("users/{targetUserId:guid}/membership")]
    public async Task<ActionResult<OrganizationUserMembershipResponse>> UpsertMembership(
        Guid organizationId,
        Guid targetUserId,
        [FromBody] UpsertOrganizationMembershipRequest request,
        CancellationToken cancellationToken)
    {
        var actingUserId = GetCurrentUserIdOrThrow();

        var membership = await _organizationSecurityService.UpsertMembershipAsync(
            organizationId,
            targetUserId,
            request.Role,
            request.IsActive,
            actingUserId,
            cancellationToken);

        return Ok(new OrganizationUserMembershipResponse
        {
            MembershipId = membership.Id,
            OrganizationId = membership.OrganizationId,
            AppUserId = membership.AppUserId,
            Role = membership.Role,
            IsActive = membership.IsActive,
            DateCreated = membership.DateCreated,
            DateUpdated = membership.DateUpdated
        });
    }

    [HttpPut("users/{targetUserId:guid}/grants")]
    public async Task<ActionResult<OrganizationAccessGrantResponse>> SetGrant(
        Guid organizationId,
        Guid targetUserId,
        [FromBody] SetOrganizationGrantRequest request,
        CancellationToken cancellationToken)
    {
        var actingUserId = GetCurrentUserIdOrThrow();

        var grant = await _organizationSecurityService.SetAccessGrantAsync(
            organizationId,
            targetUserId,
            request.Table,
            request.Actions,
            actingUserId,
            cancellationToken);

        return Ok(new OrganizationAccessGrantResponse
        {
            GrantId = grant.Id,
            OrganizationId = grant.OrganizationId,
            AppUserId = grant.AppUserId,
            Table = grant.TableName,
            Actions = grant.Actions,
            DateCreated = grant.DateCreated,
            DateUpdated = grant.DateUpdated
        });
    }

    /// <summary>
    /// Deletes one or all access grants for a user in an organization.
    /// Omit <paramref name="table"/> to delete all grants for the user.
    /// </summary>
    [HttpDelete("users/{targetUserId:guid}/grants")]
    public async Task<ActionResult> DeleteGrant(
        Guid organizationId,
        Guid targetUserId,
        [FromQuery] OrganizationSecurityTable? table,
        CancellationToken cancellationToken)
    {
        var actingUserId = GetCurrentUserIdOrThrow();
        var deleted = await _organizationSecurityService.DeleteGrantAsync(
            organizationId, targetUserId, table, actingUserId, cancellationToken);
        return Ok(new { deleted });
    }

    public sealed class UpsertOrganizationMembershipRequest
    {
        public OrganizationMemberRole Role { get; set; } = OrganizationMemberRole.Member;
        public bool IsActive { get; set; } = true;
    }

    public sealed class CheckOrganizationAccessRequest
    {
        public Guid AppUserId { get; set; }
        public OrganizationSecurityTable Table { get; set; }
        public OrganizationSecurityAction Action { get; set; }
    }

    public sealed class SetOrganizationGrantRequest
    {
        public OrganizationSecurityTable Table { get; set; }
        public OrganizationSecurityAction Actions { get; set; }
    }

    public sealed class OrganizationUserMembershipResponse
    {
        public Guid MembershipId { get; set; }
        public Guid OrganizationId { get; set; }
        public Guid AppUserId { get; set; }
        public string? DisplayName { get; set; }
        public OrganizationMemberRole Role { get; set; }
        public bool IsActive { get; set; }
        public DateTime DateCreated { get; set; }
        public DateTime? DateUpdated { get; set; }
    }

    public sealed class OrganizationAccessGrantResponse
    {
        public Guid GrantId { get; set; }
        public Guid OrganizationId { get; set; }
        public Guid AppUserId { get; set; }
        public OrganizationSecurityTable Table { get; set; }
        public OrganizationSecurityAction Actions { get; set; }
        public DateTime DateCreated { get; set; }
        public DateTime? DateUpdated { get; set; }
    }
}