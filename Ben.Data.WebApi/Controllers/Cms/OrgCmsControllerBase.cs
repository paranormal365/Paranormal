using AutoMapper;
using Ben.Data.Common.Constants;
using Ben.Data.Common.Enums;
using Ben.Data.Source.Context;
using Ben.Service.RepositoryService.GenericInterfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;

namespace Ben.Data.WebApi.Controllers.Cms;

/// <summary>
/// Shared base for CMS controllers that operate within an organization scope.
/// Provides <see cref="GetCurrentUserId"/> and <see cref="IsCmsAuthorizedAsync"/>.
/// </summary>
[ApiController]
[Authorize]
public abstract class OrgCmsControllerBase : ControllerBase
{
    protected readonly IDbContextFactory<BenDataContext> DbFactory;
    protected readonly IMapper Mapper;
    protected readonly IOrganizationSecurityService Security;

    protected OrgCmsControllerBase(
        IDbContextFactory<BenDataContext> dbFactory,
        IMapper mapper,
        IOrganizationSecurityService security)
    {
        DbFactory = dbFactory;
        Mapper    = mapper;
        Security  = security;
    }

    protected Guid? GetCurrentUserId()
    {
        var value = User.FindFirstValue(Services.EntraClaimsTransformation.AppUserIdClaimType)
                    ?? User.FindFirstValue(ClaimTypes.NameIdentifier);
        return Guid.TryParse(value, out var id) ? id : null;
    }

    /// <summary>
    /// Returns true when the current user is a site-level SuperAdmin OR passes the
    /// organization-level security check for the given table/action.
    /// </summary>
    protected async Task<bool> IsCmsAuthorizedAsync(
        Guid userId,
        Guid orgId,
        OrganizationSecurityTable table,
        OrganizationSecurityAction action,
        CancellationToken ct)
    {
        if (User.IsInRole(RoleNames.SuperAdmin)) return true;
        return await Security.HasAccessAsync(userId, orgId, table, action, ct);
    }
}
