using System.Net;
using Ben.Data.WebApi.Client.Auth;
using Ben.Data.WebApi.Client.External;
using Ben.Web.Services.WebApi;

namespace Ben.Data.WebApi.Client.Tests;

/// <summary>
/// Sessions that did not come from the password form: what renews them, and what happens when the
/// provider knows somebody this site does not.
/// </summary>
public sealed class ExternalSignInTests
{
    private static readonly Guid Somebody = Guid.Parse("6b64ab13-fb6a-4e83-2eef-08df0f397c22");

    private static (TokenSession session, SessionStore store) Build(
        StubHandler handler, Func<CancellationToken, Task<ItemResult<MeResponse>>> fetchMe)
    {
        var http = new HttpClient(handler) { BaseAddress = new Uri("https://example.test") };
        var identity = new WebApiIdentityClient(http);
        var session = new TokenSession(new InMemoryTokenStorage(), identity);
        return (session, new SessionStore(session, identity, fetchMe));
    }

    private static Func<CancellationToken, Task<ItemResult<MeResponse>>> Me(MeResponse me)
        => _ => Task.FromResult(ItemResult<MeResponse>.Ok(me));

    // ── Renewal goes back to whoever issued the token ──────────────────────────

    /// <summary>
    /// A Microsoft session renews at Microsoft, not at our own endpoint.
    /// </summary>
    /// <remarks>
    /// Our <c>/refresh</c> has never seen a Microsoft token and would refuse it, which would end a
    /// perfectly good session at its first expiry — roughly an hour in, and looking exactly like
    /// the app forgetting somebody at random.
    /// </remarks>
    [Fact]
    public async Task An_external_session_renews_through_its_own_provider()
    {
        var ourApi = StubHandler.Always(HttpStatusCode.OK, Fixture.Read("refresh-200.json"));
        var http = new HttpClient(ourApi) { BaseAddress = new Uri("https://example.test") };
        var session = new TokenSession(new InMemoryTokenStorage(), new WebApiIdentityClient(http));

        var renewals = 0;
        var expired = new StoredTokens("stale-entra", "entra-refresh", DateTimeOffset.UtcNow.AddMinutes(-1));

        await session.AdoptExternalAsync(expired, _ =>
        {
            renewals++;
            return Task.FromResult<StoredTokens?>(
                new StoredTokens("fresh-entra", "entra-refresh", DateTimeOffset.UtcNow.AddHours(1)));
        });

        var token = await session.GetValidAccessTokenAsync();

        Assert.Equal("fresh-entra", token);
        Assert.Equal(1, renewals);
        Assert.Equal(0, ourApi.CallCount);   // our /refresh was never asked
        Assert.True(session.IsExternalSession);
    }

    /// <summary>A provider that will not renew ends the session, exactly as a refused refresh does.</summary>
    [Fact]
    public async Task An_external_renewal_that_fails_ends_the_session()
    {
        var http = new HttpClient(new StubHandler()) { BaseAddress = new Uri("https://example.test") };
        var session = new TokenSession(new InMemoryTokenStorage(), new WebApiIdentityClient(http));

        var expired = new StoredTokens("stale", "r", DateTimeOffset.UtcNow.AddMinutes(-1));
        await session.AdoptExternalAsync(expired, _ => Task.FromResult<StoredTokens?>(null));

        var ended = false;
        session.SessionChanged += e => ended |= e == SessionEvent.SessionEnded;

        Assert.Null(await session.GetValidAccessTokenAsync());
        Assert.True(ended);
    }

    /// <summary>
    /// External renewal is single-flight too.
    /// </summary>
    /// <remarks>
    /// Microsoft rotates refresh tokens as well, so concurrent renewals race the same way: the
    /// second presents one that has already been retired.
    /// </remarks>
    [Fact]
    public async Task Concurrent_callers_share_one_external_renewal()
    {
        var http = new HttpClient(new StubHandler()) { BaseAddress = new Uri("https://example.test") };
        var session = new TokenSession(new InMemoryTokenStorage(), new WebApiIdentityClient(http));

        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var renewals = 0;

        await session.AdoptExternalAsync(
            new StoredTokens("stale", "r", DateTimeOffset.UtcNow.AddMinutes(-1)),
            async _ =>
            {
                Interlocked.Increment(ref renewals);
                await gate.Task;
                return new StoredTokens("fresh", "r", DateTimeOffset.UtcNow.AddHours(1));
            });

        var callers = Enumerable.Range(0, 8).Select(_ => session.GetValidAccessTokenAsync()).ToArray();

        var settled = 0;
        for (var i = 0; i < 200 && settled < 5; i++)
        {
            var before = renewals;
            await Task.Delay(5);
            settled = renewals == before ? settled + 1 : 0;
        }

        var started = renewals;
        gate.SetResult();
        var results = await Task.WhenAll(callers);

        Assert.Equal(1, started);
        Assert.All(results, t => Assert.Equal("fresh", t));
    }

