using System.Text.Json;
using NUnit.Framework;

namespace Ben.Web.Playwright.Tests;

/// <summary>
/// The two documents that make a link open the app and the site installable (item 209).
/// </summary>
/// <remarks>
/// <para>The unit tests pin what these documents SAY. What only a running host can show is that
/// they are reachable at all, with the right content type, through the real middleware pipeline —
/// and the association file is served from an endpoint sitting among a dozen others, one
/// reordering away from being shadowed by a route or swallowed by the SPA fallback.</para>
///
/// <para>iOS fetches the association file exactly once, silently, on somebody else's phone. There
/// is no error to see when it 404s.</para>
/// </remarks>
[TestFixture]
[Category("AppLinks")]
public class AppLinksAndManifestTests : BenTestBase
{
    [Test]
    public async Task The_association_file_is_served_as_json_at_the_well_known_path()
    {
        var api = await Playwright.APIRequest.NewContextAsync(new() { BaseURL = BaseUrl });
        var response = await api.GetAsync("/.well-known/apple-app-site-association");

        Assert.That(response.Status, Is.EqualTo(200), "iOS gets one shot at this file");

        // iOS refuses anything that is not application/json. A file with no extension served by
        // static middleware would arrive as application/octet-stream and be ignored in silence.
        var contentType = response.Headers.TryGetValue("content-type", out var ct) ? ct : "";
        Assert.That(contentType, Does.Contain("application/json"));

        using var doc = JsonDocument.Parse(await response.TextAsync());
        var detail = doc.RootElement.GetProperty("applinks").GetProperty("details")[0];

        var appId = detail.GetProperty("appIDs")[0].GetString();
        Assert.That(appId, Does.Match(@"^[A-Z0-9]{10}\..+"),
            "an appID is TEAMID.bundle.id — a bare bundle id claims nothing");

        var paths = detail.GetProperty("components").EnumerateArray()
            .Select(c => c.GetProperty("/").GetString()).ToList();

        Assert.That(paths, Is.Not.Empty);
        Assert.That(paths, Does.Not.Contain("/*"), "that would claim every page on the site");
        // The two that parse and then land on a placeholder screen.
        Assert.That(paths, Does.Not.Contain("/events/*"));
        Assert.That(paths, Does.Not.Contain("/organizations/*"));
    }

    [Test]
    public async Task The_association_file_is_not_behind_a_redirect_or_a_sign_in()
    {
        var api = await Playwright.APIRequest.NewContextAsync(new() { BaseURL = BaseUrl });
        var response = await api.GetAsync("/.well-known/apple-app-site-association",
            new() { MaxRedirects = 0 });

        // iOS follows no redirects for this file and sends no credentials. A 301 to a canonical
        // host, or an auth challenge from a misplaced middleware, both read as "no app links" with
        // nothing logged anywhere.
        Assert.That(response.Status, Is.EqualTo(200),
            $"expected a direct 200, got {response.Status} — a redirect or a gate would make every "
          + "universal link silently stop working");
    }

    [Test]
    public async Task The_manifest_is_served_and_its_icons_exist()
    {
        var api = await Playwright.APIRequest.NewContextAsync(new() { BaseURL = BaseUrl });
        var response = await api.GetAsync("/manifest.webmanifest");

        Assert.That(response.Status, Is.EqualTo(200));
        var contentType = response.Headers.TryGetValue("content-type", out var ct) ? ct : "";
        Assert.That(contentType, Does.Contain("application/manifest+json"));

        using var doc = JsonDocument.Parse(await response.TextAsync());
        var root = doc.RootElement;

        Assert.That(root.GetProperty("display").GetString(), Is.EqualTo("standalone"));
        Assert.That(root.GetProperty("start_url").GetString(), Is.EqualTo("/"));

        // An icon listed but not served is the commonest manifest fault, and an installer's only
        // symptom is a blank home-screen tile.
        foreach (var icon in root.GetProperty("icons").EnumerateArray())
        {
            var src = icon.GetProperty("src").GetString()!;
            var iconResponse = await api.GetAsync(src);
            Assert.That(iconResponse.Status, Is.EqualTo(200), $"the manifest lists {src}, which 404s");
        }
    }

    [Test]
    public async Task The_page_links_the_manifest_so_a_browser_can_find_it()
    {
        await Page.GotoAsync(BaseUrl);
        await Page.WaitForSelectorAsync("body");

        // A manifest nothing links to is a file nobody fetches. This is the one part of the
        // feature that lives in the page rather than in a document.
        var href = await Page.Locator("link[rel='manifest']").First.GetAttributeAsync("href");
        Assert.That(href, Is.EqualTo("/manifest.webmanifest"));

        var themeColor = await Page.Locator("meta[name='theme-color']").First.GetAttributeAsync("content");
        Assert.That(themeColor, Does.StartWith("#"));
    }
}
