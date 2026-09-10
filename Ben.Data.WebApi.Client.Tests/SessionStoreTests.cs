using System.Net;
using Ben.Data.WebApi.Client.Auth;
using Ben.Web.Services.WebApi;

namespace Ben.Data.WebApi.Client.Tests;

/// <summary>
/// Signing in, being asked for a code, and being signed out from underneath.
/// </summary>
/// <remarks>
/// Every refusal here is a captured answer from a real API. Identity replies to a wrong password,
/// a two-factor account and an unconfirmed address with the same 401, and the only thing telling
/// them apart is a string in the body — which is precisely the sort of thing an invented fixture
/// would get wrong in the same direction as the code reading it.
/// </remarks>
public sealed class SessionStoreTests
{
    private const string MeBody = """
        {"userId":"6b64ab13-fb6a-4e83-2eef-08df0f397c22","email":"captured@example.test","isSuperAdmin":true,"isAdmin":false,"isModerator":true}
        """;

    private static SessionStore Build(
        StubHandler handler,
        Func<CancellationToken, Task<ItemResult<MeResponse>>>? fetchMe = null,
        StoredTokens? stored = null,
        TokenSession? reuse = null)
    {
        var http = new HttpClient(handler) { BaseAddress = new Uri("https://example.test") };
        var identity = new WebApiIdentityClient(http);
        var session = reuse ?? new TokenSession(new InMemoryTokenStorage(stored), identity);

        fetchMe ??= _ => Task.FromResult(ItemResult<MeResponse>.Ok(
            new MeResponse(Guid.Parse("6b64ab13-fb6a-4e83-2eef-08df0f397c22"),
                "captured@example.test", true, false, true)));

        return new SessionStore(session, identity, fetchMe);
    }

    // ── The happy path ────────────────────────────────────────────────────────

    /// <summary>Password accepted, identity resolved, signed in with roles.</summary>
    [Fact]
    public async Task A_good_password_signs_in_and_resolves_roles()
    {
        var store = Build(StubHandler.Always(HttpStatusCode.OK, Fixture.Read("login-200.json")));

        var seen = new List<SessionPhase>();
        store.StateChanged += s => seen.Add(s.Phase);

        await store.SignInAsync("someone@example.test", "correct horse");

        Assert.Equal(SessionPhase.SignedIn, store.State.Phase);
        Assert.True(store.State.Me!.IsSuperAdmin);
        Assert.True(store.State.Me.IsModerator);
        Assert.Equal(
            new[] { SessionPhase.Authenticating, SessionPhase.FetchingIdentity, SessionPhase.SignedIn },
            seen);
    }

    /// <summary>
    /// Roles come from the server, never from the token.
    /// </summary>
    /// <remarks>
    /// Identity's bearer tokens are opaque and data-protected, so there is nothing in one to
    /// decode. A client that tried would find no roles and quietly treat an administrator as an
    /// ordinary member — with no error to explain why their tools vanished.
    /// </remarks>
    [Fact]
    public async Task Signing_in_asks_the_server_who_this_is()
    {
        var asked = 0;
        var store = Build(
            StubHandler.Always(HttpStatusCode.OK, Fixture.Read("login-200.json")),
            fetchMe: _ =>
            {
                asked++;
                return Task.FromResult(ItemResult<MeResponse>.Ok(
                    new MeResponse(Guid.NewGuid(), "a@b.test", false, false)));
            });

        await store.SignInAsync("someone@example.test", "pw");

        Assert.Equal(1, asked);
    }

    // ── The three refusals that share a status ────────────────────────────────

    /// <summary>A two-factor account is asked for a code, not told its password is wrong.</summary>
    [Fact]
    public async Task A_two_factor_account_is_challenged()
    {
        var store = Build(StubHandler.Always(
            HttpStatusCode.Unauthorized, Fixture.Read("login-401-requires-two-factor.json")));

        await store.SignInAsync("someone@example.test", "correct horse");

        Assert.Equal(SessionPhase.TwoFactorChallenge, store.State.Phase);
        Assert.Equal(LoginFailure.RequiresTwoFactor, store.State.Failure);
        Assert.True(store.State.NeedsTwoFactor);
    }

    /// <summary>An unconfirmed address is told to confirm it, not to reset a password that was right.</summary>
    [Fact]
    public async Task An_unconfirmed_address_is_named_as_such()
    {
        var store = Build(StubHandler.Always(
            HttpStatusCode.Unauthorized, Fixture.Read("login-401-not-allowed.json")));

        await store.SignInAsync("desktop225-unconfirmed@example.test", "Fixture225pass");

        Assert.Equal(SessionPhase.SignedOut, store.State.Phase);
        Assert.Equal(LoginFailure.EmailNotConfirmed, store.State.Failure);
    }

