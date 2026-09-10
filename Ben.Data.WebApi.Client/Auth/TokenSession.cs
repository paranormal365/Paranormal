using Ben.Web.Services.WebApi;

namespace Ben.Data.WebApi.Client.Auth;

/// <summary>What happened to the signed-in session.</summary>
public enum SessionEvent
{
    /// <summary>A session began, or was replaced by a different one.</summary>
    SignedIn,

    /// <summary>
    /// The session is over and the tokens are gone. Raised whether the person asked for it or the
    /// server ended it underneath them.
    /// </summary>
    SessionEnded,
}

/// <summary>
/// Owns the tokens for one signed-in person: hands out a usable access token, refreshes it before
/// it expires, and ends the session when it cannot.
/// </summary>
/// <remarks>
/// <para>A port of BenKit's <c>TokenSession</c> actor, which is itself the semantics the website's
/// bearer-token handler was meant to have. Three behaviours matter and all three were learned the
/// hard way.</para>
///
/// <para><b>Refresh is single-flight.</b> A screen that opens four panels at once makes four
/// requests, all of which find the same expired token. Without a guard that is four refreshes; the
/// first rotates the refresh token and the other three present one the server has already
/// retired, so three of the four fail and the session ends on a healthy connection. The first
/// caller here starts the refresh and every other caller awaits the same task.</para>
///
/// <para><b>A failed refresh ends the session exactly once.</b> Not once per waiting caller.</para>
///
/// <para><b>An event raised before anyone is listening is held, not dropped.</b> Composition order
/// is not something the caller should have to get right, and the failure it causes is silent: a
/// session ending in the first instants after launch leaves the UI looking signed in. The held
/// event is delivered to the first subscriber and never replayed to later ones, because a banner
/// about a session that ended an hour ago is worse than no banner.</para>
/// </remarks>
public sealed class TokenSession
{
    private readonly ITokenStorage _storage;
    private readonly IWebApiIdentityClient _identity;
    private readonly Func<DateTimeOffset> _now;
    private readonly SemaphoreSlim _refreshGate = new(1, 1);

    // A separate object on purpose. The refresh gate is a semaphore held across awaits; this one
    // guards two fields for a few instructions. Using one object for both would mean a monitor
    // lock and a semaphore count living on the same reference, which reads like a deadlock even
    // when it is not.
    private readonly object _eventLock = new();

    private StoredTokens? _tokens;
    private Task<string?>? _refreshInFlight;
    private SessionEvent? _undelivered;
    private bool _hasSubscriber;

    public TokenSession(
        ITokenStorage storage,
        IWebApiIdentityClient identity,
        Func<DateTimeOffset>? now = null)
    {
        _storage = storage;
        _identity = identity;
        _now = now ?? (() => DateTimeOffset.UtcNow);
    }

    /// <summary>Fires when a session begins or ends. See the remarks on held events.</summary>
    public event Action<SessionEvent>? SessionChanged
    {
        add
        {
            _sessionChanged += value;
            SessionEvent? held;
            lock (_eventLock)
            {
                held = _hasSubscriber ? null : _undelivered;
                _hasSubscriber = true;
                _undelivered = null;
            }
            if (held is { } e) value?.Invoke(e);
        }
        remove => _sessionChanged -= value;
    }

    private Action<SessionEvent>? _sessionChanged;

    /// <summary>Whether there is a session at all, expired or not.</summary>
    public bool HasSession => _tokens is not null;

    /// <summary>The stored expiry, for a caller that wants to show it. Null when signed out.</summary>
    public DateTimeOffset? ExpiresAtUtc => _tokens?.ExpiresAtUtc;

    /// <summary>Reads whatever the last run stored. Call once at startup, before anything else.</summary>
    /// <remarks>
    /// Does NOT verify the tokens with the server and does not raise <see cref="SessionEvent.SignedIn"/>.
    /// Restoring is optimistic on purpose: the first real request settles whether the session is
    /// still good, and asking the server twice at launch only makes a cold start slower.
    /// </remarks>
    public async Task RestoreAsync(CancellationToken token = default)
    {
        _tokens = await _storage.LoadAsync(token);
    }

