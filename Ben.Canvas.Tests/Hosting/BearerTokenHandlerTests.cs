using System.Net;
using Ben.Canvas.Tests.Support;
using Ben.Wasm.Canvas.Services;

namespace Ben.Canvas.Tests.Hosting;

/// <summary>
/// The bearer token goes to the API and nowhere else (review correction R15).
/// </summary>
/// <remarks>
/// The persistence client may one day be handed an absolute URL that came out of a document - a link
/// card's picture, a pasted image address. A handler that attaches the token to every request would
/// hand a case member's credentials to that third party.
/// </remarks>
public sealed class BearerTokenHandlerTests
{
    private const string Api = "https://ishaunted.test/webapi";

    private static async Task<(HttpClient Client, StubHandler Inner)> BuildAsync()
    {
        var tokens = new TokenStore(new NoJs());
        await tokens.SetAsync("secret-token", "rt", 3600);
        var auth = new AuthService(new HttpClient(new ThrowingHandler()) { BaseAddress = new Uri(Api + "/") }, tokens);
        var inner = new StubHandler(HttpStatusCode.OK, "{}");
        var handler = new BearerTokenHandler(tokens, auth, Api) { InnerHandler = inner };
        return (new HttpClient(handler), inner);
    }

    [Fact]
    public async Task Requests_under_the_api_base_carry_the_token()
    {
        var (client, inner) = await BuildAsync();

        await client.GetAsync($"{Api}/api/me");

        Assert.Equal("Bearer secret-token", inner.Requests.Single().Headers.Authorization?.ToString());
    }

    [Theory]
    [InlineData("https://pbs.twimg.test/media/picture.jpg")]
    [InlineData("https://ishaunted.test/login")]
    [InlineData("https://ishaunted.test/webapi-evil/api/me")]
    [InlineData("https://ishaunted.test.evil.example/webapi/api/me")]
    public async Task A_foreign_address_never_gets_the_token(string url)
    {
        var (client, inner) = await BuildAsync();

        await client.GetAsync(url);

        Assert.Null(inner.Requests.Single().Headers.Authorization);
    }
}
