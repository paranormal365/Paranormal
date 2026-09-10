using Ben.Web.Services.WebApi;

namespace Ben.Data.WebApi.Client.Auth;

/// <summary>
/// The sign-in state machine every client screen reads:
/// signed out, authenticating, two-factor challenge, fetching identity, signed in.
/// </summary>
/// <remarks>
/// <para>A port of BenKit's <c>SessionStore</c>. Three decisions in here are load-bearing.</para>
///
/// <para><b>Roles come from the server, always.</b> Identity issues opaque data-protected tokens,
/// not JWTs, so there is nothing in an access token to decode. A client that tries gets nothing
/// back and quietly concludes the person is an ordinary member — which is how an administrator
/// ends up unable to see their own tools. Every path that acquires a token asks
/// <c>GET api/me</c> before reporting success.</para>
///
/// <para><b>A session ending is an interrupt, not a wall.</b> The store returns to signed-out and
/// raises a flag a screen can show; it does not tear the app down. Anonymous surfaces keep
/// working, because most of this product is readable without an account.</para>
///
/// <para><b>Restoring at cold start is quiet.</b> Stored tokens that the server no longer accepts
/// are an ordinary consequence of time passing, so the person is simply signed out. Announcing it
/// as a failure trains people to ignore the message that matters.</para>
/// </remarks>
public sealed class SessionStore
{
    private readonly TokenSession _session;
    private readonly IWebApiIdentityClient _identity;
    private readonly Func<CancellationToken, Task<ItemResult<MeResponse>>> _fetchMe;

    // Held only between the password step and the code step. A second factor is submitted to the
    // same /login endpoint as the password, so the credentials have to survive the round trip;
    // they are dropped the moment the challenge resolves either way.
    private string? _pendingEmail;
    private string? _pendingPassword;

    private bool _expectingDeliberateEnd;

    public SessionStore(
        TokenSession session,
        IWebApiIdentityClient identity,
        Func<CancellationToken, Task<ItemResult<MeResponse>>> fetchMe)
    {
        _session = session;
        _identity = identity;
        _fetchMe = fetchMe;
        _session.SessionChanged += OnSessionChanged;
    }

    /// <summary>The current state. Never null.</summary>
    public SessionState State { get; private set; } = SessionState.SignedOut;

    /// <summary>Fires whenever <see cref="State"/> changes.</summary>
    public event Action<SessionState>? StateChanged;

    /// <summary>
    /// Picks up a session stored by a previous run and confirms it is still good.
    /// </summary>
    /// <remarks>
    /// Optimistic then verified: the tokens are adopted first so an authenticated request can be
    /// made at all, and <c>GET api/me</c> settles whether they still work. A refusal lands quietly
    /// in signed-out with no banner — see the remarks on this class.
    /// </remarks>
    public async Task RestoreAsync(CancellationToken token = default)
    {
        await _session.RestoreAsync(token);
        if (!_session.HasSession)
        {
            Set(SessionState.SignedOut);
            return;
        }

        Set(new SessionState(SessionPhase.FetchingIdentity));

        var me = await _fetchMe(token);
        if (me.Failed || me.Item is null)
        {
            _expectingDeliberateEnd = true;   // an expired stored session is not news
            await _session.SignOutAsync();
            Set(SessionState.SignedOut);
            return;
        }

        Set(Resolved(me.Item));
    }

    /// <summary>
    /// Where a resolved identity actually lands.
    /// </summary>
    /// <remarks>
    /// An empty UserId is not a signed-in person. It is the server saying the token is valid but
    /// nothing here belongs to it — which only an external provider can produce. Reporting it as
    /// signed in would hand somebody an app in which every single page refuses them.
    /// </remarks>
    private static SessionState Resolved(MeResponse me) =>
        me.UserId == Guid.Empty
            ? new SessionState(SessionPhase.NeedsLocalAccount, me)
            : new SessionState(SessionPhase.SignedIn, me);

    /// <summary>Signs in with an email address and password.</summary>
    public Task SignInAsync(string email, string password, CancellationToken token = default)
        => AttemptAsync(email, password, twoFactorCode: null, recoveryCode: null, token);

    /// <summary>
    /// Answers a two-factor challenge, with either an authenticator code or a printed recovery code.
    /// </summary>
    /// <param name="isRecoveryCode">
    /// Which of the two the person typed. They go in different fields and Identity treats them
    /// differently; guessing by shape would spend a recovery code on a mistyped app code.
    /// </param>
    public Task SubmitTwoFactorAsync(string code, bool isRecoveryCode = false, CancellationToken token = default)
    {
        if (_pendingEmail is null || _pendingPassword is null)
        {
            // The challenge was abandoned or never started. Sending a code with no password would
            // be refused as bad credentials, which reads to the person as "your code was wrong".
            Set(SessionState.SignedOut);
            return Task.CompletedTask;
        }

        // Spaces and hyphens are how these are printed and how people read them aloud. The server
        // strips them too; rejecting a code typed comfortably is a self-inflicted failure.
        var cleaned = code.Replace(" ", string.Empty).Replace("-", string.Empty);

        return AttemptAsync(
            _pendingEmail, _pendingPassword,
            twoFactorCode: isRecoveryCode ? null : cleaned,
            recoveryCode: isRecoveryCode ? cleaned : null,
            token);
    }

