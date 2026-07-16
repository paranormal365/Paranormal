using Ben.Service.Security.Enums;
using Ben.Service.Security.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.DependencyInjection;
using System.Security.Claims;

namespace Ben.Service.Security.Attributes;

/// <summary>
/// Custom authorization attribute that checks organization-level security permissions.
/// Usage: [OrganizationSecurityAuthorize("organizationId", OrganizationSecurityTable.User, OrganizationSecurityAction.Read)]
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
public sealed class OrganizationSecurityAuthorizeAttribute : Attribute, IAsyncAuthorizationFilter
{
    private readonly string _organizationIdParameter;
    private readonly OrganizationSecurityTable _table;
    private readonly OrganizationSecurityAction _action;

    /// <summary>
    /// Creates a new organization security authorization attribute.
    /// </summary>
    /// <param name="organizationIdParameter">The route parameter or claim name containing the organization ID</param>
    /// <param name="table">The table being accessed</param>
    /// <param name="action">The action being performed (Create, Read, Update, Delete)</param>
    public OrganizationSecurityAuthorizeAttribute(
        string organizationIdParameter,
        OrganizationSecurityTable table,
        OrganizationSecurityAction action)
    {
        _organizationIdParameter = organizationIdParameter;
        _table = table;
        _action = action;
    }

    public async Task OnAuthorizationAsync(AuthorizationFilterContext context)
    {
        // Get the current user ID from claims
        var userIdClaim = context.HttpContext.User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(userIdClaim) || !Guid.TryParse(userIdClaim, out var userId))
        {
            context.Result = new UnauthorizedResult();
            return;
        }

        // Get the organization ID from route data or query string
        var organizationId = GetOrganizationId(context);
        if (organizationId == Guid.Empty)
        {
            context.Result = new BadRequestObjectResult(
                new { error = $"Organization ID not found in parameter '{_organizationIdParameter}'" });
            return;
        }

        // Get the security service
        var securityService = context.HttpContext.RequestServices.GetRequiredService<IOrganizationSecurityService>();

        // Check permission
        var hasPermission = await securityService.HasPermissionAsync(
            userId,
            organizationId,
            _table,
            _action,
            context.HttpContext.RequestAborted);

        if (!hasPermission)
        {
            context.Result = new ForbidResult();
        }
    }

    private Guid GetOrganizationId(AuthorizationFilterContext context)
    {
        // Try to get from route data
        if (context.RouteData.Values.TryGetValue(_organizationIdParameter, out var value))
        {
            if (Guid.TryParse(value?.ToString(), out var organizationId))
            {
                return organizationId;
            }
        }

        // Try to get from query string
        if (context.HttpContext.Request.Query.TryGetValue(_organizationIdParameter, out var queryValue))
        {
            if (Guid.TryParse(queryValue.ToString(), out var organizationId))
            {
                return organizationId;
            }
        }

        return Guid.Empty;
    }
}
