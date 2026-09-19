using Xunit;
using Ben.Data.Common.Enums;

namespace Ben.Web.Tests.Services;

/// <summary>
/// The rule that keeps a tour's own accounts from becoming a link-laundering surface (item 233).
/// </summary>
/// <remarks>
/// A business types these in and a visitor clicks them without reading the address — the icon is
/// the whole promise. So an Instagram link has to be an Instagram link.
/// </remarks>
public sealed class SocialPlatformsTests
{
    [Theory]
    [InlineData(SocialPlatform.Instagram, "https://instagram.com/printersalley")]
    [InlineData(SocialPlatform.Instagram, "https://www.instagram.com/printersalley")]
    [InlineData(SocialPlatform.Facebook, "https://m.facebook.com/printersalley")]
    [InlineData(SocialPlatform.X, "https://x.com/printersalley")]
    [InlineData(SocialPlatform.X, "https://twitter.com/printersalley")]
    [InlineData(SocialPlatform.YouTube, "https://youtu.be/abcdef")]
    [InlineData(SocialPlatform.BlueSky, "https://bsky.app/profile/printersalley")]
    [InlineData(SocialPlatform.Rumble, "https://rumble.com/c/printersalley")]
    [InlineData(SocialPlatform.TikTok, "http://tiktok.com/@printersalley")]
    public void A_link_on_the_service_is_allowed(SocialPlatform platform, string url) =>
        Assert.True(SocialPlatforms.IsAllowed(platform, url));

    [Theory]
    // The whole point: somebody else's site behind a name the reader trusts.
    [InlineData(SocialPlatform.Instagram, "https://evil.example.com/instagram.com")]
    [InlineData(SocialPlatform.Instagram, "https://instagram.com.evil.example.com/x")]
    [InlineData(SocialPlatform.Facebook, "https://facebook.evil.example.com")]
    // A suffix match that is not a sub-domain: "notinstagram.com" ends with the letters but is a
    // different registrable name, which is why the check is on a dot boundary.
    [InlineData(SocialPlatform.Instagram, "https://notinstagram.com/printersalley")]
    [InlineData(SocialPlatform.X, "https://nitter.example.com/printersalley")]
    public void A_link_somewhere_else_is_refused(SocialPlatform platform, string url) =>
        Assert.False(SocialPlatforms.IsAllowed(platform, url));

    [Theory]
    [InlineData("javascript:alert(1)")]
    [InlineData("data:text/html,<script>alert(1)</script>")]
    [InlineData("/o/somebody/tours/theirs")]
    [InlineData("")]
    [InlineData(null)]
    public void Only_an_absolute_web_address_is_allowed(string? url)
    {
        // Even for Website, which may point anywhere: "anywhere" means any http or https site,
        // not any scheme. A relative link would resolve against our own domain, and javascript:
        // on a page anyone can publish to is the oldest trick there is.
        Assert.False(SocialPlatforms.IsAllowed(SocialPlatform.Website, url));
        Assert.False(SocialPlatforms.IsAllowed(SocialPlatform.Instagram, url));
    }

    [Fact]
    public void A_website_may_point_anywhere_on_the_web() =>
        Assert.True(SocialPlatforms.IsAllowed(SocialPlatform.Website, "https://printersalleywalks.com/tours"));

    [Fact]
    public void Every_platform_offered_has_a_name_and_a_rule()
    {
        foreach (var platform in SocialPlatforms.All)
        {
            Assert.False(string.IsNullOrWhiteSpace(SocialPlatforms.DisplayName(platform)));

            // Website is the one entry with nothing to check against; everything else must name
            // the hosts it accepts, or the check silently lets anything through.
            if (platform != SocialPlatform.Website)
                Assert.NotEmpty(SocialPlatforms.HostsFor(platform));
        }
    }
}
