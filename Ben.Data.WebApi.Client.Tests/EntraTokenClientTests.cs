using System.Net;
using System.Web;
using Ben.Data.WebApi.Client.Auth;
using Ben.Data.WebApi.Client.External;

namespace Ben.Data.WebApi.Client.Tests;

/// <summary>Trading a code for a Microsoft token, and renewing it.</summary>
public sealed class EntraTokenClientTests
{
    private static readonly EntraOptions Options = new(
        "the-client", "api://the-client/access_as_user", "msauth.com.ishaunted.desktop://auth");

    private const string TokenBody = """
        {"token_type":"Bearer","access_token":"an-entra-jwt","expires_in":3600,"refresh_token":"renew-me"}
        """;

    private static EntraTokenClient Build(StubHandler handler) =>
        new(new HttpClient(handler), Options);

    /// <summary>
    /// The verifier goes to the token endpoint, not to the browser.
    /// </summary>
    /// <remarks>
    /// This is the half of the proof that makes an intercepted code useless. If it were omitted,
    /// every sign-in would still work — and a stolen code would work just as well for whoever
    /// stole it.
    /// </remarks>
    [Fact]
    public async Task Redeeming_a_code_sends_the_verifier()
    {
        var handler = StubHandler.Always(HttpStatusCode.OK, TokenBody);
        var pkce = EntraPkce.Create();

        await Build(handler).RedeemCodeAsync("the-code", pkce);

        var form = HttpUtility.ParseQueryString(handler.LastBody!);
        Assert.Equal("authorization_code", form["grant_type"]);
        Assert.Equal("the-code", form["code"]);
        Assert.Equal(pkce.CodeVerifier, form["code_verifier"]);
        Assert.Equal(Options.RedirectUri, form["redirect_uri"]);
    }

    [Fact]
    public async Task A_redeemed_code_becomes_a_session()
    {
        var result = await Build(StubHandler.Always(HttpStatusCode.OK, TokenBody)).RedeemCodeAsync("c", EntraPkce.Create());

        Assert.True(result.Succeeded);
        Assert.Equal("an-entra-jwt", result.Tokens!.AccessToken);
        Assert.Equal("renew-me", result.Tokens.RefreshToken);
    }

    /// <summary>The same 30-second slack every other session here uses.</summary>
    [Fact]
    public async Task The_expiry_carries_the_usual_slack()
    {
        var before = DateTimeOffset.UtcNow;
        var result = await Build(StubHandler.Always(HttpStatusCode.OK, TokenBody)).RedeemCodeAsync("c", EntraPkce.Create());
        var after = DateTimeOffset.UtcNow;

        var expected = before.AddSeconds(3600) - StoredTokens.ExpirySlack;
        Assert.InRange(result.Tokens!.ExpiresAtUtc, expected, after.AddSeconds(3600) - StoredTokens.ExpirySlack);
    }

    [Fact]
    public async Task Renewing_sends_the_refresh_token_and_not_a_code()
    {
        var handler = StubHandler.Always(HttpStatusCode.OK, TokenBody);

        await Build(handler).RefreshAsync("renew-me");

        var form = HttpUtility.ParseQueryString(handler.LastBody!);
        Assert.Equal("refresh_token", form["grant_type"]);
        Assert.Equal("renew-me", form["refresh_token"]);
        Assert.Null(form["code"]);
    }

    /// <summary>
    /// Microsoft's own description is shown, because the alternative says nothing.
    /// </summary>
    /// <remarks>
    /// "invalid_grant" alone covers an expired code, a reused code, a mismatched redirect and a
    /// failed proof. The description is the only thing that separates them, and a person reporting
    /// a problem can at least quote it.
    /// </remarks>
    [Fact]
    public async Task A_refusal_carries_microsofts_own_description()
    {
        var client = Build(StubHandler.Always(HttpStatusCode.BadRequest, """
            {"error":"invalid_grant","error_description":"AADSTS70008: The provided authorization code has expired."}
            """));

        var result = await client.RedeemCodeAsync("stale", EntraPkce.Create());

        Assert.False(result.Succeeded);
        Assert.Contains("expired", result.Reason);
    }

    [Fact]
    public async Task An_unreachable_microsoft_is_named_as_such()
    {
        var result = await Build(new StubHandler().ThenUnreachable()).RedeemCodeAsync("c", EntraPkce.Create());

        Assert.False(result.Succeeded);
        Assert.Contains("Couldn't reach Microsoft", result.Reason);
    }

    /// <summary>A 200 with no token in it is a failure, not an empty success.</summary>
    [Fact]
    public async Task A_success_with_no_token_is_a_failure()
    {
        var result = await Build(StubHandler.Always(HttpStatusCode.OK, """{"token_type":"Bearer"}"""))
            .RedeemCodeAsync("c", EntraPkce.Create());

        Assert.False(result.Succeeded);
    }
}
