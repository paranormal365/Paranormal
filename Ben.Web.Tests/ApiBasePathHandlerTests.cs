using Ben.Web.Services.WebApi;
using Xunit;

namespace Ben.Web.Tests;

/// <summary>
/// Covers the rule that keeps the API's sub-path attached to every outgoing call.
/// </summary>
/// <remarks>
/// <para>The bug these guard against cost an afternoon and produced no error anywhere.
/// <c>HttpClient.BaseAddress</c> resolves by ordinary URI rules, so a request path starting with
/// <c>/</c> replaces the base address's path instead of extending it. With the API mounted under
/// <c>/webapi</c>, every one of the ~495 leading-slash call sites silently addressed the site root
/// and got a 404 - including the <c>/api/me</c> call that bridges an Entra sign-in, which read the
/// failure as "no linked account" and left the user apparently signed out.</para>
///
/// <para>It could not reproduce in development, where the API is served from an origin root and
/// discarding an empty base path changes nothing. The origin-root cases below are therefore as
/// important as the sub-path ones: they pin the handler's inertness where the bug cannot occur.</para>
/// </remarks>
public class ApiBasePathHandlerTests
{
    [Theory]
    [InlineData("https://ishaunted.com/webapi", "/webapi")]
    [InlineData("https://ishaunted.com/webapi/", "/webapi")]      // trailing slash is not a difference
    [InlineData("https://ishaunted.com/a/b", "/a/b")]             // nested sub-path
    public void ExtractBasePath_returns_the_path_when_the_api_is_mounted_under_one(string url, string expected)
    {
        Assert.Equal(expected, ApiBasePathHandler.ExtractBasePath(url));
    }

    [Theory]
    [InlineData("https://ishaunted.com")]
    [InlineData("https://ishaunted.com/")]
    [InlineData("http://localhost:5001")]
    [InlineData("http://localhost:5001/")]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not-a-url")]
    public void ExtractBasePath_is_empty_for_an_origin_root_or_junk(string? url)
    {
        // Empty means the handler does nothing at all - the development configuration.
        Assert.Equal(string.Empty, ApiBasePathHandler.ExtractBasePath(url));
    }

    [Theory]
    [InlineData("/api/me", "/webapi/api/me")]
    [InlineData("/api/public/cases", "/webapi/api/public/cases")]
    [InlineData("/", "/webapi/")]
    public void ApplyBasePath_prefixes_a_root_relative_path(string requested, string expected)
    {
        Assert.Equal(expected, ApiBasePathHandler.ApplyBasePath("/webapi", requested));
    }

    [Theory]
    [InlineData("/webapi/api/me")]
    [InlineData("/WEBAPI/api/me")]   // IIS paths are case-insensitive; do not double-prefix
    [InlineData("/webapi")]
    public void ApplyBasePath_leaves_an_already_prefixed_path_alone(string requested)
    {
        // Guards against a retried or already-correct request becoming /webapi/webapi/...
        Assert.Equal(requested, ApiBasePathHandler.ApplyBasePath("/webapi", requested));
    }

    [Fact]
    public void ApplyBasePath_does_not_confuse_a_lookalike_prefix()
    {
        // "/webapiv2" starts with "/webapi" as text but is a different path, so it must be prefixed.
        Assert.Equal("/webapi/webapiv2/thing", ApiBasePathHandler.ApplyBasePath("/webapi", "/webapiv2/thing"));
    }

    [Theory]
    [InlineData("/api/me")]
    [InlineData("/webapi/api/me")]
    public void ApplyBasePath_is_a_no_op_when_there_is_no_base_path(string requested)
    {
        Assert.Equal(requested, ApiBasePathHandler.ApplyBasePath(string.Empty, requested));
    }

    [Fact]
    public async Task Handler_rewrites_the_request_uri_and_keeps_the_query_string()
    {
        var spy = new CapturingHandler();
        var handler = new ApiBasePathHandler("https://ishaunted.com/webapi") { InnerHandler = spy };
        using var client = new HttpClient(handler) { BaseAddress = new Uri("https://ishaunted.com/webapi") };

        await client.GetAsync("/api/public/cases?page=1&pageSize=1");

        Assert.Equal("https://ishaunted.com/webapi/api/public/cases?page=1&pageSize=1",
                     spy.LastUri!.ToString());
    }

    [Fact]
    public async Task Handler_leaves_requests_alone_when_the_api_is_at_an_origin_root()
    {
        var spy = new CapturingHandler();
        var handler = new ApiBasePathHandler("http://localhost:5001") { InnerHandler = spy };
        using var client = new HttpClient(handler) { BaseAddress = new Uri("http://localhost:5001") };

        await client.GetAsync("/api/me");

        Assert.Equal("http://localhost:5001/api/me", spy.LastUri!.ToString());
    }

    private sealed class CapturingHandler : HttpMessageHandler
    {
        public Uri? LastUri { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            LastUri = request.RequestUri;
            return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK));
        }
    }
}
