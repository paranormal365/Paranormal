using Ben.Data.WebApi.Client.Auth;

namespace Ben.Data.WebApi.Client.External;

/// <summary>
/// Signing in with a Microsoft work, school or personal account, start to finish.
/// </summary>
/// <remarks>
/// <para><b>A Microsoft token is not one of our sessions.</b> Unlike Sign in with Apple, which
/// hands back our own Identity tokens, this leaves the client holding a Microsoft access token
/// that our API validates under a second scheme. So the session renews at Microsoft, not at
/// <c>/refresh</c> — which is why it is adopted through
/// <see cref="TokenSession.AdoptExternalAsync"/>.</para>
///
/// <para><b>Being signed in to Microsoft is not the same as having an account here.</b> The server
/// answers <c>GET api/me</c> with an empty UserId when nothing local is linked yet, and the person
/// then has to create an account or attach the one they already have. That is a normal outcome,
/// not an error.</para>
/// </remarks>
public sealed class EntraSignInService
{
    private readonly EntraOptions _options;
    private readonly EntraTokenClient _tokens;
    private readonly IInteractiveBrowser _browser;
    private readonly TokenSession _session;
    private readonly SessionStore _store;

    public EntraSignInService(
        EntraOptions options,
        EntraTokenClient tokens,
        IInteractiveBrowser browser,
        TokenSession session,
        SessionStore store)
    {
        _options = options;
        _tokens = tokens;
        _browser = browser;
        _session = session;
        _store = store;
    }

    /// <summary>Whether to offer this at all.</summary>
    /// <remarks>
    /// The API only enables its Microsoft scheme when it is configured for one, so an unconfigured
    /// client should hide the button rather than show one that cannot work. A door that leads
    /// nowhere is worse than no door.
    /// </remarks>
    public bool IsAvailable => _options.IsConfigured;

    /// <param name="Cancelled">They closed the window. A decision, not a failure — say nothing.</param>
    public readonly record struct Outcome(bool Succeeded, string? Reason, bool Cancelled = false)
    {
        public static Outcome Ok() => new(true, null);
        public static Outcome Failed(string reason) => new(false, reason);
        public static Outcome WasCancelled() => new(false, null, Cancelled: true);
    }

    /// <summary>Runs the whole handshake and leaves the session store in its resulting state.</summary>
    public async Task<Outcome> SignInAsync(CancellationToken token = default)
    {
        if (!IsAvailable)
            return Outcome.Failed("Signing in with a Microsoft account isn't set up in this build.");

        var pkce = EntraPkce.Create();
        var state = EntraAuthorizeRequest.NewState();
        var authorizeUrl = EntraAuthorizeRequest.Build(_options, pkce, state);

        Uri? redirect;
        try
        {
            redirect = await _browser.AuthenticateAsync(authorizeUrl, CallbackScheme(_options.RedirectUri), token);
        }
        catch (InteractiveBrowserException ex)
        {
            return Outcome.Failed(ex.Message);
        }

        if (redirect is null) return Outcome.WasCancelled();

        var callback = EntraAuthorizeRequest.ReadCallback(redirect, state);
        if (callback.WasCancelled) return Outcome.WasCancelled();

        if (!callback.Succeeded)
            return Outcome.Failed(callback.ErrorDescription ?? callback.Error ?? "Microsoft didn't finish signing you in.");

        var redeemed = await _tokens.RedeemCodeAsync(callback.Code!, pkce, token);
        if (!redeemed.Succeeded)
            return Outcome.Failed(redeemed.Reason ?? "Microsoft wouldn't complete the sign-in.");

        var acquired = redeemed.Tokens!;

        // Renewal goes back to Microsoft. Our own /refresh has never seen this token and would
        // refuse it, which would end a perfectly good session at the first expiry.
        await _session.AdoptExternalAsync(
            acquired,
            async ct =>
            {
                if (string.IsNullOrWhiteSpace(acquired.RefreshToken)) return null;

                var renewed = await _tokens.RefreshAsync(acquired.RefreshToken!, ct);
                return renewed.Tokens;
            },
            persist: true,
            token);

        // Roles, and whether an account exists here at all, come from the server. There is nothing
        // in a Microsoft token that answers either.
        await _store.ResolveIdentityAsync(token);

        return Outcome.Ok();
    }

    /// <summary>
    /// The scheme a platform watches for.
    /// </summary>
    /// <remarks>
    /// For <c>msauth.com.ishaunted.desktop://auth</c> this is <c>msauth.com.ishaunted.desktop</c>.
    /// A loopback redirect has no custom scheme, and the platform layer is expected to notice that
    /// and listen rather than register.
    /// </remarks>
    internal static string CallbackScheme(string redirectUri) =>
        Uri.TryCreate(redirectUri, UriKind.Absolute, out var uri) ? uri.Scheme : redirectUri;
}
