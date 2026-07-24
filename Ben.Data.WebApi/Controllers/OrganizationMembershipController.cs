using Ben.Service.RepositoryService.GenericInterfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Ben.Data.WebApi.Controllers;

/// <summary>
/// Provides endpoints for discovering and managing the calling user's organization memberships.
/// </summary>
/// <remarks>
/// All endpoints require an authenticated user (<c>[Authorize]</c>).
/// Privileged operations such as managing <em>other</em> users' memberships are
/// handled by <see cref="OrganizationSecurityController"/>.
/// </remarks>
[ApiController]
[Authorize]
[Route("api/security/organizations")]
public class OrganizationMembershipController : BenControllerBase
{
    private readonly IOrganizationSecurityService _organizationSecurityService;

    /// <summary>Initialises the controller with its required service dependency.</summary>
    public OrganizationMembershipController(IOrganizationSecurityService organizationSecurityService)
    {
        _organizationSecurityService = organizationSecurityService;
    }

    /// <summary>Searches for users within the calling user's security scope.</summary>
    /// <param name="q">Optional free-text query filtered against <c>Email</c>, <c>UserName</c>, and <c>DisplayName</c>.</param>
    /// <param name="skip">Zero-based pagination offset.</param>
    /// <param name="take">Maximum results to return (server-side clamped to 100).</param>
    /// <param name="cancellationToken">Propagates cancellation from the HTTP request.</param>
    /// <remarks>SuperAdmins see all users; others see only users sharing an active organization membership.</remarks>
    [HttpGet("users/search")]
    public async Task<ActionResult<IEnumerable<UserSearchResultResponse>>> SearchUsers(
        [FromQuery] string? q,
        [FromQuery] int skip = 0,
        [FromQuery] int take = 25,
        CancellationToken cancellationToken = default)
    {
        var actingUserId = GetCurrentUserIdOrThrow();
        var users = await _organizationSecurityService.SearchUsersAsync(actingUserId, q, skip, take, cancellationToken);

        return Ok(users.Select(u => new UserSearchResultResponse
        {
            AppUserId = u.Id,
            DisplayName = u.DisplayName,
            UserName = u.UserName,
            Email = u.Email
        }));
    }

    /// <summary>Returns all organizations the authenticated user is an active member of.</summary>
    /// <param name="cancellationToken">Propagates cancellation from the HTTP request.</param>
    /// <remarks>SuperAdmins receive every organization in the system.</remarks>
    [HttpGet("mine")]
    public async Task<ActionResult<IEnumerable<OrganizationSummaryResponse>>> GetMyOrganizations(CancellationToken cancellationToken)
    {
        var appUserId = GetCurrentUserIdOrThrow();
        var organizations = await _organizationSecurityService.GetOrganizationsForUserAsync(appUserId, cancellationToken);

        return Ok(organizations.Select(o => new OrganizationSummaryResponse
        {
            OrganizationId = o.Id,
            Name = o.Name,
            UrlName = o.UrlName,
            DateCreated = o.DateCreated,
            CreatedByAppUserId = o.CreatedByAppUserId
        }));
    }

    /// <summary>Creates a new organization with the authenticated user as its <see cref="Ben.Data.Common.Enums.OrganizationMemberRole.Owner"/>.</summary>
    /// <param name="request">Name and URL slug for the new organization.</param>
    /// <param name="cancellationToken">Propagates cancellation from the HTTP request.</param>
    /// <returns>A <c>201 Created</c> response with the new organization summary, or <c>400</c> if the name/urlName is blank or the urlName is already taken.</returns>
    [HttpPost("register")]
    public async Task<ActionResult<OrganizationSummaryResponse>> RegisterOrganization(
        [FromBody] RegisterOrganizationRequest request,
        CancellationToken cancellationToken)
    {
        var appUserId = GetCurrentUserIdOrThrow();
        var organization = await _organizationSecurityService.RegisterOrganizationAsync(appUserId, request.Name, request.UrlName, cancellationToken);

        return CreatedAtAction(
            nameof(GetMyOrganizations),
            new { },
            new OrganizationSummaryResponse
            {
                OrganizationId = organization.Id,
                Name = organization.Name,
                UrlName = organization.UrlName,
                DateCreated = organization.DateCreated,
                CreatedByAppUserId = organization.CreatedByAppUserId
            });
    }

    /// <summary>Request body for <see cref="RegisterOrganization"/>.</summary>
    public sealed class RegisterOrganizationRequest
    {
        /// <summary>Human-readable display name of the new organization.</summary>
        public required string Name { get; set; }
        /// <summary>URL-safe slug (e.g. <c>my-org</c>); must be unique across all organizations.</summary>
        public required string UrlName { get; set; }
    }

    /// <summary>Lightweight organization projection returned by membership endpoints.</summary>
    public sealed class OrganizationSummaryResponse
    {
        public Guid OrganizationId { get; set; }
        public required string Name { get; set; }
        public required string UrlName { get; set; }
        public DateTime DateCreated { get; set; }
        public Guid CreatedByAppUserId { get; set; }
    }

    /// <summary>Lightweight user projection returned by the user-search endpoint.</summary>
    public sealed class UserSearchResultResponse
    {
        public Guid AppUserId { get; set; }
        public string? DisplayName { get; set; }
        public string? UserName { get; set; }
        public string? Email { get; set; }
    }
}