using Ben.Service.Security.Enums;
using OrganizationMemberRole = Ben.Data.Common.Enums.OrganizationMemberRole;

namespace Ben.Service.Security.Models;

/// <summary>
/// Represents the permission a user has for a specific table and action within an organization.
/// </summary>
public sealed class OrganizationUserPermission
{
    public Guid OrganizationId { get; set; }
    public Guid UserId { get; set; }
    public OrganizationSecurityTable Table { get; set; }
    public OrganizationSecurityAction Actions { get; set; }

    public bool HasPermission(OrganizationSecurityAction action)
    {
        return (Actions & action) == action;
    }
}

/// <summary>
/// Represents a grant of permissions to a user for an organization and table.
/// </summary>
public sealed class OrganizationAccessGrant
{
    public Guid OrganizationId { get; set; }
    public Guid UserId { get; set; }
    public OrganizationSecurityTable Table { get; set; }
    public OrganizationSecurityAction Actions { get; set; }
    public DateTime GrantedAt { get; set; }
    public Guid GrantedByUserId { get; set; }
}

/// <summary>
/// Represents a user's membership in an organization.
/// </summary>
public sealed class OrganizationUserMembership
{
    public Guid OrganizationId { get; set; }
    public Guid UserId { get; set; }
    public OrganizationMemberRole Role { get; set; }
    public DateTime JoinedAt { get; set; }
    public DateTime? RemovalDate { get; set; }
}
