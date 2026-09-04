using System.Text.Json;
using Ben.Data.Common;
using Ben.Web.Website.Services;
using Xunit;

namespace Ben.Web.Tests.Website;

/// <summary>
/// The association file that decides whether a link opens the app, and the manifest that decides
/// what happens when somebody installs the site (item 209).
/// </summary>
/// <remarks>
/// <para><b>Why these are worth testing at all.</b> Neither document is ever read by anybody
/// during development. The association file is fetched by iOS, once, on a stranger's phone, and a
/// mistake in it shows up as "tapping the link does nothing useful" with no error anywhere. The
/// manifest is read by an installer. Both are exactly the shape of thing that ships wrong and
/// stays wrong.</para>
///
/// <para><b>The specific fear.</b> Claiming a path the app cannot render is worse than claiming
/// nothing: the link leaves Safari, where the real page is, and opens an app that shows a
/// placeholder. Two such paths exist today and both parse perfectly well, so nothing but a
/// deliberate list keeps them out.</para>
/// </remarks>
public sealed class AppLinksAndManifestTests
{
    private const string AppId = "5778H75249.com.ishaunted.ios";

    private static JsonDocument Association()
        => JsonDocument.Parse(JsonSerializer.Serialize(AppleAppSiteAssociation.For(AppId)));

    private static List<string> ClaimedPaths()
    {
        // Read back out of the SERIALISED document, not off the constant. The property names are
        // supplied by attributes — "appIDs", and a component key that is literally "/" — and a
        // rename or a missing attribute would produce a document iOS silently ignores while every
        // assertion against the C# object still passed.
        using var doc = Association();
        return doc.RootElement
            .GetProperty("applinks").GetProperty("details")[0].GetProperty("components")
            .EnumerateArray()
            .Select(c => c.GetProperty("/").GetString()!)
            .ToList();
    }

    [Fact]
    public void The_association_file_names_the_app_in_the_shape_iOS_reads()
    {
        using var doc = Association();
        var detail = doc.RootElement.GetProperty("applinks").GetProperty("details")[0];

        // appIDs plural is the modern spelling; appID singular is pre-iOS-13 and is not emitted.
        var ids = detail.GetProperty("appIDs").EnumerateArray().Select(v => v.GetString()).ToList();
        Assert.Equal([AppId], ids);
    }

    [Fact]
    public void Only_paths_that_reach_a_real_screen_are_claimed()
    {
        var claimed = ClaimedPaths();

        Assert.Equal(
        [
            "/feed", "/feed/*",
            "/events",
            "/my-cases", "/my-cases/*",
            "/my-investigations",
            "/notifications",
            "/profile",
            "/validate-email/*",
        ], claimed);
    }

    [Theory]
    // The two that hurt. Both parse in DeepLinkParser and both land on RootShell's default arm,
    // which renders a "Coming soon" placeholder — so claiming either takes somebody away from the
    // working website page and shows them nothing.
    [InlineData("/events/*")]
    [InlineData("/organizations/*")]
    // The RSVP token the router currently discards.
    [InlineData("/attending/*")]
    // Share links (item 207) are for people with no account, who are the least likely to have the
    // app; the shared player has no app equivalent at all.
    [InlineData("/s/*")]
    [InlineData("/o/*")]
    [InlineData("/admin/*")]
    public void A_path_the_app_cannot_render_is_not_claimed(string path)
    {
        Assert.DoesNotContain(path, ClaimedPaths());
    }

    [Fact]
    public void Every_unclaimed_path_carries_the_reason_it_is_unclaimed()
    {
        // The list is data rather than a comment precisely so it can be asserted. A future author
        // adding a path here without a reason is the one this catches — the reason is what stops
        // somebody "tidying up" the omission a year from now.
        foreach (var (path, why) in AppleAppSiteAssociation.UnclaimedPaths)
        {
            Assert.False(string.IsNullOrWhiteSpace(why), $"{path} is excluded with no reason given.");
            Assert.DoesNotContain(path, AppleAppSiteAssociation.ClaimedPaths);
        }
    }

    [Fact]
    public void The_events_list_is_claimed_but_not_a_single_event()
    {
        var claimed = ClaimedPaths();

        // Stated as its own test because it is the one place the claim is deliberately narrower
        // than the obvious wildcard, and the obvious wildcard is what somebody would "fix" it to.
        Assert.Contains("/events", claimed);
        Assert.DoesNotContain(claimed, p => p.StartsWith("/events/", StringComparison.Ordinal));
    }

