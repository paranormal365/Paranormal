using System.Net;
using Ben.Data.WebApi.Client.Auth;
using Ben.Web.Services.WebApi;

namespace Ben.Data.WebApi.Client.Tests;

/// <summary>
/// Holding a session: refreshing it once rather than four times, and ending it when it is over.
/// </summary>
public sealed class TokenSessionTests
{
    private static readonly string LoginBody = Fixture.Read("login-200.json");
    private static readonly string RefreshBody = Fixture.Read("refresh-200.json");

    private static (TokenSession session, StubHandler handler, InMemoryTokenStorage storage) Build(
        StubHandler handler, DateTimeOffset? now = null, StoredTokens? stored = null)
    {
        var clock = now ?? DateTimeOffset.UtcNow;
        var storage = new InMemoryTokenStorage(stored);
        var identity = new WebApiIdentityClient(
            new HttpClient(handler) { BaseAddress = new Uri("https://example.test") });
        return (new TokenSession(storage, identity, () => clock), handler, storage);
    }

    private static StoredTokens Expired(DateTimeOffset now) =>
        new("stale-access", "good-refresh", now.AddMinutes(-1));

    // ── The single-flight guarantee ───────────────────────────────────────────

    /// <summary>
    /// Four panels opening at once must produce ONE refresh.
    /// </summary>
    /// <remarks>
    /// Refreshing rotates the refresh token, so a second concurrent refresh presents one the server
    /// has already retired. Without the guard, three of four callers are refused and the session
    /// ends on a perfectly healthy connection — a failure that looks like an outage and is not one.
    /// </remarks>
    [Fact]
    public async Task Concurrent_callers_share_one_refresh()
    {
        var now = DateTimeOffset.UtcNow;

        // The handler yields rather than blocking, so the eight callers are genuinely in flight at
        // the same time. A handler that slept on the thread would run the whole request inside the
        // caller's own lock and the test would pass with the guard deleted.
        var handler = StubHandler.Always(HttpStatusCode.OK, RefreshBody);
        handler.Gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var (session, _, _) = Build(handler, now, Expired(now));
        await session.RestoreAsync();

        var callers = Enumerable.Range(0, 8)
            .Select(_ => session.GetValidAccessTokenAsync())
            .ToArray();

        // Let every caller reach the handler, or be parked behind the one that did.
        var settled = 0;
        for (var i = 0; i < 200 && settled < 5; i++)
        {
            var before = handler.CallCount;
            await Task.Delay(5);
            settled = handler.CallCount == before ? settled + 1 : 0;
        }

        var reachedTheServer = handler.CallCount;
        handler.Gate.SetResult();

        var results = await Task.WhenAll(callers);

        Assert.Equal(1, reachedTheServer);
        Assert.All(results, t => Assert.Equal("REDACTED-ACCESS-TOKEN", t));
    }

    /// <summary>A refused refresh ends the session once, not once per waiting caller.</summary>
    [Fact]
    public async Task A_refused_refresh_ends_the_session_exactly_once()
    {
        var now = DateTimeOffset.UtcNow;
        var handler = StubHandler.Always(HttpStatusCode.Unauthorized, """{"detail":"Failed"}""");
        var (session, _, storage) = Build(handler, now, Expired(now));
        await session.RestoreAsync();

        var ended = 0;
        session.SessionChanged += e => { if (e == SessionEvent.SessionEnded) Interlocked.Increment(ref ended); };

        var results = await Task.WhenAll(Enumerable.Range(0, 6)
            .Select(_ => session.GetValidAccessTokenAsync()));

        Assert.All(results, Assert.Null);
        Assert.Equal(1, ended);
        Assert.Null(await storage.LoadAsync());
    }

    // ── Unreachable is not refused ────────────────────────────────────────────

    /// <summary>
    /// A dropped connection must not throw the session away.
    /// </summary>
    /// <remarks>
    /// The network coming back is the common case. Signing somebody out because their wifi blinked
    /// costs them a password and a two-factor code for nothing, and the server never refused
    /// anything.
    /// </remarks>
    [Fact]
    public async Task An_unreachable_server_keeps_the_session()
    {
        var now = DateTimeOffset.UtcNow;
        var handler = new StubHandler().ThenUnreachable();
        var (session, _, storage) = Build(handler, now, Expired(now));
        await session.RestoreAsync();

        var ended = false;
        session.SessionChanged += e => ended |= e == SessionEvent.SessionEnded;

        Assert.Null(await session.GetValidAccessTokenAsync());
        Assert.False(ended);
        Assert.True(session.HasSession);
        Assert.NotNull(await storage.LoadAsync());
    }

    // ── Expiry arithmetic ─────────────────────────────────────────────────────

    /// <summary>A token still inside its life is handed out without asking the server anything.</summary>
    [Fact]
    public async Task A_live_token_is_used_as_is()
    {
        var now = DateTimeOffset.UtcNow;
        var handler = new StubHandler();
        var (session, _, _) = Build(handler, now, new StoredTokens("live", "r", now.AddMinutes(10)));
        await session.RestoreAsync();

        Assert.Equal("live", await session.GetValidAccessTokenAsync());
        Assert.Equal(0, handler.CallCount);
    }