    /// <summary>A wrong password is the only one of the three actually reported as one.</summary>
    [Fact]
    public async Task A_wrong_password_says_so()
    {
        var store = Build(StubHandler.Always(
            HttpStatusCode.Unauthorized, Fixture.Read("login-401-failed.json")));

        await store.SignInAsync("someone@example.test", "wrong");

        Assert.Equal(LoginFailure.InvalidCredentials, store.State.Failure);
    }

    /// <summary>
    /// A rate-limited sign-in carries how long to wait, because the server said.
    /// </summary>
    /// <remarks>
    /// Captured with the real header: the API answers 429 with <c>Retry-After: 60</c>. Discarding
    /// it leaves a screen guessing, or offering a button certain to be refused again — which
    /// consumes the next window too.
    /// </remarks>
    [Fact]
    public async Task A_rate_limited_sign_in_carries_the_wait()
    {
        var handler = new StubHandler().Then(_ =>
        {
            var response = new HttpResponseMessage(HttpStatusCode.TooManyRequests)
            {
                Content = new StringContent(Fixture.Read("login-429.json"),
                    System.Text.Encoding.UTF8, "application/json"),
            };
            response.Headers.Add("Retry-After", "60");
            return response;
        });

        var store = Build(handler);
        await store.SignInAsync("someone@example.test", "pw");

        Assert.Equal(LoginFailure.RateLimited, store.State.Failure);
        Assert.Equal(TimeSpan.FromSeconds(60), store.State.RetryAfter);
    }

    // ── The second factor ─────────────────────────────────────────────────────

    /// <summary>
    /// The first attempt must not carry a two-factor field at all.
    /// </summary>
    /// <remarks>
    /// An empty string is not "no code" to Identity — it is a wrong code, and it spends an attempt
    /// against an account that may never have had two-factor turned on.
    /// </remarks>
    [Fact]
    public async Task No_two_factor_field_is_sent_on_the_first_attempt()
    {
        var handler = StubHandler.Always(HttpStatusCode.OK, Fixture.Read("login-200.json"));
        var store = Build(handler);

        await store.SignInAsync("someone@example.test", "pw");

        Assert.DoesNotContain("twoFactorCode", handler.LastBody);
        Assert.DoesNotContain("twoFactorRecoveryCode", handler.LastBody);
    }

    /// <summary>The code goes with the held password, in the field that matches what was typed.</summary>
    [Theory]
    [InlineData(false, "twoFactorCode")]
    [InlineData(true, "twoFactorRecoveryCode")]
    public async Task The_second_attempt_carries_the_code_in_the_right_field(bool isRecovery, string expectedField)
    {
        var handler = new StubHandler()
            .Then(HttpStatusCode.Unauthorized, Fixture.Read("login-401-requires-two-factor.json"))
            .Then(HttpStatusCode.OK, Fixture.Read("login-200.json"));

        var store = Build(handler);
        await store.SignInAsync("someone@example.test", "correct horse");
        await store.SubmitTwoFactorAsync("123456", isRecovery);

        Assert.Contains(expectedField, handler.LastBody);
        Assert.Contains("123456", handler.LastBody);
        Assert.Contains("correct horse", handler.LastBody);   // the password was held, not re-typed
        Assert.Equal(SessionPhase.SignedIn, store.State.Phase);
    }

    /// <summary>
    /// A code typed the way it is printed still works.
    /// </summary>
    /// <remarks>
    /// Recovery codes are printed with a hyphen and people read app codes aloud in threes. The
    /// server strips both; refusing a comfortably typed code would be a failure we caused.
    /// </remarks>
    [Fact]
    public async Task Spaces_and_hyphens_in_a_code_are_ignored()
    {
        var handler = new StubHandler()
            .Then(HttpStatusCode.Unauthorized, Fixture.Read("login-401-requires-two-factor.json"))
            .Then(HttpStatusCode.OK, Fixture.Read("login-200.json"));

        var store = Build(handler);
        await store.SignInAsync("someone@example.test", "pw");
        await store.SubmitTwoFactorAsync("123 456");

        Assert.Contains("123456", handler.LastBody);
        Assert.DoesNotContain("123 456", handler.LastBody);
    }

    /// <summary>Abandoning the challenge forgets the password rather than leaving it in memory.</summary>
    [Fact]
    public async Task Cancelling_the_challenge_forgets_the_credentials()
    {
        var handler = new StubHandler()
            .Then(HttpStatusCode.Unauthorized, Fixture.Read("login-401-requires-two-factor.json"))
            .Then(HttpStatusCode.OK, Fixture.Read("login-200.json"));

        var store = Build(handler);
        await store.SignInAsync("someone@example.test", "correct horse");
        store.CancelTwoFactor();

        Assert.Equal(SessionPhase.SignedOut, store.State.Phase);

        await store.SubmitTwoFactorAsync("123456");

        Assert.Equal(1, handler.CallCount);   // nothing was sent; there was no password to send with it
        Assert.Equal(SessionPhase.SignedOut, store.State.Phase);
    }

