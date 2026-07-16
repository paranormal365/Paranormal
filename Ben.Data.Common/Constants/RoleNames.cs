namespace Ben.Data.Common.Constants;

/// <summary>
/// Strongly-typed constants for ASP.NET Core Identity role names.
/// </summary>
/// <remarks>
/// Use these constants everywhere a role name is required — in
/// <c>[Authorize(Roles = RoleNames.SuperAdmin)]</c> attributes, calls to
/// <c>UserManager.IsInRoleAsync</c>, and Serilog enrichment properties —
/// so that a future rename requires only a single change here.
/// <para>
/// Currently only one role exists in the application.
/// </para>
/// </remarks>
public static class RoleNames
{
    /// <summary>
    /// The name of the application super-administrator role.
    /// Users in this role can access all <c>/api/admin/*</c> endpoints and
    /// perform impersonation.  Created by
    /// <c>Ben.Data.WebApi.SeedData.SuperAdminSeeder</c> at startup.
    /// </summary>
    public const string SuperAdmin = "SuperAdmin";
}
