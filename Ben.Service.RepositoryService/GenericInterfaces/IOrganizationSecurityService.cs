using Ben.Data.Common.Enums;
using Ben.Data.Source.Entities;

namespace Ben.Service.RepositoryService.GenericInterfaces;

/// <summary>
/// Provides organization-level security and membership operations used by the
/// WebApi controller layer.
/// </summary>
/// <remarks>
/// This interface is distinct from
/// <c>Ben.Service.Security.Services.IOrganizationSecurityService</c>, which
/// is used by the middleware/attribute layer.  Both are registered in DI and
/// routed to the same concrete implementation in
/// <c>Ben.Service.RepositoryService.Services.OrganizationSecurityService</c>.
/// </remarks>
public interface IOrganizationSecurityService
{
    /// <summary>
    /// Searches for users visible to the acting user.
    /// </summary>
    /// <param name="actingUserId">The user performing the search.  SuperAdmins see all users; others see only users who share an active organization membership.</param>
    /// <param name="query">Optional free-text filter applied to <c>Email</c>, <c>UserName</c>, and <c>DisplayName</c>.</param>
    /// <param name="skip">Number of results to skip for pagination (minimum 0).</param>
    /// <param name="take">Maximum results to return (clamped to 1–100).</param>
    /// <param name="token">Propagates cancellation to the database query.</param>
    Task<IReadOnlyList<AppUser>> SearchUsersAsync(Guid actingUserId, string? query, int skip = 0, int take = 25, CancellationToken token = default);

    /// <summary>
    /// Returns <c>true</c> if the user is authorised to perform <paramref name="actionName"/>
    /// on <paramref name="tableName"/> within the organization.
    /// </summary>
    /// <param name="appUserId">The user whose access is being checked.</param>
    /// <param name="organizationId">The target organization.</param>
    /// <param name="tableName">The table (domain area) being accessed.</param>
    /// <param name="actionName">The CRUD operation being attempted.</param>
    /// <param name="token">Propagates cancellation to the database query.</param>
    /// <remarks>
    /// SuperAdmins always return <c>true</c>.  Organization Owners and Administrators
    /// return <c>true</c> for any table/action combination.  Other members require
    /// an explicit <see cref="OrganizationAccessGrant"/> row.
    /// </remarks>
    Task<bool> HasAccessAsync(Guid appUserId, Guid organizationId, OrganizationSecurityTable tableName, OrganizationSecurityAction actionName, CancellationToken token = default);

    /// <summary>
    /// Returns the list of organizations the user is an active member of.
    /// SuperAdmins receive all organizations.
    /// </summary>
    /// <param name="appUserId">The user whose organizations are requested.</param>
    /// <param name="token">Propagates cancellation to the database query.</param>
    Task<IReadOnlyList<Organization>> GetOrganizationsForUserAsync(Guid appUserId, CancellationToken token = default);

    /// <summary>
    /// Creates a new organization and adds <paramref name="appUserId"/> as its Owner.
    /// </summary>
    /// <param name="appUserId">The user who will own the new organization.</param>
    /// <param name="name">Display name of the organization.  Must not be blank.</param>
    /// <param name="urlName">URL-safe slug for the organization.  Must be unique across all organizations.</param>
    /// <param name="token">Propagates cancellation to the database write.</param>
    /// <returns>The newly created <see cref="Organization"/> entity.</returns>
    /// <exception cref="InvalidOperationException">
    /// Thrown when <paramref name="name"/> or <paramref name="urlName"/> is blank,
    /// the acting user does not exist, or <paramref name="urlName"/> is already taken.
    /// </exception>
    Task<Organization> RegisterOrganizationAsync(Guid appUserId, string name, string urlName, CancellationToken token = default);

    /// <summary>
    /// Returns all membership rows for the organization, ordered by role then creation date.
    /// </summary>
    /// <param name="organizationId">The organization to query.</param>
    /// <param name="actingUserId">Must be a SuperAdmin or an Owner/Administrator of the organization; otherwise an <see cref="UnauthorizedAccessException"/> is thrown.</param>
    /// <param name="token">Propagates cancellation to the database query.</param>
    Task<IReadOnlyList<OrganizationUserMembership>> GetOrganizationUsersAsync(Guid organizationId, Guid actingUserId, CancellationToken token = default);

    /// <summary>
    /// Creates or updates the membership row for a user in an organization.
    /// </summary>
    /// <param name="organizationId">Target organization.</param>
    /// <param name="targetUserId">User whose membership is being set.</param>
    /// <param name="role">The <see cref="OrganizationMemberRole"/> to assign.</param>
    /// <param name="isActive">Whether the membership is active.</param>
    /// <param name="actingUserId">Must be a SuperAdmin or an Owner/Administrator of the organization.</param>
    /// <param name="token">Propagates cancellation to the database write.</param>
    /// <returns>The created or updated <see cref="OrganizationUserMembership"/> entity.</returns>
    Task<OrganizationUserMembership> UpsertMembershipAsync(Guid organizationId, Guid targetUserId, OrganizationMemberRole role, bool isActive, Guid actingUserId, CancellationToken token = default);

    /// <summary>
    /// Creates or updates an explicit access grant for a specific table, setting the full actions bitmask.
    /// </summary>
    /// <param name="organizationId">Target organization.</param>
    /// <param name="targetUserId">The user receiving the grant.  Must be an active member of the organization.</param>
    /// <param name="tableName">The table the grant applies to.</param>
    /// <param name="actions">The <see cref="OrganizationSecurityAction"/> flags to store.  Pass <c>None</c> to clear all access.</param>
    /// <param name="actingUserId">Must be a SuperAdmin or an Owner/Administrator of the organization.</param>
    /// <param name="token">Propagates cancellation to the database write.</param>
    /// <returns>The created or updated <see cref="OrganizationAccessGrant"/> entity.</returns>
    /// <exception cref="InvalidOperationException">Thrown when <paramref name="targetUserId"/> is not an active member.</exception>
    Task<OrganizationAccessGrant> SetAccessGrantAsync(Guid organizationId, Guid targetUserId, OrganizationSecurityTable tableName, OrganizationSecurityAction actions, Guid actingUserId, CancellationToken token = default);
}
