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
/// Two app-wide roles exist. Organization-level permissions are a separate system entirely
/// (<c>OrganizationMemberRole</c> plus the org security service) and are not represented here.
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

    /// <summary>
    /// An app-wide administrator below <see cref="SuperAdmin"/>.
    /// </summary>
    /// <remarks>
    /// <para><b>This role currently grants nothing except visibility of the app-administration
    /// help documents.</b> Every existing authorization check — 2 controller attributes, 54 inline
    /// server checks and 32 UI checks at the time of writing — still tests for
    /// <see cref="SuperAdmin"/> alone and was deliberately left untouched when this role was
    /// introduced. Widening them all at once would have been an unreviewed privilege expansion
    /// carried in on the back of a documentation feature.</para>
    ///
    /// <para>Granting Admin a specific capability is a per-site decision: change that site's check
    /// to accept either role, and think about what that endpoint exposes while doing it.</para>
    /// </remarks>
    public const string Admin = "Admin";

    /// <summary>Both app-wide administration roles, for checks that accept either.</summary>
    public static readonly string[] AppAdministrators = [SuperAdmin, Admin];
}
