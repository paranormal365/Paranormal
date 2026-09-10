using System.Net;
using System.Text;
using Ben.Data.WebApi.Services.Apple;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Ben.Web.Tests.Services;

/// <summary>
/// The two calls to Apple that item 229 adds. Neither may fail a sign-in or a deletion, so the
/// tests are mostly about what comes back when Apple says no, or nothing.
/// </summary>
public class AppleTokenClientTests
{
    /// <summary>Answers a scripted response and remembers what was sent.</summary>
    private sealed class Stub : HttpMessageHandler
    {
        public HttpStatusCode Status = HttpStatusCode.OK;
        public string Body = "{}";
        public bool Throw;
        public HttpRequestMessage? Last;
        public Dictionary<string, string> LastForm = new();

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Last = request;
            var form = await request.Content!.ReadAsStringAsync(ct);
            LastForm = form.Split('&').Select(kv => kv.Split('=', 2))
                .ToDictionary(kv => Uri.UnescapeDataString(kv[0]), kv => Uri.UnescapeDataString(kv[1]));
            if (Throw) throw new HttpRequestException("network down");
            return new HttpResponseMessage(Status) { Content = new StringContent(Body, Encoding.UTF8, "application/json") };
        }
    }

    private static (AppleTokenClient Client, Stub Stub) Build(bool configured = true)
    {
        var stub = new Stub();
        var options = configured ? AppleClientSecretTests.Configured().Options : AppleSigningOptions.Unconfigured;
        var client = new AppleTokenClient(new HttpClient(stub), new AppleClientSecret(options), NullLogger<AppleTokenClient>.Instance);
        return (client, stub);
    }

    private static string FakeIdToken(string sub)
    {
        static string B(string s) => Convert.ToBase64String(Encoding.UTF8.GetBytes(s)).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        return $"{B("{\"alg\":\"RS256\"}")}.{B($"{{\"sub\":\"{sub}\",\"iss\":\"https://appleid.apple.com\"}}")}.sig";
    }

    [Fact]
    public async Task ACodeIsExchangedWithASignedSecretForTheRightClient()
    {
        var (client, stub) = Build();
        stub.Body = $"{{\"refresh_token\":\"r-1\",\"access_token\":\"a-1\",\"id_token\":\"{FakeIdToken("001234.abc")}\"}}";

        var result = await client.ExchangeCodeAsync("c-1", "com.ishaunted.ios", default);

        Assert.True(result.Succeeded);
        Assert.Equal("r-1", result.RefreshToken);
        Assert.Equal("001234.abc", result.Subject);
        Assert.Equal(AppleTokenClient.TokenEndpoint, stub.Last!.RequestUri!.ToString());
        Assert.Equal("authorization_code", stub.LastForm["grant_type"]);
        Assert.Equal("c-1", stub.LastForm["code"]);
        Assert.Equal("com.ishaunted.ios", stub.LastForm["client_id"]);
        Assert.Equal(3, stub.LastForm["client_secret"].Split('.').Length);   // a JWT, freshly signed
    }

    [Theory]
    [InlineData(HttpStatusCode.BadRequest, "{\"error\":\"invalid_grant\"}", "invalid_grant")]
    [InlineData(HttpStatusCode.BadRequest, "{\"error\":\"invalid_client\"}", "invalid_client")]
    [InlineData(HttpStatusCode.InternalServerError, "", "http_500")]
    [InlineData(HttpStatusCode.OK, "{\"access_token\":\"a\"}", "no_refresh_token")]
    public async Task AppleSayingNoComesBackAsItsWordNotAnException(HttpStatusCode status, string body, string expected)
    {
        var (client, stub) = Build();
        stub.Status = status; stub.Body = body;

        var result = await client.ExchangeCodeAsync("c-1", "com.ishaunted.ios", default);

        Assert.False(result.Succeeded);
        Assert.Equal(expected, result.Error);
    }

    [Fact]
    public async Task AnUnreachableAppleIsNotAnExceptionEither()
    {
        var (client, stub) = Build();
        stub.Throw = true;

        Assert.Equal("unreachable", (await client.ExchangeCodeAsync("c-1", "com.ishaunted.ios", default)).Error);
        Assert.False(await client.RevokeAsync("r-1", "com.ishaunted.ios", default));
    }

    [Fact]
    public async Task UnconfiguredSendsNothingAtAll()
    {
        var (client, stub) = Build(configured: false);

        Assert.False(client.IsConfigured);
        Assert.Equal("not_configured", (await client.ExchangeCodeAsync("c-1", "com.ishaunted.ios", default)).Error);
        Assert.False(await client.RevokeAsync("r-1", "com.ishaunted.ios", default));
        Assert.Null(stub.Last);
    }

    [Fact]
    public async Task RevokingSendsTheTokenWithItsHintAndBelievesOnlyASuccess()
    {
        var (client, stub) = Build();

        Assert.True(await client.RevokeAsync("r-1", "com.ishaunted.web", default));
        Assert.Equal(AppleTokenClient.RevokeEndpoint, stub.Last!.RequestUri!.ToString());
        Assert.Equal("r-1", stub.LastForm["token"]);
        Assert.Equal("refresh_token", stub.LastForm["token_type_hint"]);
        Assert.Equal("com.ishaunted.web", stub.LastForm["client_id"]);

        stub.Status = HttpStatusCode.BadRequest; stub.Body = "{\"error\":\"invalid_request\"}";
        Assert.False(await client.RevokeAsync("r-1", "com.ishaunted.web", default));
    }

    [Fact]
    public void TheSubjectIsReadFromTheIdTokenWithoutTrustingIt()
    {
        Assert.Equal("001234.abc", AppleTokenClient.SubjectOf(FakeIdToken("001234.abc")));
        Assert.Null(AppleTokenClient.SubjectOf("not.a.token"));
        Assert.Null(AppleTokenClient.SubjectOf(null));
    }
}