    /// <summary>Signing out clears the external renewal, so the next session is not renewed at the old provider.</summary>
    [Fact]
    public async Task Signing_out_forgets_the_external_provider()
    {
        var http = new HttpClient(StubHandler.Always(HttpStatusCode.OK, Fixture.Read("login-200.json")))
        { BaseAddress = new Uri("https://example.test") };
        var session = new TokenSession(new InMemoryTokenStorage(), new WebApiIdentityClient(http));

        await session.AdoptExternalAsync(
            new StoredTokens("a", "r", DateTimeOffset.UtcNow.AddHours(1)),
            _ => Task.FromResult<StoredTokens?>(null));
        Assert.True(session.IsExternalSession);

        await session.SignOutAsync();

        Assert.False(session.IsExternalSession);
    }

    // ── Vouched for, but nobody here ──────────────────────────────────────────

    /// <summary>
    /// An empty UserId means the token is good and no account here belongs to it.
    /// </summary>
    /// <remarks>
    /// The server answers this rather than refusing, because refusing would be wrong: the person
    /// really is authenticated. Treating it as signed in is the bug — it hands somebody an app in
    /// which every page refuses them, for a reason none of those pages can name.
    /// </remarks>
    [Fact]
    public async Task An_external_identity_with_no_local_account_asks_for_one()
    {
        var (session, store) = Build(
            new StubHandler(),
            Me(new MeResponse(Guid.Empty, "someone@contoso.com", false, false)));

        await store.AdoptExternalSignInAsync(new WebApiTokenResponse
        { AccessToken = "a", RefreshToken = "r", ExpiresIn = 3600 });

        Assert.Equal(SessionPhase.NeedsLocalAccount, store.State.Phase);
        Assert.True(store.State.NeedsLocalAccount);
        Assert.False(store.State.IsSignedIn);
        Assert.Equal("someone@contoso.com", store.State.ExternalEmail);
    }

    /// <summary>Once an account exists, asking again is what moves them on.</summary>
    [Fact]
    public async Task Resolving_again_after_an_account_is_created_signs_them_in()
    {
        var linked = false;
        var http = new HttpClient(new StubHandler()) { BaseAddress = new Uri("https://example.test") };
        var identity = new WebApiIdentityClient(http);
        var session = new TokenSession(new InMemoryTokenStorage(), identity);

        var store = new SessionStore(session, identity, _ => Task.FromResult(
            ItemResult<MeResponse>.Ok(linked
                ? new MeResponse(Somebody, "someone@contoso.com", false, false)
                : new MeResponse(Guid.Empty, "someone@contoso.com", false, false))));

        await store.AdoptExternalSignInAsync(new WebApiTokenResponse
        { AccessToken = "a", RefreshToken = "r", ExpiresIn = 3600 });
        Assert.Equal(SessionPhase.NeedsLocalAccount, store.State.Phase);

        linked = true;
        await store.ResolveIdentityAsync();

        Assert.Equal(SessionPhase.SignedIn, store.State.Phase);
        Assert.Equal(Somebody, store.State.Me!.UserId);
    }

    /// <summary>An ordinary password sign-in never lands in that state.</summary>
    [Fact]
    public async Task A_local_account_goes_straight_to_signed_in()
    {
        var (_, store) = Build(
            StubHandler.Always(HttpStatusCode.OK, Fixture.Read("login-200.json")),
            Me(new MeResponse(Somebody, "a@b.test", false, false)));

        await store.SignInAsync("a@b.test", "pw");

        Assert.Equal(SessionPhase.SignedIn, store.State.Phase);
        Assert.False(store.State.NeedsLocalAccount);
    }
}
