using System.Net.Http.Json;
using Ben.Data.WebApi.Client.Auth;
using Ben.Data.WebApi.Client.External;
using Ben.Web.Services.WebApi;

namespace Ben.Data.WebApi.Client.Tests;

/// <summary>
/// The whole client, against a real Ben.Data.WebApi.
/// </summary>
/// <remarks>
/// <para>Stubbed tests prove the client behaves as the fixtures say. These prove the fixtures are
/// still what the server sends — which is the half that rots. A contract drift passes every
/// stubbed test in the file next to this one.</para>
///
/// <para><b>They skip when no API is listening</b>, and skipping is the correct resting state:
/// most runs, including CI, have no server. A skipped test says "not checked"; a failing one would
/// say "broken", and saying "broken" about a machine that simply is not running a server trains
/// people to ignore the suite.</para>
///
/// <para>Point them at a scratch database, never the shared one. Set BEN_LIVE_API to the base URL
/// and BEN_LIVE_EMAIL / BEN_LIVE_PASSWORD to an account on it:</para>
/// <code>
/// BEN_LIVE_API=http://127.0.0.1:5252 BEN_LIVE_EMAIL=... BEN_LIVE_PASSWORD=... dotnet test
/// </code>
/// </remarks>
public sealed class LiveApiTests
{
    private static readonly string? BaseUrl = Environment.GetEnvironmentVariable("BEN_LIVE_API");
    private static readonly string? Email = Environment.GetEnvironmentVariable("BEN_LIVE_EMAIL");
    private static readonly string? Password = Environment.GetEnvironmentVariable("BEN_LIVE_PASSWORD");

    private static bool Configured =>
        !string.IsNullOrWhiteSpace(BaseUrl)
        && !string.IsNullOrWhiteSpace(Email)
        && !string.IsNullOrWhiteSpace(Password);

    private static HttpClient Http() => new() { BaseAddress = new Uri(BaseUrl!) };

    /// <summary>The real sign-in, all the way to roles resolved from the server.</summary>
    [SkippableFact]
    public async Task A_real_sign_in_reaches_signed_in_with_real_roles()
    {
        Skip.IfNot(Configured, "No live API configured. Set BEN_LIVE_API, BEN_LIVE_EMAIL, BEN_LIVE_PASSWORD.");

        var identityHttp = Http();
        var identity = new WebApiIdentityClient(identityHttp);
        var session = new TokenSession(new InMemoryTokenStorage(), identity);

        var apiHttp = new HttpClient(new BearerTokenHandler(session) { InnerHandler = new HttpClientHandler() })
        { BaseAddress = new Uri(BaseUrl!) };

        var store = new SessionStore(session, identity,
            ct => ApiResponseMapper.ReadItemAsync<MeResponse>(
                apiHttp, new HttpRequestMessage(HttpMethod.Get, "api/me"), ct));

        await store.SignInAsync(Email!, Password!);

        // A two-factor account would land in the challenge, which is a correct outcome but not the
        // one this test is about. Say so rather than failing on it.
        Skip.If(store.State.NeedsTwoFactor, "That account has two-factor turned on; this test needs one that does not.");

        Assert.Equal(SessionPhase.SignedIn, store.State.Phase);
        Assert.NotNull(store.State.Me);
        Assert.Equal(Email, store.State.Me!.Email, ignoreCase: true);
        Assert.True(session.HasSession);
    }

    /// <summary>
    /// The bearer handler really does get a token onto a real authenticated request.
    /// </summary>
    /// <remarks>
    /// A stub can only prove the header was set. This proves the server accepted it, which is the
    /// claim that matters.
    /// </remarks>
    [SkippableFact]
    public async Task The_handler_reaches_an_authenticated_endpoint()
    {
        Skip.IfNot(Configured, "No live API configured.");

        var identity = new WebApiIdentityClient(Http());
        var session = new TokenSession(new InMemoryTokenStorage(), identity);

        var attempt = await identity.TryLoginAsync(Email!, Password!);
        Skip.If(attempt.RequiresTwoFactor, "That account has two-factor turned on.");
        Assert.NotNull(attempt.Token);

        await session.AdoptAsync(attempt.Token!);

        var apiHttp = new HttpClient(new BearerTokenHandler(session) { InnerHandler = new HttpClientHandler() })
        { BaseAddress = new Uri(BaseUrl!) };

        var me = await ApiResponseMapper.ReadItemAsync<MeResponse>(
            apiHttp, new HttpRequestMessage(HttpMethod.Get, "api/me"));

        Assert.False(me.Failed);
        Assert.NotNull(me.Item);
    }

    /// <summary>
    /// A refresh against the real endpoint returns a usable token.
    /// </summary>
    /// <remarks>
    /// Worth doing live because the refresh token is data-protected by the server's key ring, and
    /// nothing about that is exercised by a stub.
    /// </remarks>
    [SkippableFact]
    public async Task A_real_refresh_produces_a_working_token()
    {
        Skip.IfNot(Configured, "No live API configured.");

        var identity = new WebApiIdentityClient(Http());

        var attempt = await identity.TryLoginAsync(Email!, Password!);
        Skip.If(attempt.RequiresTwoFactor, "That account has two-factor turned on.");
        Assert.NotNull(attempt.Token?.RefreshToken);

        var refreshed = await identity.RefreshAsync(attempt.Token!.RefreshToken!);

        Assert.NotNull(refreshed);
        Assert.False(string.IsNullOrWhiteSpace(refreshed!.AccessToken));
        Assert.True(refreshed.ExpiresIn > 0);
    }

