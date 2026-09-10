namespace Ben.Web.Services.WebApi;

/// <summary>
/// What every client of Ben.Data.WebApi knows about the person signed in: their tokens, when the
/// access token stops being usable, and who they are.
/// </summary>
/// <remarks>
/// <para>Item 225 split this out of <c>IWebApiTokenStore</c>, which mixes it with three
/// Blazor-Server concerns a desktop or mobile client has no equivalent for: impersonation, the
/// Entra OIDC bridge, and the <c>AuthReady</c> gate that exists only because a Blazor Server page
/// renders before its circuit is interactive. The web interface still carries those and extends
/// this one, so nothing on the website changed.</para>
///
/// <para><b>The roles are not read from the token.</b> Identity issues opaque, data-protected
/// bearer tokens rather than JWTs, so there are no claims to parse. Every client must ask
/// <c>GET api/me</c> after acquiring a token, and re-ask after any token exchange.</para>
/// </remarks>
public interface ITokenStore
{
    string? AccessToken { get; set; }
    string? RefreshToken { get; set; }

    /// <summary>
    /// When the access token stops being accepted, or null when nothing is signed in.
    /// </summary>
    /// <remarks>
    /// Set this to the issue time plus <c>expiresIn</c> LESS a small slack, so a token that is
    /// about to expire is refreshed rather than sent and refused. Every client here uses 30
    /// seconds.
    /// </remarks>
    DateTimeOffset? AccessTokenExpiresAtUtc { get; set; }

    string? UserEmail { get; set; }
    string? UserDisplayName { get; set; }
    Guid? UserId { get; set; }
    bool IsSuperAdmin { get; set; }

    /// <summary>
    /// App-wide Admin role. Grants nothing on its own today beyond help-document visibility —
    /// see RoleNames.Admin.
    /// </summary>
    bool IsAdmin { get; set; }

    /// <summary>Item 186 F5: may review reported posts and media awaiting screening.</summary>
    bool IsModerator { get; set; }

    bool IsAuthenticated => !string.IsNullOrWhiteSpace(AccessToken);

    /// <summary>Fires after any auth-relevant state change (login, logout, impersonate, refresh).</summary>
    event Action? StateChanged;

    /// <summary>Invoke after all state fields have been set to notify subscribers.</summary>
    void NotifyStateChanged();
}
