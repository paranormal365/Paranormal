using Microsoft.Playwright;
using NUnit.Framework;

namespace Ben.Web.Playwright.Tests;

/// <summary>
/// The chart wrapper renders, in both themes, and survives more than one chart on a page.
/// </summary>
/// <remarks>
/// <para>The multi-instance assertion is the one worth having. Every chart is keyed by its own
/// container id in a module-level Map for exactly this reason — the sibling map module carries a
/// comment about what happens when a component reaches for <c>document.querySelector(...)</c>
/// instead: it finds whichever element is first in the DOM, and the second component silently
/// drives the first one's chart. That bug looks like nothing at all until a dashboard puts four
/// charts on one screen, which is precisely what the next phase does.</para>
///
/// <para>The dark-theme assertion exists because the template ships ApexCharts' light styling and
/// none of its dark styling — measured, zero rules in night.min.css — so a chart is the element
/// most likely to stay light on a dark page.</para>
/// </remarks>
[TestFixture]
[Category("Charts")]
public class ChartTests : BenTestBase
{
    private const string ChartPage = "/admin/sidecar-telemetry";

    /// <summary>ApexCharts draws into an SVG under a .apexcharts-canvas div.</summary>
    private ILocator Charts => Page.Locator(".apexcharts-canvas");

    [SetUp]
    public async Task SignInAndOpen()
    {
        await LoginAsync(SuperAdminEmail, SuperAdminPassword);
        await Page.GotoAsync($"{BaseUrl}{ChartPage}");
    }

    [Test]
    public async Task AChartRendersOnThePage()
    {
        await Expect(Charts.First).ToBeVisibleAsync(new() { Timeout = 20_000 });

        // The SVG has real geometry — a canvas div that never got a chart is still "visible".
        var box = await Charts.First.BoundingBoxAsync();
        Assert.That(box, Is.Not.Null);
        Assert.That(box!.Width, Is.GreaterThan(50), "The chart has no width; it did not lay out.");
        Assert.That(box.Height, Is.GreaterThan(50), "The chart has no height; it did not lay out.");
    }

    [Test]
    public async Task EveryChartOnThePageGetsItsOwnCanvas()
    {
        await Expect(Charts.First).ToBeVisibleAsync(new() { Timeout = 20_000 });

        // Each ApexChart component owns a uniquely-id'd container. Two components sharing one id,
        // or a module that ignores the id and takes the first match, both show up here as fewer
        // canvases than containers.
        var containers = await Page.Locator("[id^='apex-']").CountAsync();
        var canvases = await Charts.CountAsync();

        Assert.That(containers, Is.GreaterThan(0), "No chart containers rendered at all.");
        Assert.That(canvases, Is.EqualTo(containers),
            $"{containers} chart container(s) produced {canvases} chart(s). A container without a "
            + "chart means two components resolved to the same element — the failure the "
            + "per-instance id exists to prevent.");
    }

    [Test]
    public async Task TheChartFollowsTheDarkTheme()
    {
        await Expect(Charts.First).ToBeVisibleAsync(new() { Timeout = 20_000 });

        // The site stores its theme choice; set it and reload rather than hunting the toggle,
        // which lives in the header and is not what this test is about.
        await Page.EvaluateAsync(@"() => {
            localStorage.setItem('ben-theme', 'dark');
            localStorage.setItem('layoutSettings', JSON.stringify({ theme: 'dark', htmlRoot: 'set-nav-dark' }));
        }");
        await Page.ReloadAsync();
        await Expect(Charts.First).ToBeVisibleAsync(new() { Timeout = 20_000 });

        var theme = await Page.EvaluateAsync<string?>(
            "() => document.documentElement.getAttribute('data-bs-theme')");
        Assert.That(theme, Is.EqualTo("dark"), "The page did not switch to the dark theme.");

        // The chart must not paint its own light ground inside a dark card. ben-charts.css forces
        // the canvas transparent; without it this is an opaque near-white.
        var background = await Page.EvaluateAsync<string>(
            "() => getComputedStyle(document.querySelector('.apexcharts-canvas')).backgroundColor");
        Assert.That(background is "rgba(0, 0, 0, 0)" or "transparent", Is.True,
            $"The chart canvas paints its own background ({background}) — on a dark page that is a "
            + "pale rectangle inside the card.");
    }
}
