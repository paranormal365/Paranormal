using NUnit.Framework;

namespace Ben.Web.Playwright.Capture;

/// <summary>
/// Opens named screens as a group's owner and writes what each one shows after it has had time to settle.
/// </summary>
/// <remarks>
/// For checking a screenshot that looked blank or stuck: was the page empty, or caught mid-load? Opt-in with
/// <c>BEN_PROBE=1</c>; the addresses come from <c>BEN_PROBE_URLS</c> (space-separated paths), and the
/// pictures and text go to <c>BEN_PROBE_OUT</c> (site audit, 10/09/2026).
/// </remarks>
[TestFixture]
[Category("Capture")]
[NonParallelizable]
public sealed class PageProbe : BenTestBase
{
    [Test]
    public async Task Open_each_screen_and_say_what_it_shows()
    {
        if (Environment.GetEnvironmentVariable("BEN_PROBE") != "1")
            Assert.Ignore("Set BEN_PROBE=1 and BEN_PROBE_URLS to probe screens.");

        var outDir = Environment.GetEnvironmentVariable("BEN_PROBE_OUT") ?? "page-probe";
        Directory.CreateDirectory(outDir);
        var urls = (Environment.GetEnvironmentVariable("BEN_PROBE_URLS") ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var orgId = await OrgIdBySlugAsync("paranormal365");

        await LoginAsync(UserEmail, UserPassword);
        var report = new System.Text.StringBuilder();
        var n = 0;
        foreach (var raw in urls)
        {
            var url = raw.Replace("{org}", orgId);
            await Page.GotoAsync($"{BaseUrl}{url}");
            await WaitForTheCircuitAsync();
            await Page.WaitForTimeoutAsync(6_000);
            var text = await Page.Locator(".app-content, main").First.InnerTextAsync();
            await Page.ScreenshotAsync(new() { Path = Path.Combine(outDir, $"{++n:00}.png") });
            report.Append($"## {n:00} {url}\n{(text.Length > 1200 ? text[..1200] : text)}\n\n");
        }
        await File.WriteAllTextAsync(Path.Combine(outDir, "probe.md"), report.ToString());
    }
}
