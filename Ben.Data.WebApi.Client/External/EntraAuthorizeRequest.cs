using System.Web;

namespace Ben.Data.WebApi.Client.External;

/// <summary>
/// Builds the URL the browser opens, and reads what comes back from it.
/// </summary>
/// <remarks>
/// Pure on both sides, so the whole handshake can be tested without a browser, a tenant or a
/// network. The interactive step around it is a single call that hands a URL to the operating
/// system and waits for a redirect.
/// </remarks>
public static class EntraAuthorizeRequest
{
    /// <summary>The authorize URL for one sign-in attempt.</summary>
    /// <param name="state">
    /// An unguessable value echoed back by Entra. Checking it is what stops a redirect this app
    /// did not start from being accepted as though it had — cross-site request forgery against the
    /// callback.
    /// </param>
    public static Uri Build(EntraOptions options, EntraPkce pkce, string state)
    {
        var query = HttpUtility.ParseQueryString(string.Empty);
        query["client_id"] = options.ClientId;
        query["response_type"] = "code";
        query["redirect_uri"] = options.RedirectUri;
        query["response_mode"] = "query";

        // offline_access is what makes a refresh token come back. Without it the session dies at
        // the first expiry and the person is sent through the browser again roughly hourly.
        query["scope"] = $"{options.Scope} offline_access openid profile email";

        query["state"] = state;
        query["code_challenge"] = pkce.CodeChallenge;
        query["code_challenge_method"] = EntraPkce.Method;

        return new Uri($"{options.AuthorizeEndpoint}?{query}");
    }

    /// <summary>A random, unguessable state value.</summary>
    public static string NewState() =>
        EntraPkce.Base64Url(System.Security.Cryptography.RandomNumberGenerator.GetBytes(16));

    /// <summary>What came back on the redirect: a code, or a refusal.</summary>
    /// <param name="Error">Entra's own error code, e.g. <c>access_denied</c> when somebody cancels.</param>
    public readonly record struct Callback(string? Code, string? State, string? Error, string? ErrorDescription)
    {
        public bool Succeeded => !string.IsNullOrWhiteSpace(Code);

        /// <summary>Whether the person closed the window rather than anything going wrong.</summary>
        /// <remarks>
        /// Worth separating: cancelling is a decision, not a failure, and showing somebody an error
        /// because they changed their mind is noise.
        /// </remarks>
        public bool WasCancelled =>
            string.Equals(Error, "access_denied", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Reads the redirect, refusing anything whose state does not match the attempt we started.
    /// </summary>
    /// <remarks>
    /// The state check is not optional and is not a formality. Skipping it means any redirect
    /// delivered to this app's URL scheme — by another app that registered the same one, or by a
    /// link somebody was sent — is treated as the answer to a sign-in this app never began.
    /// </remarks>
    public static Callback ReadCallback(Uri redirect, string expectedState)
    {
        var query = HttpUtility.ParseQueryString(redirect.Query);

        // Some providers answer in the fragment. Entra with response_mode=query does not, but a
        // fragment costs one line to read and its absence costs a sign-in that silently never
        // completes.
        if (query.Count == 0 && !string.IsNullOrEmpty(redirect.Fragment))
            query = HttpUtility.ParseQueryString(redirect.Fragment.TrimStart('#'));

        var state = query["state"];
        if (!FixedTimeEquals(state, expectedState))
            return new Callback(null, state, "state_mismatch",
                "The sign-in answer did not match the request this app started.");

        return new Callback(query["code"], state, query["error"], query["error_description"]);
    }

    private static bool FixedTimeEquals(string? a, string? b)
    {
        if (a is null || b is null) return false;

        var left = System.Text.Encoding.UTF8.GetBytes(a);
        var right = System.Text.Encoding.UTF8.GetBytes(b);
        return System.Security.Cryptography.CryptographicOperations.FixedTimeEquals(left, right);
    }
}
