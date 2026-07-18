using Ben.Service.Security.Enums;
using OrganizationMemberRole = Ben.Data.Common.Enums.OrganizationMemberRole;

namespace Ben.Service.Security.Services;

/// <summary>
/// Provides organization-level permission checks and membership management used
/// by the middleware/attribute layer (<c>OrganizationSecurityAuthorizeAttribute</c>).
/// </summary>
/// <remarks>
/// This interface is distinct from
/// <c>Ben.Service.RepositoryService.GenericInterfaces.IOrganizationSecurityService</c>,
/// which is used by controller code.  Both share the same concrete implementation
/// but are registered separately in DI.
/// </remarks>
public interface IOrganizationSecurityService
{
    /// <summary>
    /// Returns <c>true</c> if the user holds explicit grant access for
    /// <paramref name="action"/> on <paramref name="table"/> within the organization.
    /// </summary>
    /// <param name="userId">The user being checked.</param>
    /// <param name="organizationId">The target organization.</param>
    /// <param name="table">The domain table being accessed.</param>
    /// <param name="action">The bitflag operation(s) being attempted (see <see cref="OrganizationSecurityAction"/>).</param>
    /// <param name="cancellationToken">Propagates cancellation to the database query.</param>
    /// <remarks>
    /// Owners always return <c>true</c>.  Other roles require an explicit
    /// <c>OrganizationAccessGrant</c> row where <c>IsAllowed = true</c>.
    /// </remarks>
    Task<bool> HasPermissionAsync(
        Guid userId,
        Guid organizationId,
        OrganizationSecurityTable table,
        OrganizationSecurityAction action,
        CancellationToken cancellationToken = default);

    /// <summary>Returns <c>true</c> if an active membership row exists for the user in the organization.</summary>
    /// <param name="userId">The user to check.</param>
    /// <param name="organizationId">The organization to check membership in.</param>
    /// <param name="cancellationToken">Propagates cancellation to the database query.</param>
    Task<bool> IsMemberAsync(
        Guid userId,
        Guid organizationId,
        CancellationToken cancellationToken = default);

    /// <summary>Returns the IDs of all organizations the user is an active member of.</summary>
    /// <param name="userId">The user whose organizations are requested.</param>
    /// <param name="cancellationToken">Propagates cancellation to the database query.</param>
    Task<IReadOnlyList<Guid>> GetUserOrganizationsAsync(
        Guid userId,
        CancellationToken cancellationToken = default);

    /// <summary>Returns the user's <see cref="OrganizationMemberRole"/> within the organization.</summary>
    /// <param name="userId">The user to look up.</param>
    /// <param name="organizationId">The organization to check.</param>
    /// <param name="cancellationToken">Propagates cancellation to the database query.</param>
    /// <returns>The user's role, or <c>null</c> if they are not a member.</returns>
    Task<OrganizationMemberRole?> GetUserRoleAsync(
        Guid userId,
        Guid organizationId,
        CancellationToken cancellationToken = default);

    /// <summary>Returns <c>true</c> if the user holds the <see cref="OrganizationMemberRole.Owner"/> role in the organization.</summary>
    /// <param name="userId">The user to check.</param>
    /// <param name="organizationId">The organization to check.</param>
    /// <param name="cancellationToken">Propagates cancellation to the database query.</param>
    Task<bool> IsOwnerAsync(
        Guid userId,
        Guid organizationId,
        CancellationToken cancellationToken = default);

    /// <summary>Returns all active members of an organization with their assigned roles.</summary>
    /// <param name="organizationId">The organization to query.</param>
    /// <param name="cancellationToken">Propagates cancellation to the database query.</param>
    Task<IReadOnlyList<(Guid UserId, OrganizationMemberRole Role)>> GetOrganizationMembersAsync(
        Guid organizationId,
        CancellationToken cancellationToken = default);

    /// <summary>Grants a user permission to perform <paramref name="actions"/> on <paramref name="table"/> within the organization.</summary>
    /// <param name="organizationId">Target organization.</param>
    /// <param name="userId">User receiving the grant.</param>
    /// <param name="table">The domain table the grant applies to.</param>
    /// <param name="actions">The bitflag set of <see cref="OrganizationSecurityAction"/> values to permit.</param>
    /// <param name="grantedByUserId">The user issuing the grant (used for audit).</param>
    /// <param name="cancellationToken">Propagates cancellation to the database write.</param>
    Task GrantAccessAsync(
        Guid organizationId,
        Guid userId,
        OrganizationSecurityTable table,
        OrganizationSecurityAction actions,
        Guid grantedByUserId,
        CancellationToken cancellationToken = default);

    /// <summary>Revokes all access grants for the user on <paramref name="table"/> within the organization.</summary>
    /// <param name="organizationId">Target organization.</param>
    /// <param name="userId">User whose grant is being revoked.</param>
    /// <param name="table">The domain table whose grants are cleared.</param>
    /// <param name="cancellationToken">Propagates cancellation to the database write.</param>
    Task RevokeAccessAsync(
        Guid organizationId,
        Guid userId,
        OrganizationSecurityTable table,
        CancellationToken cancellationToken = default);

    /// <summary>Adds a user to the organization with the specified role, or reactivates their existing membership.</summary>
    /// <param name="organizationId">Target organization.</param>
    /// <param name="userId">User to add or reactivate.</param>
    /// <param name="role">The <see cref="OrganizationMemberRole"/> to assign.</param>
    /// <param name="cancellationToken">Propagates cancellation to the database write.</param>
    Task AddMemberAsync(
        Guid organizationId,
        Guid userId,
        OrganizationMemberRole role,
        CancellationToken cancellationToken = default);

    /// <summary>Deactivates a user's membership by setting <c>IsActive = false</c>.</summary>
    /// <param name="organizationId">Target organization.</param>
    /// <param name="userId">User to remove.</param>
    /// <param name="cancellationToken">Propagates cancellation to the database write.</param>
    /// <remarks>The membership row is retained for audit purposes; only <c>IsActive</c> is set to <c>false</c>.</remarks>
    Task RemoveMemberAsync(
        Guid organizationId,
        Guid userId,
        CancellationToken cancellationToken = default);
}
