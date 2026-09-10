using System.Net;
using System.Net.Http.Headers;
using Ben.Data.WebApi.Client.Auth;
using Ben.Web.Services.WebApi;

namespace Ben.Data.WebApi.Client.Tests;

/// <summary>Attaching the token, and knowing when the server has stopped accepting it.</summary>
public sealed class BearerTokenHandlerTests
{
    private static async Task<(HttpClient client, TokenSession session, StubHandler inner)> BuildAsync(
        StubHandler inner, StoredTokens? stored = null)
    {
        var identityHandler = StubHandler.Always(HttpStatusCode.Unauthorized);
        var session = new TokenSession(
            new InMemoryTokenStorage(stored),
            new WebApiIdentityClient(new HttpClient(identityHandler) { BaseAddress = new Uri("https://example.test") }));
        await session.RestoreAsync();

        var handler = new BearerTokenHandler(session) { InnerHandler = inner };
        return (new HttpClient(handler) { BaseAddress = new Uri("https://example.test") }, session, inner);
    }

    private static StoredTokens Live() => new("live-token", "renew", DateTimeOffset.UtcNow.AddHours(1));

    [Fact]
    public async Task The_token_is_attached()
    {
        var (client, _, inner) = await BuildAsync(StubHandler.Always(HttpStatusCode.OK, "{}"), Live());

        await client.GetAsync("api/me");

        Assert.Equal("Bearer", inner.LastRequest!.Headers.Authorization!.Scheme);
        Assert.Equal("live-token", inner.LastRequest.Headers.Authorization.Parameter);
    }

    [Fact]
    public async Task Nothing_is_attached_when_nobody_is_signed_in()
    {
        var (client, _, inner) = await BuildAsync(StubHandler.Always(HttpStatusCode.OK, "{}"));

        await client.GetAsync("api/public/events");

        Assert.Null(inner.LastRequest!.Headers.Authorization);
    }

    /// <summary>
    /// A header the caller set is left alone.
    /// </summary>
    /// <remarks>
    /// This is how the Entra endpoints are reached. <c>api/auth/entra/register</c> and <c>/link</c>
    /// are authorised by a Microsoft-issued JWT under a different scheme, and overwriting it with
    /// an Identity token makes them answer 401 for a reason nothing on screen could explain.
    /// </remarks>
    [Fact]
    public async Task A_caller_supplied_token_is_not_overwritten()
    {
        var (client, _, inner) = await BuildAsync(StubHandler.Always(HttpStatusCode.OK, "{}"), Live());

        using var request = new HttpRequestMessage(HttpMethod.Post, "api/auth/entra/register");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", "an-entra-jwt");
        await client.SendAsync(request);

        Assert.Equal("an-entra-jwt", inner.LastRequest!.Headers.Authorization!.Parameter);
    }

    /// <summary>A 401 on a request that carried our token ends the session.</summary>
    [Fact]
    public async Task A_401_on_our_own_token_ends_the_session()
    {
        var (client, session, _) = await BuildAsync(StubHandler.Always(HttpStatusCode.Unauthorized), Live());

        var ended = false;
        session.SessionChanged += e => ended |= e == SessionEvent.SessionEnded;

        await client.GetAsync("api/me");

        Assert.True(ended);
        Assert.False(session.HasSession);
    }

    /// <summary>
    /// A 401 on an anonymous request does not.
    /// </summary>
    /// <remarks>
    /// A public endpoint refusing an unauthenticated read says nothing about the session, and
    /// signing somebody out over it would end a perfectly good session because they opened a page
    /// they were never signed in to read.
    /// </remarks>
    [Fact]
    public async Task A_401_on_an_anonymous_request_leaves_the_session_alone()
    {
        var (client, session, _) = await BuildAsync(StubHandler.Always(HttpStatusCode.Unauthorized));

        var ended = false;
        session.SessionChanged += e => ended |= e == SessionEvent.SessionEnded;

        await client.GetAsync("api/public/events");

        Assert.False(ended);
    }

    /// <summary>A 403 is not a 401. Being told no is not being signed out.</summary>
    [Fact]
    public async Task A_403_does_not_end_the_session()
    {
        var (client, session, _) = await BuildAsync(StubHandler.Always(HttpStatusCode.Forbidden), Live());

        var ended = false;
        session.SessionChanged += e => ended |= e == SessionEvent.SessionEnded;

        await client.GetAsync("api/admin/site-settings");

        Assert.False(ended);
        Assert.True(session.HasSession);
    }
}
