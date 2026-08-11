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
}
