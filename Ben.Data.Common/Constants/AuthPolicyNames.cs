namespace Ben.Data.Common.Constants;

/// <summary>
/// Strongly-typed constants for named ASP.NET Core authorization policies (as opposed to
/// <see cref="RoleNames"/>, which are Identity role names). Use these in
/// <c>[Authorize(Policy = AuthPolicyNames.X)]</c> attributes rather than a raw string, so the
/// attribute and the policy registration in <c>Program.cs</c> can't drift out of sync.
/// </summary>
public static class AuthPolicyNames
{
    /// <summary>
    /// Requires the caller to present a validated Microsoft Entra JWT (not a local Identity
    /// bearer token). Used by endpoints that must read Entra identity claims (OID, email)
    /// straight off the token rather than trusting client-supplied values in the request body —
    /// see <c>EntraAuthController</c>. When Entra isn't configured for this environment, the
    /// policy always denies rather than the request crashing on an unregistered scheme.
    /// </summary>
    public const string EntraOnly = "EntraOnly";

    /// <summary>
    /// Requires the caller to hold either app-wide administration role
    /// (<see cref="RoleNames.SuperAdmin"/> or <see cref="RoleNames.Admin"/>), resolved the same
    /// way the SuperAdmin policy resolves its role — by claim for a local Identity bearer token,
    /// or by database lookup for an Entra JWT.
    /// </summary>
    /// <remarks>
    /// Exists so an endpoint that accepts both roles can still go through a policy.
    /// <c>[Authorize(Roles = "SuperAdmin,Admin")]</c> cannot: a bare Roles attribute names no
    /// authentication scheme, so it re-authenticates with the default one only, and an Entra
    /// caller is not merely refused but comes back as unauthenticated — a 401 where a 403 was
    /// meant. See the note on the SuperAdmin policy in <c>Ben.Data.WebApi/Program.cs</c>.
    /// </remarks>
    public const string AppAdministrator = "AppAdministrator";

    /// <summary>
    /// The Microsoft Entra JWT bearer scheme, registered in <c>Program.cs</c> only when Entra is
    /// configured. Shared so an endpoint that must authenticate it explicitly — an anonymous one,
    /// which the default scheme alone cannot see — names the same string the registration does.
    /// </summary>
    public const string EntraScheme = "Entra";
}
