using Ben.Service.RepositoryService.GenericInterfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;

namespace Ben.Data.WebApi.Controllers;

/// <summary>
/// Provides endpoints for discovering and managing the calling user's organisation memberships.
/// </summary>
/// <remarks>
/// All endpoints require an authenticated user (<c>[Authorize]</c>).
/// Privileged operations such as managing <em>other</em> users' memberships are
/// handled by <see cref="OrganizationSecurityController"/>.
/// </remarks>
[ApiController]
[Authorize]
[Route("api/security/organizations")]
public class OrganizationMembershipController : ControllerBase
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
    /// <remarks>SuperAdmins see all users; others see only users sharing an active organisation membership.</remarks>
    [HttpGet("users/search")]
    public async Task<ActionResult<IEnumerable<UserSearchResultResponse>>> SearchUsers(
        [FromQuery] string? q,
        [FromQuery] int skip = 0,
        [FromQuery] int take = 25,
        CancellationToken cancellationToken = default)
    {
        var actingUserId = GetCurrentUserId();
        var users = await _organizationSecurityService.SearchUsersAsync(actingUserId, q, skip, take, cancellationToken);

        return Ok(users.Select(u => new UserSearchResultResponse
        {
            AppUserId = u.Id,
            DisplayName = u.DisplayName,
            UserName = u.UserName,
            Email = u.Email
        }));
    }

    /// <summary>Returns all organisations the authenticated user is an active member of.</summary>
    /// <param name="cancellationToken">Propagates cancellation from the HTTP request.</param>
    /// <remarks>SuperAdmins receive every organisation in the system.</remarks>
    [HttpGet("mine")]
    public async Task<ActionResult<IEnumerable<OrganizationSummaryResponse>>> GetMyOrganizations(CancellationToken cancellationToken)
    {
        var appUserId = GetCurrentUserId();
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

    /// <summary>Creates a new organisation with the authenticated user as its <see cref="Ben.Data.Common.Enums.OrganizationMemberRole.Owner"/>.</summary>
    /// <param name="request">Name and URL slug for the new organisation.</param>
    /// <param name="cancellationToken">Propagates cancellation from the HTTP request.</param>
    /// <returns>A <c>201 Created</c> response with the new organisation summary, or <c>400</c> if the name/urlName is blank or the urlName is already taken.</returns>
    [HttpPost("register")]
    public async Task<ActionResult<OrganizationSummaryResponse>> RegisterOrganization(
        [FromBody] RegisterOrganizationRequest request,
        CancellationToken cancellationToken)
    {
        var appUserId = GetCurrentUserId();
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

    /// <summary>Extracts the authenticated user's <see cref="Guid"/> from the JWT/identity claims.</summary>
    /// <exception cref="UnauthorizedAccessException">Thrown when the user ID claim is absent or not a valid <see cref="Guid"/>.</exception>
    private Guid GetCurrentUserId()
    {
        var value = User.FindFirstValue(Ben.Data.WebApi.Services.EntraClaimsTransformation.AppUserIdClaimType)
                    ?? User.FindFirstValue(ClaimTypes.NameIdentifier);

        if (!Guid.TryParse(value, out var appUserId))
        {
            throw new UnauthorizedAccessException("Authenticated user id claim is missing or invalid.");
        }

        return appUserId;
    }

    /// <summary>Request body for <see cref="RegisterOrganization"/>.</summary>
    public sealed class RegisterOrganizationRequest
    {
        /// <summary>Human-readable display name of the new organisation.</summary>
        public required string Name { get; set; }
        /// <summary>URL-safe slug (e.g. <c>my-org</c>); must be unique across all organisations.</summary>
        public required string UrlName { get; set; }
    }

    /// <summary>Lightweight organisation projection returned by membership endpoints.</summary>
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