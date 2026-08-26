using Ben.Data.Common.Enums;
using Ben.Data.Source.Entities;

namespace Ben.Service.RepositoryService.GenericInterfaces;

/// <summary>
/// Provides organization-level security and membership operations used by the
/// WebApi controller layer.
/// </summary>
/// <remarks>
/// Implemented by <c>Ben.Service.RepositoryService.Services.OrganizationSecurityService</c>, which
/// controllers inject directly. A second, identically-named interface used to exist in a
/// <c>Ben.Service.Security</c> project for a middleware/attribute layer that was never applied to
/// any route; both it and that project have been removed, so this is the only one.
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

    // ── The three questions, said plainly (Ben, 2026-08-26) ──────────────────
    //
    // Twenty-six controllers had each written their own wrapper around HasAccessAsync and named
    // it something friendlier — IsOrgMember, IsMemberAsync, MayManageAsync, CanManageAsync. The
    // names disagreed with each other AND with what the code did: a helper called `IsOrgMember`
    // that returns "holds a Case.Read grant" is a lie in a method name, and it cost this branch
    // one wrong measurement of its own audit. Ben's rule, which these follow: a USER is a
    // verified account, a MEMBER belongs to an organization, and a question about a GRANT should
    // say neither.
    //
    // Three names, three meanings, no wrappers. A call site reads as the question it is asking.

    /// <summary>May this person take this action in this area?</summary>
    /// <remarks>
    /// <para>The grant question. Takes an <see cref="OrganizationPermissionArea"/> rather than a
    /// table, because an area is what a role editor grants and what a UI affordance is about;
    /// the table is an implementation detail underneath.</para>
    ///
    /// <para>Answers are cached for the life of the request: the same verdict is asked for many
    /// times while rendering one page, and each uncached call opens its own DbContext for up to
    /// four queries — which is why <c>OrganizationController</c> had to hand-batch this to avoid
    /// "up to 8N queries for N orgs". Nothing a person may do changes mid-request.</para>
    /// </remarks>
    Task<bool> MayAsync(Guid appUserId, Guid organizationId, OrganizationPermissionArea area, OrganizationSecurityAction action, CancellationToken token = default);

    /// <summary>Is this person the owner or an administrator of this organization?</summary>
    /// <remarks>
    /// The TIER question, not the grant question — deliberately separate. Some things are
    /// owner-or-admin forever regardless of any role: member levels, area of operation,
    /// transferring a case away. Asking it by name stops those being quietly converted into
    /// grants by somebody tidying up.
    /// </remarks>
    Task<bool> IsOwnerOrAdminAsync(Guid appUserId, Guid organizationId, CancellationToken token = default);

    /// <summary>Does this person belong to this organization at all?</summary>
    /// <remarks>
    /// The MEMBERSHIP question. For the handful of things no permission area covers — the group's
    /// message board is the example — where belonging IS the whole rule. Not a substitute for
    /// <see cref="MayAsync"/>: reaching for this because the area model is inconvenient is how a
    /// grant stops meaning anything.
    /// </remarks>
    Task<bool> BelongsToAsync(Guid appUserId, Guid organizationId, CancellationToken token = default);

    /// <summary>
    /// Returns the list of organizations the user is an active member of.
    /// SuperAdmins receive all organizations.
    /// </summary>
    /// <param name="appUserId">The user whose organizations are requested.</param>
    /// <param name="token">Propagates cancellation to the database query.</param>
    Task<IReadOnlyList<Organization>> GetMembershipOrganizationsAsync(Guid appUserId, CancellationToken token = default);
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
    /// <param name="kind">What the new group is (2026-08-24). Decides the defaults it starts
    /// with — see <c>OrganizationKindDefaults</c> — and nothing else.</param>
    Task<Organization> RegisterOrganizationAsync(Guid appUserId, string name, string urlName,
        Ben.Data.Common.Enums.OrganizationKind kind = Ben.Data.Common.Enums.OrganizationKind.InvestigationGroup,
        CancellationToken token = default);

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

    /// <summary>
    /// Deletes one or all access grants for a user in an organization.
    /// </summary>
    /// <param name="organizationId">Target organization.</param>
    /// <param name="targetUserId">User whose grant(s) are being removed.</param>
    /// <param name="tableName">When provided, only the grant for that table is deleted.  When <c>null</c>, all grants for the user in the organization are deleted.</param>
    /// <param name="actingUserId">Must be a SuperAdmin or an Owner/Administrator of the organization.</param>
    /// <param name="token">Propagates cancellation to the database write.</param>
    /// <returns>The number of grant rows deleted.</returns>
    Task<int> DeleteGrantAsync(Guid organizationId, Guid targetUserId, OrganizationSecurityTable? tableName, Guid actingUserId, CancellationToken token = default);
}
