using System.Net;
using System.Net.Http.Headers;
using Ben.Data.WebApi.Services.LinkUnfurl;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace Ben.Web.Tests.Services;

/// <summary>
/// The guarded fetch: resolve, vet every address, connect only to the vetted one, follow at most
/// three redirects (each vetted again), cap the body as it arrives (canvas plan M6-08, R21).
/// </summary>
/// <remarks>
/// A stub resolver and a stub handler stand in for DNS and the network, so each rule is exercised
/// exactly and nothing leaves this machine. The handler records every request it was asked to send;
/// "refused before any request" means that count stayed at zero.
/// </remarks>
public sealed class SafeUrlFetcherTests
{
    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> answer) : HttpMessageHandler
    {
        public List<HttpRequestMessage> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Requests.Add(request);
            return Task.FromResult(answer(request));
        }
    }

    private static readonly Dictionary<string, IPAddress[]> Dns = new(StringComparer.OrdinalIgnoreCase)
    {
        ["example.com"]  = [IPAddress.Parse("93.184.216.34")],
        ["other.test"]   = [IPAddress.Parse("93.184.216.35")],
        ["private.test"] = [IPAddress.Parse("10.0.0.1")],
        ["both.test"]    = [IPAddress.Parse("93.184.216.34"), IPAddress.Parse("10.0.0.1")],
        ["mapped.test"]  = [IPAddress.Parse("::ffff:169.254.169.254")],
        ["empty.test"]   = [],
    };

    private static (SafeUrlFetcher Fetcher, StubHandler Handler) Build(Func<HttpRequestMessage, HttpResponseMessage> answer)
    {
        var handler = new StubHandler(answer);
        var factory = new Mock<IHttpClientFactory>();
        factory.Setup(f => f.CreateClient(SafeUrlFetcher.ClientName)).Returns(() => new HttpClient(handler, disposeHandler: false));
        var fetcher = new SafeUrlFetcher(factory.Object, NullLogger<SafeUrlFetcher>.Instance,
            (host, _) => Task.FromResult(Dns.TryGetValue(host, out var a) ? a : throw new System.Net.Sockets.SocketException(11001)));
        return (fetcher, handler);
    }

    private static HttpResponseMessage Html(string body = "<html><title>Hi</title></html>")
        => new(HttpStatusCode.OK) { Content = new StringContent(body, System.Text.Encoding.UTF8, "text/html") };

    [Fact]
    public async Task A_name_that_resolves_to_a_private_address_is_refused_before_any_request()
    {
        var (fetcher, handler) = Build(_ => Html());

        var result = await fetcher.FetchAsync(new Uri("https://private.test/"), "text/html", 1_048_576, default);

        Assert.NotNull(result.Refusal);
        Assert.Null(result.Body);
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task A_name_with_one_public_and_one_private_address_is_refused()
    {
        // Rebinding bait: a filter that checks the first answer and connects to whichever the OS picks.
        var (fetcher, handler) = Build(_ => Html());

        var result = await fetcher.FetchAsync(new Uri("https://both.test/"), "text/html", 1_048_576, default);

        Assert.NotNull(result.Refusal);
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task An_ipv6_mapped_metadata_address_is_refused()
    {
        var (fetcher, handler) = Build(_ => Html());

        var result = await fetcher.FetchAsync(new Uri("https://mapped.test/"), "text/html", 1_048_576, default);

        Assert.NotNull(result.Refusal);
        Assert.Empty(handler.Requests);
    }

    [Theory]
    [InlineData("http://example.com/")]
    [InlineData("https://127.0.0.1/")]
    [InlineData("https://example.com:8080/")]
    public async Task A_url_the_policy_refuses_never_reaches_the_resolver_or_the_network(string url)
    {
        var (fetcher, handler) = Build(_ => Html());

        var result = await fetcher.FetchAsync(new Uri(url), "text/html", 1_048_576, default);

        Assert.NotNull(result.Refusal);
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task A_name_that_does_not_resolve_is_refused_without_a_request()
    {
        var (fetcher, handler) = Build(_ => Html());

        Assert.NotNull((await fetcher.FetchAsync(new Uri("https://nowhere.test/"), "text/html", 1_048_576, default)).Refusal);
        Assert.NotNull((await fetcher.FetchAsync(new Uri("https://empty.test/"), "text/html", 1_048_576, default)).Refusal);
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task The_request_carries_the_vetted_address_and_nothing_of_ours()
    {
        var (fetcher, handler) = Build(_ => Html());

        var result = await fetcher.FetchAsync(new Uri("https://example.com/page#fragment"), "text/html", 1_048_576, default);

        Assert.Null(result.Refusal);
        Assert.Equal(200, result.StatusCode);
        var request = Assert.Single(handler.Requests);
        Assert.True(request.Options.TryGetValue(SafeUrlFetcher.VettedAddressKey, out var vetted));
        Assert.Equal(IPAddress.Parse("93.184.216.34"), vetted);
        Assert.Equal("https://example.com/page", request.RequestUri!.AbsoluteUri);   // fragment never sent
        Assert.Null(request.Headers.Authorization);
        Assert.False(request.Headers.Contains("Cookie"));
        Assert.Contains("IsHaunted-LinkPreview", request.Headers.UserAgent.ToString());
        Assert.Equal(HttpMethod.Get, request.Method);
    }

    [Fact]
    public async Task A_redirect_to_a_private_address_is_refused_at_that_hop()
    {
        var (fetcher, handler) = Build(r => r.RequestUri!.Host == "example.com"
            ? new HttpResponseMessage(HttpStatusCode.Found) { Headers = { Location = new Uri("https://192.168.1.1/admin") } }
            : Html());

        var result = await fetcher.FetchAsync(new Uri("https://example.com/"), "text/html", 1_048_576, default);

        Assert.NotNull(result.Refusal);
        Assert.Single(handler.Requests);   // the first hop only
    }

    [Fact]
    public async Task A_relative_redirect_is_followed_and_vetted_again()
    {
        var (fetcher, handler) = Build(r => r.RequestUri!.AbsolutePath == "/old"
            ? new HttpResponseMessage(HttpStatusCode.MovedPermanently) { Headers = { Location = new Uri("/new", UriKind.Relative) } }
            : Html());

        var result = await fetcher.FetchAsync(new Uri("https://example.com/old"), "text/html", 1_048_576, default);

        Assert.Null(result.Refusal);
        Assert.Equal(new Uri("https://example.com/new"), result.FinalUrl);
        Assert.Equal(2, handler.Requests.Count);
        Assert.All(handler.Requests, r => Assert.True(r.Options.TryGetValue(SafeUrlFetcher.VettedAddressKey, out _)));
    }

    [Fact]
    public async Task Four_redirects_are_refused()
    {
        var (fetcher, handler) = Build(r =>
        {
            var n = int.Parse(r.RequestUri!.AbsolutePath.Trim('/') is { Length: > 0 } p ? p : "0", System.Globalization.CultureInfo.InvariantCulture);
            return new HttpResponseMessage(HttpStatusCode.TemporaryRedirect) { Headers = { Location = new Uri($"https://example.com/{n + 1}") } };
        });

        var result = await fetcher.FetchAsync(new Uri("https://example.com/0"), "text/html", 1_048_576, default);

        Assert.NotNull(result.Refusal);
        Assert.Contains("redirect", result.Refusal);
        Assert.Equal(4, handler.Requests.Count);   // the original and three followed redirects
    }

    [Fact]
    public async Task A_body_one_byte_over_the_cap_is_refused_even_without_a_content_length()
    {
        var body = new byte[1_048_577];
        var (fetcher, _) = Build(_ =>
        {
            var content = new StreamContent(new MemoryStream(body));   // no Content-Length promised up front
            content.Headers.ContentType = new MediaTypeHeaderValue("text/html");
            content.Headers.ContentLength = null;
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = content };
        });

        var result = await fetcher.FetchAsync(new Uri("https://example.com/"), "text/html", 1_048_576, default);

        Assert.NotNull(result.Refusal);
        Assert.Contains("large", result.Refusal);
        Assert.Null(result.Body);
    }

    [Fact]
    public async Task A_body_exactly_at_the_cap_is_accepted()
    {
        var (fetcher, _) = Build(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new ByteArrayContent(new byte[1_048_576]) { Headers = { ContentType = new MediaTypeHeaderValue("text/html") } },
        });

        var result = await fetcher.FetchAsync(new Uri("https://example.com/"), "text/html", 1_048_576, default);

        Assert.Null(result.Refusal);
        Assert.Equal(1_048_576, result.Body!.Length);
    }

    [Fact]
    public async Task The_wrong_kind_of_content_is_refused()
    {
        var (fetcher, _) = Build(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new ByteArrayContent([0x89, 0x50, 0x4E, 0x47]) { Headers = { ContentType = new MediaTypeHeaderValue("image/png") } },
        });

        var result = await fetcher.FetchAsync(new Uri("https://example.com/"), "text/html", 1_048_576, default);

        Assert.NotNull(result.Refusal);
        Assert.Null(result.Body);
    }

    [Fact]
    public async Task A_non_200_answer_is_reported_without_a_body()
    {
        var (fetcher, _) = Build(_ => new HttpResponseMessage(HttpStatusCode.InternalServerError) { Content = new StringContent("boom") });

        var result = await fetcher.FetchAsync(new Uri("https://example.com/"), "text/html", 1_048_576, default);

        Assert.Equal(500, result.StatusCode);
        Assert.Null(result.Body);
    }

    // ── the real handler ─────────────────────────────────────────────────────

    [Fact]
    public void The_handler_follows_no_redirect_sends_no_cookie_uses_no_proxy_and_recycles_connections()
    {
        using var handler = SafeUrlFetcher.CreateHandler();

        Assert.False(handler.AllowAutoRedirect);
        Assert.False(handler.UseCookies);
        Assert.False(handler.UseProxy);
        Assert.Null(handler.Proxy);
        Assert.NotNull(handler.ConnectCallback);
        Assert.True(handler.PooledConnectionLifetime <= TimeSpan.FromMinutes(2), "a pooled connection must not outlive its vetting by long");
        Assert.True(handler.ConnectTimeout <= TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task The_handler_refuses_to_connect_without_a_vetted_address()
    {
        using var client = new HttpClient(SafeUrlFetcher.CreateHandler());

        var error = await Assert.ThrowsAsync<HttpRequestException>(() => client.GetAsync("https://example.com/"));

        Assert.Contains("vetted", error.GetBaseException().Message + error.Message);
    }

    [Fact]
    public async Task The_handler_refuses_to_connect_to_a_forbidden_vetted_address()
    {
        using var client = new HttpClient(SafeUrlFetcher.CreateHandler());
        using var request = new HttpRequestMessage(HttpMethod.Get, "https://example.com/");
        request.Options.Set(SafeUrlFetcher.VettedAddressKey, IPAddress.Loopback);

        var error = await Assert.ThrowsAsync<HttpRequestException>(() => client.SendAsync(request));

        Assert.Contains("forbidden", error.GetBaseException().Message + error.Message);
    }
}
