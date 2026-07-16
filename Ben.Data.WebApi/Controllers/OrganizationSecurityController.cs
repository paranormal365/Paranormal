using Ben.Data.Common.Enums;
using Ben.Service.RepositoryService.GenericInterfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;

namespace Ben.Data.WebApi.Controllers;

[ApiController]
[Authorize]
[Route("api/organizations/{organizationId:guid}/security")]
public class OrganizationSecurityController : ControllerBase
{
    private readonly IOrganizationSecurityService _organizationSecurityService;

    public OrganizationSecurityController(IOrganizationSecurityService organizationSecurityService)
    {
        _organizationSecurityService = organizationSecurityService;
    }

    [HttpGet("my-access")]
    public async Task<ActionResult<bool>> CheckMyAccess(
        Guid organizationId,
        [FromQuery] OrganizationSecurityTable table,
        [FromQuery] OrganizationSecurityAction action,
        CancellationToken cancellationToken)
    {
        var appUserId = GetCurrentUserId();
        var hasAccess = await _organizationSecurityService.HasAccessAsync(appUserId, organizationId, table, action, cancellationToken);
        return Ok(hasAccess);
    }

    [HttpPost("check-access")]
    public async Task<ActionResult<bool>> CheckAccess(
        Guid organizationId,
        [FromBody] CheckOrganizationAccessRequest request,
        CancellationToken cancellationToken)
    {
        var actingUserId = GetCurrentUserId();

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
        var actingUserId = GetCurrentUserId();
        var members = await _organizationSecurityService.GetOrganizationUsersAsync(organizationId, actingUserId, cancellationToken);

        return Ok(members.Select(m => new OrganizationUserMembershipResponse
        {
            MembershipId = m.Id,
            OrganizationId = m.OrganizationId,
            AppUserId = m.AppUserId,
            Role = m.Role,
            IsActive = m.IsActive,
            DateCreated = m.DateCreated,
            DateUpdated = m.DateUpdated
        }));
    }

    [HttpPut("users/{targetUserId:guid}/membership")]
    public async Task<ActionResult<OrganizationUserMembershipResponse>> UpsertMembership(
        Guid organizationId,
        Guid targetUserId,
        [FromBody] UpsertOrganizationMembershipRequest request,
        CancellationToken cancellationToken)
    {
        var actingUserId = GetCurrentUserId();

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
        var actingUserId = GetCurrentUserId();

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

    private Guid GetCurrentUserId()
    {
        var value = User.FindFirstValue(ClaimTypes.NameIdentifier)
                    ?? User.FindFirstValue("sub");

        if (!Guid.TryParse(value, out var appUserId))
        {
            throw new UnauthorizedAccessException("Authenticated user id claim is missing or invalid.");
        }

        return appUserId;
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