using Microsoft.Playwright;
using Microsoft.Playwright.NUnit;

namespace Ben.Canvas.Playwright.Capture;

/// <summary>
/// Screenshots of the host shell - the board and the sign-in page - at three sizes in both themes.
/// </summary>
/// <remarks>
/// Opt-in: runs only when BEN_CANVAS_WALK=1, so an ordinary test run never writes files. The phone
/// size is 390 x 844 to match the site's own help captures (Ben.Web.Playwright/Capture/
/// HostedEventPersonaWalk.cs), while the assertion fixtures use the harsher 375 x 812.
/// </remarks>
[Category("Capture")]
[NonParallelizable]
public sealed class ShellWalk : PageTest
{
    private static string CanvasUrl => (Environment.GetEnvironmentVariable("BEN_CANVAS_URL") ?? "http://localhost:5125").TrimEnd('/');

    public static IEnumerable<TestCaseData> Sizes()
    {
        yield return new TestCaseData("desktop", 1440, 900, false);
        yield return new TestCaseData("ipad", 768, 1024, true);
        yield return new TestCaseData("iphone", 390, 844, true);
    }

    [TestCaseSource(nameof(Sizes))]
    public async Task Capture_the_shell(string device, int width, int height, bool touch)
    {
        if (Environment.GetEnvironmentVariable("BEN_CANVAS_WALK") != "1")
            Assert.Ignore("Set BEN_CANVAS_WALK=1 to capture the shell walk.");

        var folder = Environment.GetEnvironmentVariable("BEN_CANVAS_WALK_OUT")
                     ?? Path.Combine(TestContext.CurrentContext.WorkDirectory, "canvas-walk");
        Directory.CreateDirectory(folder);

        foreach (var theme in new[] { "dark", "light" })
        {
            await using var context = await Browser.NewContextAsync(new()
            {
                ViewportSize = new() { Width = width, Height = height },
                DeviceScaleFactor = 2,
                HasTouch = touch,
                IsMobile = touch,
                ColorScheme = theme == "dark" ? ColorScheme.Dark : ColorScheme.Light,
            });
            // Seed the theme under the site's own keys, as a returning visitor would have it.
            await context.AddInitScriptAsync($"localStorage.setItem('ben-theme', '{theme}'); localStorage.setItem('layoutSettings', JSON.stringify({{ theme: '{theme}' }}));");

            var page = await context.NewPageAsync();
            foreach (var (route, name) in new[] { ("/", "board"), ("/login", "signin") })
            {
                await page.GotoAsync(CanvasUrl + route);
                await page.Locator(".bc-editor, .bwc-login").First.WaitForAsync(new() { Timeout = 60_000 });
                await page.WaitForLoadStateAsync(LoadState.NetworkIdle);
                await page.EvaluateAsync("async () => { await document.fonts.ready; }");
                await page.ScreenshotAsync(new() { Path = Path.Combine(folder, $"m0-{device}-{theme}-{name}.png") });
            }
        }
    }
}
