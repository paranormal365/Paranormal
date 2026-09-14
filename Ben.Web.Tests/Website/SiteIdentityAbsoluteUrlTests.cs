using Ben.Data.Common;
using Xunit;

namespace Ben.Web.Tests.Website;

/// <summary>
/// What a link preview and a letter carry as an address: absolute on a site that knows its origin,
/// the path itself on one that does not.
/// </summary>
/// <remarks>
/// Pinned because a Playwright test assumed the second case could not happen: it expected an
/// absolute <c>og:image</c> and failed on the e2e host, which has no <c>BaseUrl</c> and correctly
/// wrote a relative one. The rule is the method's, so the test of it lives here.
/// </remarks>
public class SiteIdentityAbsoluteUrlTests
{
    [Theory]
    [InlineData("https://ishaunted.com", "/media/event-photo/1", "https://ishaunted.com/media/event-photo/1")]
    [InlineData("https://ishaunted.com/", "/media/event-photo/1", "https://ishaunted.com/media/event-photo/1")]
    [InlineData("https://ishaunted.com", "media/event-photo/1", "https://ishaunted.com/media/event-photo/1")]
    [InlineData("https://ishaunted.com/", "media/event-photo/1", "https://ishaunted.com/media/event-photo/1")]
    public void With_an_origin_every_path_gets_exactly_one_slash_between(string baseUrl, string path, string expected)
        => Assert.Equal(expected, new SiteIdentity { BaseUrl = baseUrl }.AbsoluteUrl(path));

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Without_an_origin_the_path_comes_back_unchanged(string baseUrl)
        => Assert.Equal("/media/event-photo/1", new SiteIdentity { BaseUrl = baseUrl }.AbsoluteUrl("/media/event-photo/1"));
}
