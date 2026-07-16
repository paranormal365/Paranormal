namespace Ben.Service.Security.Extensions;

/// <summary>
/// Extension methods for security models.
/// </summary>
public static class SecurityExtensions
{
    /// <summary>
    /// Checks if the grant includes the specified action.
    /// </summary>
    public static bool HasPermission(
        this OrganizationAccessGrant grant,
        OrganizationSecurityAction action)
    {
        return (grant.Actions & action) == action;
    }
}