    /// <summary>
    /// The slack is real: a token in its last seconds is refreshed rather than sent.
    /// </summary>
    /// <remarks>
    /// Sent, it would very likely be refused mid-flight, and the 401 that came back would be
    /// indistinguishable from a session that genuinely ended.
    /// </remarks>
    [Fact]
    public async Task A_token_inside_the_slack_is_refreshed_before_it_is_sent()
    {
        var now = DateTimeOffset.UtcNow;
        var response = new WebApiTokenResponse { AccessToken = "a", RefreshToken = "r", ExpiresIn = 3600 };

        var tokens = StoredTokens.From(response, now);

        Assert.Equal(now.AddSeconds(3600) - StoredTokens.ExpirySlack, tokens.ExpiresAtUtc);
        Assert.False(tokens.IsExpiredAt(now.AddSeconds(3600) - StoredTokens.ExpirySlack - TimeSpan.FromSeconds(1)));
        Assert.True(tokens.IsExpiredAt(now.AddSeconds(3600) - StoredTokens.ExpirySlack));
        Assert.True(tokens.IsExpiredAt(now.AddSeconds(3599)));   // inside the slack, before the server's expiry
    }

    /// <summary>A session with no refresh token ends rather than sending a token known to be dead.</summary>
    [Fact]
    public async Task A_session_that_cannot_be_renewed_ends_when_it_expires()
    {
        var now = DateTimeOffset.UtcNow;
        var handler = new StubHandler();
        var (session, _, _) = Build(handler, now, new StoredTokens("stale", RefreshToken: null, now.AddMinutes(-1)));
        await session.RestoreAsync();

        var ended = false;
        session.SessionChanged += e => ended |= e == SessionEvent.SessionEnded;

        Assert.Null(await session.GetValidAccessTokenAsync());
        Assert.True(ended);
        Assert.Equal(0, handler.CallCount);   // nothing was sent; there was nothing to send
    }

    // ── The held event ────────────────────────────────────────────────────────

    /// <summary>
    /// An event raised before anyone is listening is delivered to the first subscriber.
    /// </summary>
    /// <remarks>
    /// The bug this prevents is silent: a session ending in the first instants after launch, before
    /// the screen has wired itself up, leaves the app looking signed in to somebody who is not.
    /// This was a real defect on the iOS side, found only because the event stream was tested
    /// directly.
    /// </remarks>
    [Fact]
    public async Task An_event_raised_before_anyone_listens_is_not_lost()
    {
        var now = DateTimeOffset.UtcNow;
        var handler = StubHandler.Always(HttpStatusCode.Unauthorized, """{"detail":"Failed"}""");
        var (session, _, _) = Build(handler, now, Expired(now));
        await session.RestoreAsync();

        await session.GetValidAccessTokenAsync();   // ends the session with nobody subscribed

        SessionEvent? received = null;
        session.SessionChanged += e => received = e;

        Assert.Equal(SessionEvent.SessionEnded, received);
    }

    /// <summary>A held event reaches the first subscriber only. A stale banner an hour later is worse than none.</summary>
    [Fact]
    public async Task A_held_event_is_not_replayed_to_later_subscribers()
    {
        var now = DateTimeOffset.UtcNow;
        var handler = StubHandler.Always(HttpStatusCode.Unauthorized, """{"detail":"Failed"}""");
        var (session, _, _) = Build(handler, now, Expired(now));
        await session.RestoreAsync();
        await session.GetValidAccessTokenAsync();

        session.SessionChanged += _ => { };          // first subscriber takes the held event

        SessionEvent? second = null;
        session.SessionChanged += e => second = e;

        Assert.Null(second);
    }

    // ── Unauthorized handling ─────────────────────────────────────────────────

    /// <summary>
    /// A 401 on a token that looked live means the session is over, not that a password was wrong.
    /// </summary>
    /// <remarks>
    /// The usual cause is the API restarting without a persisted data-protection key ring, which
    /// invalidates every token it ever issued. Reporting that as a credential problem sends people
    /// to reset passwords that were always right.
    /// </remarks>
    [Fact]
    public async Task A_401_on_a_live_token_ends_the_session()
    {
        var now = DateTimeOffset.UtcNow;
        var (session, _, storage) = Build(new StubHandler(), now, new StoredTokens("live", "r", now.AddMinutes(10)));
        await session.RestoreAsync();

        var ended = false;
        session.SessionChanged += e => ended |= e == SessionEvent.SessionEnded;

        await session.HandleUnauthorizedAsync();

        Assert.True(ended);
        Assert.False(session.HasSession);
        Assert.Null(await storage.LoadAsync());
    }

    /// <summary>A 401 when nobody is signed in is not an event. There was no session to end.</summary>
    [Fact]
    public async Task A_401_with_no_session_raises_nothing()
    {
        var (session, _, _) = Build(new StubHandler());
        await session.RestoreAsync();

        var raised = false;
        session.SessionChanged += _ => raised = true;

        await session.HandleUnauthorizedAsync();

        Assert.False(raised);
    }

    /// <summary>Adopting a sign-in stores it, so the next launch finds it.</summary>
    [Fact]
    public async Task Adopting_a_sign_in_persists_it()
    {
        var now = DateTimeOffset.UtcNow;
        var (session, _, storage) = Build(new StubHandler(), now);

        await session.AdoptAsync(new WebApiTokenResponse
        {
            AccessToken = "fresh", RefreshToken = "renew", ExpiresIn = 3600, TokenType = "Bearer",
        });

        var stored = await storage.LoadAsync();
        Assert.NotNull(stored);
        Assert.Equal("fresh", stored!.AccessToken);
        Assert.Equal(now.AddSeconds(3600) - StoredTokens.ExpirySlack, stored.ExpiresAtUtc);
    }
}