    // ── Cold start ────────────────────────────────────────────────────────────

    /// <summary>A stored session that still works comes back signed in, with roles re-asked.</summary>
    [Fact]
    public async Task A_stored_session_is_restored()
    {
        var stored = new StoredTokens("live", "renew", DateTimeOffset.UtcNow.AddHours(1));
        var store = Build(StubHandler.Always(HttpStatusCode.OK, MeBody), stored: stored);

        await store.RestoreAsync();

        Assert.Equal(SessionPhase.SignedIn, store.State.Phase);
        Assert.True(store.State.Me!.IsSuperAdmin);
    }

    /// <summary>
    /// A stored session the server no longer accepts signs somebody out quietly.
    /// </summary>
    /// <remarks>
    /// Tokens expiring is what time passing looks like. Announcing it as a failure at every cold
    /// start teaches people to dismiss the banner that will one day matter.
    /// </remarks>
    [Fact]
    public async Task An_expired_stored_session_is_not_announced()
    {
        var stored = new StoredTokens("stale", "spent", DateTimeOffset.UtcNow.AddHours(1));
        var store = Build(
            StubHandler.Always(HttpStatusCode.Unauthorized),
            fetchMe: _ => Task.FromResult(ItemResult<MeResponse>.SessionEnded()),
            stored: stored);

        await store.RestoreAsync();

        Assert.Equal(SessionPhase.SignedOut, store.State.Phase);
        Assert.False(store.State.SessionEndedUnexpectedly);
        Assert.Null(store.State.Failure);
    }

    /// <summary>Nothing stored is simply signed out, with no request made.</summary>
    [Fact]
    public async Task A_cold_start_with_nothing_stored_asks_nothing()
    {
        var handler = new StubHandler();
        var store = Build(handler);

        await store.RestoreAsync();

        Assert.Equal(SessionPhase.SignedOut, store.State.Phase);
        Assert.Equal(0, handler.CallCount);
    }

    // ── Being signed out from underneath ──────────────────────────────────────

    /// <summary>A session ending on its own deserves a banner.</summary>
    [Fact]
    public async Task A_session_ending_unexpectedly_is_flagged()
    {
        var handler = StubHandler.Always(HttpStatusCode.Unauthorized);
        var http = new HttpClient(handler) { BaseAddress = new Uri("https://example.test") };
        var session = new TokenSession(
            new InMemoryTokenStorage(new StoredTokens("live", "r", DateTimeOffset.UtcNow.AddHours(1))),
            new WebApiIdentityClient(http));

        var store = Build(handler, reuse: session);
        await session.RestoreAsync();

        await session.HandleUnauthorizedAsync();

        Assert.Equal(SessionPhase.SignedOut, store.State.Phase);
        Assert.True(store.State.SessionEndedUnexpectedly);
    }

    /// <summary>Signing out on purpose does not. Telling somebody their session ended when they ended it is noise.</summary>
    [Fact]
    public async Task A_deliberate_sign_out_raises_no_banner()
    {
        var handler = StubHandler.Always(HttpStatusCode.OK, Fixture.Read("login-200.json"));
        var store = Build(handler);
        await store.SignInAsync("someone@example.test", "pw");

        await store.SignOutAsync();

        Assert.Equal(SessionPhase.SignedOut, store.State.Phase);
        Assert.False(store.State.SessionEndedUnexpectedly);
    }

    /// <summary>The banner clears once a screen has shown it.</summary>
    [Fact]
    public async Task Acknowledging_the_banner_clears_it()
    {
        var handler = StubHandler.Always(HttpStatusCode.Unauthorized);
        var http = new HttpClient(handler) { BaseAddress = new Uri("https://example.test") };
        var session = new TokenSession(
            new InMemoryTokenStorage(new StoredTokens("live", "r", DateTimeOffset.UtcNow.AddHours(1))),
            new WebApiIdentityClient(http));
        var store = Build(handler, reuse: session);
        await session.RestoreAsync();
        await session.HandleUnauthorizedAsync();

        store.AcknowledgeSessionEnded();

        Assert.False(store.State.SessionEndedUnexpectedly);
    }

    /// <summary>An external sign-in lands in the same signed-in state as a password does.</summary>
    [Fact]
    public async Task An_external_sign_in_resolves_identity_too()
    {
        var asked = 0;
        var store = Build(
            new StubHandler(),
            fetchMe: _ =>
            {
                asked++;
                return Task.FromResult(ItemResult<MeResponse>.Ok(
                    new MeResponse(Guid.NewGuid(), "apple@example.test", false, false)));
            });

        await store.AdoptExternalSignInAsync(new WebApiTokenResponse
        {
            AccessToken = "from-apple", RefreshToken = "r", ExpiresIn = 3600,
        });

        Assert.Equal(SessionPhase.SignedIn, store.State.Phase);
        Assert.Equal(1, asked);
    }
}
