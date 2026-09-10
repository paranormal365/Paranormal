using System.Net.Http.Json;
using Ben.Data.WebApi.Client.Auth;
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
}
