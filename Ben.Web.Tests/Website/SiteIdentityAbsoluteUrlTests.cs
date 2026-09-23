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

    // ── the API's own links (2026-09-23) ─────────────────────────────────────

    [Fact]
    public void A_link_the_api_answers_is_built_on_the_apis_origin_not_the_sites()
    {
        var site = new SiteIdentity { BaseUrl = "https://ishaunted.com", ApiBaseUrl = "https://ishaunted.com/webapi/" };
        Assert.Equal("https://ishaunted.com/webapi/api/public/event-passes/abc.png",
                     site.ApiAbsoluteUrl("/api/public/event-passes/abc.png"));
    }

    /// <summary>
    /// Without the API's origin the path comes back as it is — never on the site's origin, which
    /// does not serve /api and would be a link that cannot open.
    /// </summary>
    [Fact]
    public void Without_the_apis_origin_the_path_is_not_put_on_the_sites()
        => Assert.Equal("/api/public/tour-passes/abc.png",
                        new SiteIdentity { BaseUrl = "https://ishaunted.com" }.ApiAbsoluteUrl("/api/public/tour-passes/abc.png"));

    /// <summary>
    /// Both deploy paths wrote the API's AppBaseUrl and never its SiteIdentity:BaseUrl, so every
    /// letter link built from it was relative. Unset, it now comes from AppBaseUrl.
    /// </summary>
    [Fact]
    public void An_unset_origin_falls_back_to_AppBaseUrl()
    {
        var site = new SiteIdentity();
        SiteIdentity.UseAppBaseUrlWhenUnset(site, "https://ishaunted.com/");
        Assert.Equal("https://ishaunted.com/event-picks/t", site.AbsoluteUrl("/event-picks/t"));
    }

    [Fact]
    public void A_configured_origin_is_not_overwritten_by_AppBaseUrl()
    {
        var site = new SiteIdentity { BaseUrl = "https://ishaunted.com" };
        SiteIdentity.UseAppBaseUrlWhenUnset(site, "http://localhost:5078");
        Assert.Equal("https://ishaunted.com", site.BaseUrl);
    }
}
