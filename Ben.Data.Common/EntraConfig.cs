namespace Ben.Data.Common;

/// <summary>
/// The one rule for whether Microsoft Entra sign-in is actually configured, and whether the tenant
/// it points at is a multi-tenant authority.
/// </summary>
/// <remarks>
/// <para>This exists because the website and the API each grew their own answer and the two stopped
/// agreeing. The API asked whether <c>ClientId</c> parsed as a GUID. The website asked whether
/// <c>ClientId</c> differed from one particular literal it had been told to treat as unset. Those
/// are different questions, and a value can pass one while failing the other — which is exactly
/// what happened: the real registration id was written into the website's check as a sentinel, so
/// the website hid the Microsoft button while the API happily stood a JWT bearer handler up. One
/// rule, in one place, is the fix; which rule matters less than that both hosts use it.</para>
///
/// <para>The rule is the shape test. A placeholder must therefore never be GUID-shaped — write
/// <c>YOUR_CLIENT_ID</c> or leave the key out, and it is rejected here for free. A GUID-shaped
/// placeholder cannot be told apart from a real registration by any amount of inspection, and
/// maintaining a list of the ones we happen to know about only moves the failure: the list is
/// silently wrong the first time somebody invents a placeholder it has not heard of, and
/// catastrophically wrong the first time a real id lands on it.</para>
/// </remarks>
public static class EntraConfig
{
    /// <summary>
    /// The authorities that are not a single tenant. Any other value names one directory.
    /// </summary>
    private static readonly HashSet<string> MultiTenantAuthorities =
        new(StringComparer.OrdinalIgnoreCase) { "common", "organizations", "consumers" };

    /// <summary>
    /// True when <paramref name="clientId"/> names an app registration, rather than being absent,
    /// blank, malformed, or the empty GUID.
    /// </summary>
    /// <remarks>
    /// Both hosts gate their entire Entra setup on this. False means no OpenIdConnect scheme on the
    /// website and no JWT bearer scheme on the API — the feature disappears rather than half-works,
    /// which is the intended state until a registration exists.
    /// </remarks>
    public static bool IsConfigured(string? clientId) =>
        Guid.TryParse(clientId, out var id) && id != Guid.Empty;

    /// <summary>
    /// True for <c>common</c>, <c>organizations</c> and <c>consumers</c>, the authorities where a
    /// token legitimately carries any tenant's issuer and there is nothing single to validate
    /// against. Anything else — a GUID, or a domain like <c>contoso.onmicrosoft.com</c> — names one
    /// tenant, and issuer validation must then be on: without it a token minted in any Microsoft
    /// directory on earth satisfies every remaining check.
    /// </summary>
    /// <remarks>
    /// A null or blank tenant is treated as multi-tenant, matching the <c>common</c> fallback both
    /// hosts apply when the setting is absent. It is the safe reading: it never turns validation on
    /// against an authority that cannot satisfy it, which would lock everyone out rather than let
    /// anyone in.
    /// </remarks>
    public static bool IsMultiTenant(string? tenantId) =>
        string.IsNullOrWhiteSpace(tenantId) || MultiTenantAuthorities.Contains(tenantId);

    /// <summary>The tenant to build an authority URL from, defaulting to <c>common</c>.</summary>
    public static string TenantOrCommon(string? tenantId) =>
        string.IsNullOrWhiteSpace(tenantId) ? "common" : tenantId;
}