    /// <summary>
    /// A wrong password is still answered with "Failed", not something else.
    /// </summary>
    /// <remarks>
    /// This is the drift detector. If Identity ever changes that word, every stubbed test in this
    /// project keeps passing and real people start being told the wrong thing.
    /// </remarks>
    [SkippableFact]
    public async Task A_wrong_password_still_maps_to_invalid_credentials()
    {
        Skip.IfNot(Configured, "No live API configured.");

        var identity = new WebApiIdentityClient(Http());
        var attempt = await identity.TryLoginAsync(Email!, "definitely-not-the-password");

        Assert.Null(attempt.Token);
        Assert.Equal(LoginFailure.InvalidCredentials, LoginFailureMapping.From(attempt));
    }

    /// <summary>A handle nobody holds is reported free by the real endpoint.</summary>
    [SkippableFact]
    public async Task Handle_availability_answers_from_the_real_endpoint()
    {
        Skip.IfNot(Configured, "No live API configured.");

        var client = new AccountClient(Http());
        var result = await client.CheckHandleAsync($"zzz_live_{Guid.NewGuid():N}"[..24]);

        Assert.NotNull(result);
        Assert.True(result!.Available);
    }

    // ── External sign-in, as far as it can honestly be taken without a provider ──

    /// <summary>
    /// A token Apple did not sign is refused, and the refusal is a sentence.
    /// </summary>
    /// <remarks>
    /// This is as far as Sign in with Apple can be checked without a real Apple identity token,
    /// and it is worth checking: it proves the endpoint is reachable, that the server has an Apple
    /// audience configured at all (an unconfigured one answers 503, not 401), and that the client
    /// maps the refusal to something a person can read.
    /// </remarks>
    [SkippableFact]
    public async Task Apple_refuses_a_token_it_cannot_verify()
    {
        Skip.IfNot(Configured, "No live API configured.");

        var identity = new WebApiIdentityClient(Http());
        var session = new TokenSession(new InMemoryTokenStorage(), identity);
        var store = new SessionStore(session, identity,
            ct => ApiResponseMapper.ReadItemAsync<MeResponse>(
                Http(), new HttpRequestMessage(HttpMethod.Get, "api/me"), ct));

        var outcome = await new AppleSignInClient(Http(), store).SignInAsync("not.a.real.token");

        Assert.False(outcome.Succeeded);
        Assert.False(outcome.RequiresProfile);

        // A 503 here would mean Apple:ClientIds is empty on this server, which is a configuration
        // fact worth failing on rather than passing over.
        Assert.DoesNotContain("isn't switched on", outcome.Reason);
        Assert.False(string.IsNullOrWhiteSpace(outcome.Reason));
    }

    /// <summary>
    /// The Microsoft endpoints refuse a caller who holds no Microsoft token.
    /// </summary>
    /// <remarks>
    /// Both are gated on a validated Entra token, and the server reads the identity off that
    /// token's claims rather than the request body — deliberately, because trusting a body-supplied
    /// identifier there once allowed an account to be claimed by somebody who did not hold it.
    /// This proves the gate is actually on; the interactive half needs a tenant and a browser.
    /// </remarks>
    [SkippableFact]
    public async Task The_microsoft_endpoints_refuse_an_unauthenticated_caller()
    {
        Skip.IfNot(Configured, "No live API configured.");

        var client = new EntraAccountClient(Http());

        var registered = await client.RegisterAsync("Nobody At All");
        var linked = await client.LinkAsync("nobody@example.test", "whatever");

        Assert.False(registered.Succeeded);
        Assert.False(registered.ShouldLinkInstead);   // 401, not the 409 that means "use the other door"
        Assert.False(linked.Succeeded);
    }

    /// <summary>
    /// The Apple link door exists, and refuses a token Apple did not sign.
    /// </summary>
    /// <remarks>
    /// It is the door that stops an Apple relay address, or an Apple ID at a different address,
    /// silently producing a second account. This proves it is routed and that a forged token gets
    /// nowhere near the password check; the rest needs a real Apple identity token.
    /// </remarks>
    [SkippableFact]
    public async Task Apple_linking_refuses_a_token_it_cannot_verify()
    {
        Skip.IfNot(Configured, "No live API configured.");

        var identity = new WebApiIdentityClient(Http());
        var session = new TokenSession(new InMemoryTokenStorage(), identity);
        var store = new SessionStore(session, identity,
            ct => ApiResponseMapper.ReadItemAsync<MeResponse>(
                Http(), new HttpRequestMessage(HttpMethod.Get, "api/me"), ct));

        var outcome = await new AppleSignInClient(Http(), store)
            .LinkAsync("not.a.real.token", Email!, Password!);

        Assert.False(outcome.Succeeded);

        // A 404 would surface as "The server answered 404", which is how a route that was never
        // mapped shows up. A 503 would mean Apple:ClientIds is empty on this server.
        Assert.DoesNotContain("404", outcome.Reason);
        Assert.DoesNotContain("isn't switched on", outcome.Reason);

        // And nothing was signed in on the strength of a forged token.
        Assert.NotEqual(SessionPhase.SignedIn, store.State.Phase);
    }
}
