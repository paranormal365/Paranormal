using Ben.Data.Common;
using Xunit;

namespace Ben.Web.Tests.Website;

/// <summary>
/// The App Store address lives in SiteIdentity; the help article that has to spell it out must
/// spell out the same one.
/// </summary>
/// <remarks>
/// Ben, 2026-10-01: "The appstore should have a link on the site." The footer, Home and Safari's
/// smart app banner all read <see cref="SiteIdentity.AppStoreUrl"/>. The help article cannot — help
/// is markdown with no substitution, and the product PDF copies it verbatim — so it carries the
/// address as text, and this test is what keeps the two from drifting. Same pattern as the editor
/// hosts' copies of the skin (SignalCopiesStayInStepTests).
/// </remarks>
public class AppStoreLinkTests
{
    [Fact]
    public void The_address_carries_the_numeric_id_the_smart_banner_needs()
    {
        var site = new SiteIdentity();
        Assert.StartsWith("https://apps.apple.com/", site.AppStoreUrl);
        Assert.Equal("6806786633", site.AppStoreAppId);
    }

    [Fact]
    public void An_address_without_an_id_gives_no_banner_rather_than_a_broken_one()
    {
        Assert.Null(new SiteIdentity { AppStoreUrl = "https://example.com/somewhere" }.AppStoreAppId);
        Assert.Null(new SiteIdentity { AppStoreUrl = "" }.AppStoreAppId);
    }

    [Fact]
    public void The_help_article_links_to_the_same_app()
    {
        var help = File.ReadAllText(Path.Combine(RepoRoot(), "Ben.Web.Services", "Help", "Content", "the-mobile-apps.md"));
        Assert.Contains(new SiteIdentity().AppStoreUrl, help);
    }

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Ben.slnx"))) dir = dir.Parent;
        Assert.NotNull(dir);
        return dir!.FullName;
    }
}
