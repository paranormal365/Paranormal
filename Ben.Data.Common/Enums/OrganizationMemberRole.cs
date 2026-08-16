namespace Ben.Data.Common.Enums;

/// <summary>
/// Defines the hierarchical roles a user can hold within an organization.
/// Stored as an <c>int</c> column on <c>OrganizationUserMembership</c>.
/// </summary>
/// <remarks>
/// Integer values are intentionally ordered from highest privilege (1) to
/// lowest (5), which allows simple range comparisons:
/// <code>membership.Role &lt;= OrganizationMemberRole.Administrator</code>
/// <para>
/// Both <see cref="Owner"/> and <see cref="Administrator"/> are treated as
/// "org admins" in access-control checks throughout
/// <c>Ben.Service.RepositoryService.Services.OrganizationSecurityService</c>.
/// </para>
/// <para>
/// It lives in <c>Ben.Data.Common</c> because the <c>OrganizationUserMembership</c> entity in the
/// data layer references it, and everything above the data layer can depend on Common without
/// creating a cycle.
/// </para>
/// </remarks>
public enum OrganizationMemberRole
{
    /// <summary>Full ownership of the organization; exactly one owner exists per organization and is set at registration time.</summary>
    Owner = 1,

    /// <summary>Administrative rights equivalent to the owner for day-to-day management tasks.</summary>
    Administrator = 2,

    /// <summary>Can manage organization content but does not have administrative access to membership or security settings.</summary>
    Manager = 3,

    /// <summary>Standard membership with read and limited interaction rights as defined by <c>OrganizationAccessGrant</c> rows.</summary>
    Member = 4,

    /// <summary>Read-only access to organization content; cannot create, update, or delete records.</summary>
    Viewer = 5
}
