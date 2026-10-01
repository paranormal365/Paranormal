using Microsoft.Playwright;
using NUnit.Framework;

namespace Ben.Web.Playwright.Tests;

/// <summary>
/// The seven advertisements offered from the home page (Ben, 2026-10-01).
/// </summary>
/// <remarks>
/// <para><b>What can actually go wrong here is the files, not the markup.</b> The strip is static:
/// seven anchors and seven images, no client, no loading state, nothing to fail at run time. The
/// failure this guards against is a flyer being rebuilt in <c>docs/ads</c> and never copied across
/// to <c>wwwroot/flyers</c> — which renders as a broken image over a dead link, looks like nothing
/// in the markup, and is exactly the mistake <c>docs/README.md</c> already carries a warning about
/// for the iPhone guide.</para>
///
/// <para>So the second test does not inspect the page at all. It takes every href the page offers
/// and fetches it, and does the same for every thumbnail, and insists on a PDF and an image coming
/// back. A 404 on either is a failure; so is a 200 that answers with the site's own HTML, which is
/// what a misrouted static path looks like.</para>
///
/// <para>Anonymous on purpose. These are for sending to somebody who has never seen the site.</para>
/// </remarks>
[TestFixture]
[Category("Home")]
public class FlyerStripTests : BenTestBase
{
    private static readonly string[] ExpectedAudiences =
    [
        "Everyone", "Just curious", "Enthusiasts", "Investigation groups",
        "Ghost walk tours", "Haunted venues", "Event hosts",
    ];

    [SetUp]
    public async Task GoToTheHomePageAsync()
    {
        await Page.GotoAsync(BaseUrl);

        // ScrollIntoViewIfNeededAsync on a bare locator fails here with "Element is not attached
        // to the DOM", and the strip is not at fault: the page is prerendered, the circuit then
        // connects, and the re-render replaces the node between resolving it and scrolling to it.
        // Expect() re-resolves on every attempt, so it waits out the swap; the scroll is done
        // through the window rather than through a node, which has nothing to detach.
        await Expect(Page.Locator("#flyer-strip")).ToBeVisibleAsync(new() { Timeout = 20_000 });
        await Page.EvaluateAsync("window.scrollTo(0, document.body.scrollHeight)");

        // The images are lazy, so they are only asked for once the strip is actually on screen.
        await Expect(Page.Locator("#flyer-strip a.flyer").First).ToBeVisibleAsync(new() { Timeout = 20_000 });
    }

    [Test]
    [Description("All seven flyers are offered, each saying who it is for, with 'Everyone' first.")]
    public async Task The_home_page_offers_all_seven_flyers()
    {
        var links = Page.Locator("#flyer-strip a.flyer");
        await Expect(links).ToHaveCountAsync(7);

        var audiences = await links.Locator(".who").AllInnerTextsAsync();
        Assert.That(audiences.Select(a => a.Trim()), Is.EqualTo(ExpectedAudiences).AsCollection,
            "The strip should name each flyer's audience, with the whole-platform one first.");

        // Ben asked for 64-pixel thumbnails; the box is what is measured, not the file behind it,
        // which is deliberately 128 wide so a retina screen has pixels to draw with.
        var box = await links.First.Locator("img").BoundingBoxAsync();
        Assert.That(box, Is.Not.Null, "The first thumbnail should be laid out.");
        Assert.That(box!.Width, Is.EqualTo(64).Within(1), "Thumbnails are 64 pixels wide.");
    }

    [Test]
    [Description("Every flyer and every thumbnail the page offers actually downloads.")]
    public async Task Every_flyer_and_its_thumbnail_actually_downloads()
    {
        var hrefs = await Page.Locator("#flyer-strip a.flyer").EvaluateAllAsync<string[]>(
            "els => els.map(e => e.getAttribute('href'))");
        var thumbs = await Page.Locator("#flyer-strip a.flyer img").EvaluateAllAsync<string[]>(
            "els => els.map(e => e.getAttribute('src'))");

        Assert.That(hrefs, Has.Length.EqualTo(7));
        Assert.That(thumbs, Has.Length.EqualTo(7));

        var api = await Playwright.APIRequest.NewContextAsync(new() { BaseURL = BaseUrl });
        var broken = new List<string>();

        foreach (var (path, wanted) in hrefs.Select(h => (h, "pdf")).Concat(thumbs.Select(t => (t, "image"))))
        {
            var response = await api.GetAsync(path);
            var type = response.Headers.TryGetValue("content-type", out var ct) ? ct : "(none)";

            if (!response.Ok)
                broken.Add($"{path} answered {response.Status}");
            else if (wanted == "pdf" && !type.Contains("pdf", StringComparison.OrdinalIgnoreCase))
                broken.Add($"{path} answered 200 with '{type}' rather than a PDF");
            else if (wanted == "image" && !type.StartsWith("image/", StringComparison.OrdinalIgnoreCase))
                broken.Add($"{path} answered 200 with '{type}' rather than an image");
        }

        Assert.That(broken, Is.Empty,
            "A flyer rebuilt in docs/ads and not copied to Ben.Web.Website/wwwroot/flyers looks "
            + "exactly like this. Run: python3 docs/ads/publish-to-site.py\n  "
            + string.Join("\n  ", broken));
    }
}
