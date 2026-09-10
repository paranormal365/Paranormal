using Ben.Web.Services.WebApi;

namespace Ben.Data.WebApi.Client.Auth;

/// <summary>
/// The pair of tokens one signed-in session consists of, and the moment the access token stops
/// being usable.
/// </summary>
/// <param name="ExpiresAtUtc">
/// Already includes the safety slack — see <see cref="From"/>. Compare it against now; do not
/// subtract anything else from it.
/// </param>
/// <param name="RefreshToken">
/// Null when the server answered without one. Identity always sends one today, but a session that
/// cannot be renewed is a real state and pretending otherwise would make it fail silently at the
/// first expiry with nothing to explain it.
/// </param>
public sealed record StoredTokens(string AccessToken, string? RefreshToken, DateTimeOffset ExpiresAtUtc)
{
    /// <summary>
    /// How long before the server's own expiry this client stops trusting an access token.
    /// </summary>
    /// <remarks>
    /// Without it, a token that expires while a request is in flight is sent and refused, and the
    /// 401 that comes back is indistinguishable from a session that genuinely ended. Thirty
    /// seconds is what the website and the iOS app both use; changing it here alone would make the
    /// three disagree about when a session is over.
    /// </remarks>
    public static readonly TimeSpan ExpirySlack = TimeSpan.FromSeconds(30);

    /// <summary>Builds a session from what <c>POST /login</c> or <c>POST /refresh</c> answered.</summary>
    /// <param name="now">
    /// Passed in rather than read, so a test can prove the expiry arithmetic without waiting for
    /// real time to pass.
    /// </param>
    public static StoredTokens From(WebApiTokenResponse response, DateTimeOffset now) =>
        new(response.AccessToken,
            response.RefreshToken,
            now.AddSeconds(response.ExpiresIn) - ExpirySlack);

    /// <summary>Whether the access token should be refreshed before it is used again.</summary>
    public bool IsExpiredAt(DateTimeOffset now) => ExpiresAtUtc <= now;

    /// <summary>Whether this session can be renewed at all.</summary>
    public bool CanRefresh => !string.IsNullOrWhiteSpace(RefreshToken);
}