    [Fact]
    public void Nothing_is_claimed_broadly_and_then_carved_back()
    {
        using var doc = Association();
        var raw = doc.RootElement.GetRawText();

        // Apple's component matching has ordering rules that are easy to get subtly wrong, and
        // wrong here fails silently on somebody else's phone. The document is built so it needs no
        // exclusions at all, which removes the ordering question entirely.
        Assert.DoesNotContain("exclude", raw, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void No_claimed_path_swallows_the_whole_site()
    {
        foreach (var path in ClaimedPaths())
        {
            // A single "/*" — or a bare "*" — claims every page including the share links, the
            // public group pages and the admin screens. It is one keystroke away from several of
            // the entries above.
            Assert.NotEqual("/*", path);
            Assert.NotEqual("*", path);
            Assert.StartsWith("/", path);
        }
    }

    // ── the manifest ──────────────────────────────────────────────────────────

    private static JsonDocument Manifest(string name = "IsHaunted.com")
        => JsonDocument.Parse(JsonSerializer.Serialize(
            WebAppManifest.For(new SiteIdentity { Name = name, Tagline = "A tagline." })));

    [Fact]
    public void The_manifest_takes_its_name_from_configuration()
    {
        using var doc = Manifest("Spectre.example");

        // SiteIdentity exists because the domain is not settled. A manifest is read by an
        // installer rather than by a page, so a name baked in here goes stale unseen.
        Assert.Equal("Spectre.example", doc.RootElement.GetProperty("name").GetString());
        Assert.Equal("Spectre", doc.RootElement.GetProperty("short_name").GetString());
    }

    [Theory]
    [InlineData("IsHaunted.com", "IsHaunted")]
    // A modern top-level domain. The first version of this capped the suffix at four characters
    // and would have kept ".paranormal" while stripping ".com" — the test that caught it.
    [InlineData("Spectre.paranormal", "Spectre")]
    [InlineData("Ghost Watch", "Ghost Watch")]                       // no dot at all
    [InlineData("Paranormal Investigations Ltd.", "Paranormal Investigations Ltd.")]  // spaces: a name
    [InlineData("IsHaunted.", "IsHaunted.")]                          // nothing after the dot
    [InlineData("Release.2", "Release.2")]                            // a version, not a domain
    [InlineData("A.B", "A")]
    public void The_short_name_drops_a_domain_suffix_and_nothing_else(string name, string expected)
    {
        // Trimming by LENGTH would cut a real name mid-word, which is worse than a name that is a
        // little long: a launcher truncates with an ellipsis, and this would not.
        Assert.Equal(expected, WebAppManifest.ShortNameFor(name));
    }

    [Fact]
    public void The_manifest_carries_both_icon_sizes_an_installer_needs()
    {
        using var doc = Manifest();
        var icons = doc.RootElement.GetProperty("icons").EnumerateArray().ToList();

        var sizes = icons.Select(i => i.GetProperty("sizes").GetString()).ToList();
        Assert.Contains("192x192", sizes);
        Assert.Contains("512x512", sizes);

        // Android crops a non-maskable icon into its own shape and can clip the artwork.
        Assert.Contains(icons, i => i.GetProperty("purpose").GetString()!.Contains("maskable"));
    }

    [Fact]
    public void The_manifest_starts_at_the_site_root_and_is_scoped_to_it()
    {
        using var doc = Manifest();

        // Somebody who installed the site should land where a visitor lands; the home page itself
        // decides whether that is a member's desk or the front door.
        Assert.Equal("/", doc.RootElement.GetProperty("start_url").GetString());
        Assert.Equal("/", doc.RootElement.GetProperty("scope").GetString());
        Assert.Equal("standalone", doc.RootElement.GetProperty("display").GetString());
    }

    [Fact]
    public void The_manifest_background_matches_the_theme_the_site_actually_renders()
    {
        var css = File.ReadAllText(RepoFile("Ben.Web.Website/wwwroot/css/themes/night.min.css"));

        // A mismatch is not a style nit: the installed window paints this colour before the page
        // does, so a wrong value is a flash of the wrong shade on every single launch.
        Assert.Contains($"--bs-body-bg:{WebAppManifest.BackgroundColor}", css, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(WebAppManifest.BackgroundColor, WebAppManifest.ThemeColor);
    }

    [Fact]
    public void The_page_head_links_the_manifest_and_does_not_hard_code_the_colour()
    {
        var head = File.ReadAllText(RepoFile("Ben.Web.Website/Components/App.razor"));

        Assert.Contains("rel=\"manifest\" href=\"/manifest.webmanifest\"", head);
        // The colour comes from the same constant the manifest uses. Two copies drift, and this
        // drift shows as the browser chrome disagreeing with the page.
        Assert.Contains("WebAppManifest.ThemeColor", head);
    }

    private static string RepoFile(string relative)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Ben.slnx"))) dir = dir.Parent;
        return Path.Combine(dir!.FullName, relative);
    }
}
