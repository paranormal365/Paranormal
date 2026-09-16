using Microsoft.Playwright;
using NUnit.Framework;

namespace Ben.Web.Playwright.Tests;

/// <summary>
/// A research page works on a phone, a tablet and a laptop: nothing scrolls sideways, the controls that matter are on the
/// screen and big enough to touch, and Files and links can always be reached.
/// </summary>
/// <remarks>
/// Beta feedback plan, decision 10: 375px phone to large screen; the rail folds behind a button under 992px. The page is
/// made at desktop width — the helpers walk the group list, whose phone layout they do not know — then loaded again at the
/// width under test, which is what a person arriving on that device sees.
/// </remarks>
[TestFixture]
[Category("CaseResearch")]
public class CaseResearchPageViewportTests : ResearchPageTestBase
{
    [TestCase(375, 812)]
    [TestCase(768, 1024)]
    [TestCase(1280, 800)]
    public async Task ThePage_FitsTheScreen(int width, int height)
    {
        await Page.SetViewportSizeAsync(1280, 800);
        await OpenResearchTabAsync();
        var url = await CreatePageAsync(UniqueTitle($"Fits {width}"));
        await AddTextAsync("Words long enough to need wrapping on a narrow screen, so that a block that refused to wrap would show it.");
        await SaveNowAsync();

        await Page.SetViewportSizeAsync(width, height);
        await ReloadPageAsync(url);
        await Expect(BlockPage).ToBeVisibleAsync();

        // Open the text block, so its editor and toolbar are measured too.
        await Page.Locator("[data-testid=text-block]").First.ClickAsync();
        await Expect(Page.Locator("[data-testid=text-block-editor] .k-toolbar")).ToBeVisibleAsync(new() { Timeout = 10_000 });

        var overflow = await Page.EvaluateAsync<int>("() => document.documentElement.scrollWidth - document.documentElement.clientWidth");
        Assert.That(overflow, Is.LessThanOrEqualTo(0), $"the page scrolls sideways by {overflow}px at {width}px");

        foreach (var control in new[] { "#research-page-save", "#research-page-publish", "[data-testid=block-add-text]" })
        {
            var box = await Page.Locator(control).BoundingBoxAsync();
            Assert.That(box, Is.Not.Null, $"{control} is not on the page at {width}px");
            Assert.That(box!.X, Is.GreaterThanOrEqualTo(0), $"{control} starts off the left of the screen at {width}px");
            Assert.That(box.X + box.Width, Is.LessThanOrEqualTo(width + 0.5), $"{control} runs off the right of the screen at {width}px");
        }

        var toolbar = (await Page.Locator("[data-testid=text-block-editor] .k-toolbar").BoundingBoxAsync())!;
        Assert.That(toolbar.X + toolbar.Width, Is.LessThanOrEqualTo(width + 0.5), $"the text toolbar runs off the screen at {width}px");

        var handle = (await Page.Locator("[data-block-handle]").First.BoundingBoxAsync())!;
        Assert.That(Math.Min(handle.Width, handle.Height), Is.GreaterThanOrEqualTo(44), $"the drag handle is {handle.Width:0}×{handle.Height:0}px, under 44px, at {width}px");

        // Files and links: beside the page from 992px, behind a button below it.
        var linkBox = Page.Locator("#research-rail-link");
        if (width < 992)
        {
            await Expect(linkBox).Not.ToBeVisibleAsync();
            await Page.Locator("[data-testid=rail-toggle]").ClickAsync();
        }
        else
        {
            await Expect(Page.Locator("[data-testid=rail-toggle]")).Not.ToBeVisibleAsync();
        }
        await Expect(linkBox).ToBeVisibleAsync(new() { Timeout = 5_000 });
    }

    [TestCase(375, 812)]
    [TestCase(1280, 800)]
    public async Task AMapBlock_FitsTheScreen_AndItsButtonsAreBigEnoughToTouch(int width, int height)
    {
        await Page.SetViewportSizeAsync(1280, 800);
        await OpenResearchTabAsync();
        await CreatePageAsync(UniqueTitle($"Map fits {width}"));

        // The width under test is set before the block is added, so the new block is the open one — clicking a saved map
        // to open it would land on the map and add a place.
        await Page.SetViewportSizeAsync(width, height);
        await ClickUntilAsync(Page.Locator("[data-testid=block-add-map]"), Page.Locator("[data-testid=map-block]"));
        var block = Page.Locator("[data-testid=map-block]").First;
        var find = block.Locator("[data-testid=map-block-add-place]");
        await Expect(find).ToBeVisibleAsync(new() { Timeout = 10_000 });

        var overflow = await Page.EvaluateAsync<int>("() => document.documentElement.scrollWidth - document.documentElement.clientWidth");
        Assert.That(overflow, Is.LessThanOrEqualTo(0), $"the page scrolls sideways by {overflow}px at {width}px with a map on it");

        var map = (await block.BoundingBoxAsync())!;
        Assert.That(map.X + map.Width, Is.LessThanOrEqualTo(width + 0.5), $"the map block runs off the screen at {width}px");

        var mapHeight = await block.Locator(".ben-map").EvaluateAsync<double>("el => el.getBoundingClientRect().height");
        Assert.That(mapHeight, Is.EqualTo(width >= 992 ? 360 : 260).Within(2), $"the map is {mapHeight:0}px tall at {width}px");

        var findBox = (await find.BoundingBoxAsync())!;
        Assert.That(findBox.Height, Is.GreaterThanOrEqualTo(44), $"Find is {findBox.Height:0}px tall at {width}px");
    }
}
