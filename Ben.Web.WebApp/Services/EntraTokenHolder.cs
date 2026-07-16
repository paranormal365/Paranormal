namespace Ben.Web.WebApp.Services;

/// <summary>
/// Scoped service populated by middleware during the initial HTTP request.
/// The token is then serialized via <see cref="SerializedEntraToken"/> into
/// the SSR-rendered HTML so the Interactive Server circuit can restore it.
/// </summary>
public sealed class EntraTokenHolder
{
    public string? AccessToken { get; set; }
    public string? Email { get; set; }

    /// <summary>The Entra Object ID (oid claim) — used as the external login ProviderKey.</summary>
    public string? EntraOid { get; set; }

    public bool IsEntraAuthenticated { get; set; }
}

/// <summary>
/// Plain serializable record used by <c>PersistentComponentState</c> to bridge
/// the HTTP-scope <see cref="EntraTokenHolder"/> into the Blazor circuit scope.
/// </summary>
public sealed record SerializedEntraToken(
    string? AccessToken,
    string? Email,
    string? EntraOid,
    bool IsEntraAuthenticated);
