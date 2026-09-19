using Ben.Web.Website.Services;
using Xunit;

namespace Ben.Web.Tests.Website;

/// <summary>
/// One name for the site, and one deliberate exception to it.
/// </summary>
/// <remarks>
/// www.ishaunted.com served the same site rather than redirecting to it, which meant a Sign in
/// with Apple begun on www could never finish: Apple posts its answer to the one registered
/// return URL on the bare name, and the state cookie set on www is not sent there. The failure
/// surfaces as a state check nothing on screen can explain, so it is worth holding in tests.
/// </remarks>
public class CanonicalHostTests
{
    [Theory]
    [InlineData("www.ishaunted.com", "ishaunted.com")]
    [InlineData("WWW.IsHaunted.com", "IsHaunted.com")]
    [InlineData("www.staging.ishaunted.com", "staging.ishaunted.com")]
    public void A_www_name_is_moved_to_the_bare_one(string host, string expected)
        => Assert.Equal(expected, CanonicalHost.ApexFor(host, "/"));

    [Theory]
    [InlineData("ishaunted.com")]
    [InlineData("localhost")]
    [InlineData("127.0.0.1")]
    [InlineData("wwwsomething.com")]   // not the prefix, merely starts with the same letters
    [InlineData("")]
    [InlineData(null)]
    public void Everything_else_is_left_where_it_is(string? host)
        => Assert.Null(CanonicalHost.ApexFor(host, "/"));

    /// <summary>
    /// Apple fetches this path on the exact host it is verifying and does not follow redirects,
    /// so redirecting it would make www permanently unverifiable — reported by the portal as a
    /// mismatch rather than as a redirect, which is the expensive way to find out.
    /// </summary>
    [Theory]
    [InlineData("/.well-known/apple-developer-domain-association.txt")]
    [InlineData("/.well-known/apple-app-site-association")]
    [InlineData("/.WELL-KNOWN/anything")]
    [InlineData("/.well-known")]
    public void A_domain_verification_path_stays_on_the_name_being_verified(string path)
        => Assert.Null(CanonicalHost.ApexFor("www.ishaunted.com", path));

    [Fact]
    public void A_well_known_lookalike_is_still_moved()
        => Assert.Equal("ishaunted.com",
            CanonicalHost.ApexFor("www.ishaunted.com", "/.well-knownish/thing"));

    /// <summary>"www." alone is not a name, and an empty host is not somewhere a browser can go.</summary>
    [Fact]
    public void A_bare_prefix_is_not_a_redirect()
        => Assert.Null(CanonicalHost.ApexFor("www.", "/"));
}
