using System.Text.Json;
using System.Text.Json.Serialization;
using System.Web;

namespace Ben.Data.WebApi.Client.External;

/// <summary>
/// Builds the URL a browser is sent to for Sign in with Apple, and reads the form Apple posts back.
/// </summary>
/// <remarks>
/// <para><b>No client secret, deliberately.</b> Asking for <c>id_token</c> in the response type
/// makes Apple return the signed identity token in the form post itself, and that token is the only
/// thing our API needs. The alternative — redeeming the authorization code at Apple's token
/// endpoint — requires a client secret that is itself a JWT signed with a downloaded key and
/// expires within six months, which is an operational burden bought for nothing here.</para>
///
/// <para>Both sides are pure, so the handshake is testable without Apple, https, or a Services ID.
/// That matters more here than usual: Apple refuses a localhost redirect, so the round trip itself
/// cannot be run on a development machine at all.</para>
/// </remarks>
public static class AppleWebAuthorizeRequest
{
    /// <summary>The URL to send the browser to.</summary>
    /// <param name="state">
    /// An unguessable value Apple echoes back. Checking it is what stops a form post this site did
    /// not ask for being accepted as though it had.
    /// </param>
    /// <param name="nonce">
    /// Echoed into the identity token's own claims, so a token minted for some other request cannot
    /// be replayed into this one.
    /// </param>
    public static Uri Build(AppleWebOptions options, string state, string nonce)
    {
        var query = HttpUtility.ParseQueryString(string.Empty);
        query["client_id"] = options.ServicesId;
        query["redirect_uri"] = options.RedirectUri;

        // code AND id_token: the id_token is what makes the client secret unnecessary.
        query["response_type"] = "code id_token";

        // Apple REQUIRES form_post once any scope is requested, and requires it for id_token too.
        // Anything else is refused before the person sees a sign-in box.
        query["response_mode"] = "form_post";

        // The name arrives on a FIRST authorization only, and only if asked for here. Not asking
        // means every account created this way is named whatever we invented.
        query["scope"] = "name email";

        query["state"] = state;
        query["nonce"] = nonce;

        return new Uri($"{AppleWebOptions.AuthorizeEndpoint}?{query}");
    }

    /// <summary>An unguessable value for <c>state</c> or <c>nonce</c>.</summary>
    public static string NewSecret() =>
        Base64Url(System.Security.Cryptography.RandomNumberGenerator.GetBytes(24));

    /// <summary>
    /// Base64 with the URL-safe alphabet and no padding: '+' and '/' change meaning in a query
    /// string, and '=' has no business in one.
    /// </summary>
    private static string Base64Url(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    /// <summary>What Apple posted back.</summary>
    /// <param name="DisplayName">
    /// Assembled from Apple's <c>user</c> field, which arrives on a FIRST authorization only.
    /// </param>
    public readonly record struct Callback(
        string? IdentityToken, string? DisplayName, string? Error, string? ErrorDescription, string? Code = null)
    {
        public bool Succeeded => !string.IsNullOrWhiteSpace(IdentityToken);

        /// <summary>They closed Apple's page or refused. A decision, not a failure.</summary>
        public bool WasCancelled =>
            string.Equals(Error, "user_cancelled_authorize", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Reads Apple's form post, refusing anything whose state does not match the request we started.
    /// </summary>
    /// <param name="form">The posted fields.</param>
    /// <param name="expectedState">The value stashed when the browser was sent to Apple.</param>
    /// <remarks>
    /// The state check is not a formality. This endpoint is a public POST, so without it anything
    /// that can post to it is treated as the answer to a sign-in nobody began.
    /// </remarks>
    public static Callback ReadCallback(
        IReadOnlyDictionary<string, string?> form, string? expectedState)
    {
        form.TryGetValue("state", out var state);

        if (string.IsNullOrWhiteSpace(expectedState) || !FixedTimeEquals(state, expectedState))
            return new Callback(null, null, "state_mismatch",
                "That sign-in didn't match a request from this browser. Start again.");

        form.TryGetValue("error", out var error);
        if (!string.IsNullOrWhiteSpace(error))
            return new Callback(null, null, error, null);

        form.TryGetValue("id_token", out var idToken);
        form.TryGetValue("user", out var user);

        // The authorization code rides along to the API, which exchanges it for the token that
        // lets the person's Apple tokens be revoked when they delete their account (item 229).
        form.TryGetValue("code", out var code);
        return new Callback(idToken, ReadDisplayName(user), null, null, string.IsNullOrWhiteSpace(code) ? null : code);
    }

    /// <summary>
    /// Pulls a display name out of Apple's <c>user</c> field.
    /// </summary>
    /// <remarks>
    /// <para>A JSON document, present on the FIRST authorization only, shaped
    /// <c>{"name":{"firstName":…,"lastName":…},"email":…}</c>. Apple never sends it again — not on a
    /// later sign-in, not after the account is deleted and remade — so a name here is the only one
    /// there will ever be.</para>
    ///
    /// <para>Both parts are joined rather than one taken, because somebody with only a family name
    /// recorded would otherwise come out nameless. Unreadable JSON answers null rather than
    /// throwing: a missing name must not cost somebody their sign-in.</para>
    /// </remarks>
    internal static string? ReadDisplayName(string? userJson)
    {
        if (string.IsNullOrWhiteSpace(userJson)) return null;

        try
        {
            var user = JsonSerializer.Deserialize<AppleUserField>(userJson);
            if (user?.Name is null) return null;

            var joined = string.Join(" ", new[] { user.Name.FirstName, user.Name.LastName }
                .Where(part => !string.IsNullOrWhiteSpace(part))).Trim();

            return string.IsNullOrWhiteSpace(joined) ? null : joined;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static bool FixedTimeEquals(string? a, string? b)
    {
        if (a is null || b is null) return false;

        return System.Security.Cryptography.CryptographicOperations.FixedTimeEquals(
            System.Text.Encoding.UTF8.GetBytes(a), System.Text.Encoding.UTF8.GetBytes(b));
    }

    private sealed record AppleUserField([property: JsonPropertyName("name")] AppleName? Name);

    private sealed record AppleName(
        [property: JsonPropertyName("firstName")] string? FirstName,
        [property: JsonPropertyName("lastName")] string? LastName);
}