    /// <summary>Takes on the session a sign-in just produced.</summary>
    public async Task AdoptAsync(WebApiTokenResponse response, CancellationToken token = default)
    {
        var tokens = StoredTokens.From(response, _now());
        _tokens = tokens;
        await _storage.SaveAsync(tokens, token);
        Raise(SessionEvent.SignedIn);
    }

    /// <summary>
    /// An access token that is good to send, refreshing first if the stored one has expired.
    /// Null when there is no session, or when the refresh was refused.
    /// </summary>
    public async Task<string?> GetValidAccessTokenAsync(CancellationToken token = default)
    {
        var current = _tokens;
        if (current is null) return null;
        if (!current.IsExpiredAt(_now())) return current.AccessToken;

        Task<string?> refresh;
        await _refreshGate.WaitAsync(token);
        try
        {
            // Re-read inside the gate: another caller may have finished the whole refresh while
            // this one was queued, in which case there is nothing left to do.
            current = _tokens;
            if (current is null) return null;
            if (!current.IsExpiredAt(_now())) return current.AccessToken;

            // A session with no refresh token cannot be renewed, and the access token is already
            // spent. Ending it here is the honest answer; the alternative is sending a token known
            // to be dead and reporting the 401 as though something went wrong.
            if (!current.CanRefresh)
            {
                await EndSessionAsync();
                return null;
            }

            refresh = _refreshInFlight ??= RefreshAsync(current.RefreshToken!);
        }
        finally
        {
            _refreshGate.Release();
        }

        return await refresh;
    }

    private async Task<string?> RefreshAsync(string refreshToken)
    {
        try
        {
            var response = await _identity.RefreshAsync(refreshToken);
            if (response is null || string.IsNullOrWhiteSpace(response.AccessToken))
            {
                // The refresh token is spent, revoked, or was signed with a key ring this server no
                // longer has. Nothing the client can do restores it, so stop pretending: a retry
                // loop against a dead session is the failure mode this branch exists to prevent.
                await EndSessionAsync();
                return null;
            }

            var tokens = StoredTokens.From(response, _now());
            _tokens = tokens;
            await _storage.SaveAsync(tokens);
            return tokens.AccessToken;
        }
        catch (HttpRequestException)
        {
            // Unreachable is NOT the same as refused. Keep the tokens: the network may come back,
            // and throwing away a good session because the wifi dropped means signing in again for
            // no reason.
            return null;
        }
        finally
        {
            await _refreshGate.WaitAsync();
            try { _refreshInFlight = null; }
            finally { _refreshGate.Release(); }
        }
    }

    /// <summary>
    /// Call when a request that carried a token came back 401.
    /// </summary>
    /// <remarks>
    /// The token looked live and the server disagreed, which means the session is over — most often
    /// because the API restarted without a persisted data-protection key ring, which invalidates
    /// every token it ever issued. Reporting that as a bad password sends somebody to reset one
    /// that was always right.
    /// </remarks>
    public Task HandleUnauthorizedAsync() => _tokens is null ? Task.CompletedTask : EndSessionAsync();

    /// <summary>Ends the session deliberately. There is no server endpoint for this; sign-out is local.</summary>
    public Task SignOutAsync() => EndSessionAsync();

    private async Task EndSessionAsync()
    {
        var had = _tokens is not null;
        _tokens = null;
        await _storage.ClearAsync();
        if (had) Raise(SessionEvent.SessionEnded);
    }

    private void Raise(SessionEvent e)
    {
        bool holdIt;
        lock (_eventLock) holdIt = !_hasSubscriber;

        if (holdIt)
        {
            lock (_eventLock) _undelivered = e;
            return;
        }

        _sessionChanged?.Invoke(e);
    }
}