    /// <summary>Abandons a two-factor challenge and forgets the held credentials.</summary>
    public void CancelTwoFactor()
    {
        ForgetPendingCredentials();
        Set(SessionState.SignedOut);
    }

    private async Task AttemptAsync(
        string email, string password,
        string? twoFactorCode, string? recoveryCode,
        CancellationToken token)
    {
        Set(new SessionState(SessionPhase.Authenticating));

        var attempt = await _identity.TryLoginAsync(email, password, twoFactorCode, recoveryCode, token);
        var response = attempt.Token;

        if (response is null || string.IsNullOrWhiteSpace(response.AccessToken))
        {
            var failure = LoginFailureMapping.From(attempt);

            if (failure == LoginFailure.RequiresTwoFactor)
            {
                _pendingEmail = email;
                _pendingPassword = password;
                Set(new SessionState(SessionPhase.TwoFactorChallenge, Failure: failure));
                return;
            }

            ForgetPendingCredentials();
            Set(new SessionState(
                SessionPhase.SignedOut,
                Failure: failure,
                RetryAfter: failure == LoginFailure.RateLimited ? attempt.RetryAfter : null));
            return;
        }

        ForgetPendingCredentials();
        await AdoptAsync(response, token);
    }

    /// <summary>
    /// Takes on a session that came from somewhere other than the password form — Sign in with
    /// Apple. The body that endpoint answers is the same one <c>/login</c> answers, deliberately,
    /// so there is nothing special to do with it.
    /// </summary>
    /// <remarks>
    /// NOT the Microsoft path. A Microsoft sign-in leaves the client holding a token issued by
    /// Microsoft rather than by us, so it is adopted directly into the token session with its own
    /// renewal and then settled by <see cref="ResolveIdentityAsync"/>.
    /// </remarks>
    public Task AdoptExternalSignInAsync(WebApiTokenResponse response, CancellationToken token = default)
        => AdoptAsync(response, token);

    /// <summary>
    /// Asks the server who the current token belongs to, and moves to the state that implies.
    /// </summary>
    /// <remarks>
    /// <para>Call after adopting a token this store did not itself obtain, and again after an
    /// external identity has been given a local account — the answer changes from an empty UserId
    /// to a real one, and nothing else tells the client that happened.</para>
    ///
    /// <para>A refusal here ends the session rather than leaving somebody in front of an app whose
    /// every page will refuse them.</para>
    /// </remarks>
    public async Task ResolveIdentityAsync(CancellationToken token = default)
    {
        Set(new SessionState(SessionPhase.FetchingIdentity));

        var me = await _fetchMe(token);
        if (me.Failed || me.Item is null)
        {
            _expectingDeliberateEnd = true;
            await _session.SignOutAsync();
            Set(new SessionState(SessionPhase.SignedOut, Failure: LoginFailure.UnknownRefusal));
            return;
        }

        Set(Resolved(me.Item));
    }

    private async Task AdoptAsync(WebApiTokenResponse response, CancellationToken token)
    {
        await _session.AdoptAsync(response, token);
        Set(new SessionState(SessionPhase.FetchingIdentity));

        var me = await _fetchMe(token);
        if (me.Failed || me.Item is null)
        {
            // Tokens that were accepted a moment ago and refused now. Rare, and worth naming
            // rather than reporting as a bad password: the credentials were plainly fine.
            _expectingDeliberateEnd = true;
            await _session.SignOutAsync();
            Set(new SessionState(SessionPhase.SignedOut, Failure: LoginFailure.UnknownRefusal));
            return;
        }

        Set(Resolved(me.Item));
    }

    /// <summary>Signs out at this person's request. There is no server endpoint; the tokens are simply dropped.</summary>
    public async Task SignOutAsync()
    {
        ForgetPendingCredentials();
        _expectingDeliberateEnd = true;
        await _session.SignOutAsync();
        Set(SessionState.SignedOut);
    }

    /// <summary>Clears the "your session ended" flag once a screen has shown it.</summary>
    public void AcknowledgeSessionEnded()
    {
        if (State.SessionEndedUnexpectedly)
            Set(State with { SessionEndedUnexpectedly = false });
    }

    private void OnSessionChanged(SessionEvent e)
    {
        if (e != SessionEvent.SessionEnded) return;

        var deliberate = _expectingDeliberateEnd;
        _expectingDeliberateEnd = false;

        ForgetPendingCredentials();
        Set(new SessionState(SessionPhase.SignedOut, SessionEndedUnexpectedly: !deliberate));
    }

    private void ForgetPendingCredentials()
    {
        _pendingEmail = null;
        _pendingPassword = null;
    }

    private void Set(SessionState state)
    {
        State = state;
        StateChanged?.Invoke(state);
    }
}
