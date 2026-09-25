using Ben.Canvas.Playwright.Support;
using Microsoft.Playwright;
using Microsoft.Playwright.NUnit;

namespace Ben.Canvas.Playwright.Capture;

/// <summary>
/// A board made only by pasting and dropping - a link, a photo, formatted text, a note, coordinates and a PDF -
/// shown after a reload, in both themes at 1440 x 900.
/// </summary>
/// <remarks>Opt-in with BEN_CANVAS_WALK=1, like <see cref="BoardWalk"/>.</remarks>
[Category("Capture")]
[NonParallelizable]
public sealed class PasteWalk : PageTest
{
    private static string CanvasUrl => (Environment.GetEnvironmentVariable("BEN_CANVAS_URL") ?? "http://localhost:5125").TrimEnd('/');

    [TestCase("dark")]
    [TestCase("light")]
    public async Task Capture_a_pasted_board_after_a_reload(string theme)
    {
        if (Environment.GetEnvironmentVariable("BEN_CANVAS_WALK") != "1")
            Assert.Ignore("Set BEN_CANVAS_WALK=1 to capture the paste walk.");

        var folder = Environment.GetEnvironmentVariable("BEN_CANVAS_WALK_OUT")
                     ?? Path.Combine(TestContext.CurrentContext.WorkDirectory, "canvas-walk");
        Directory.CreateDirectory(folder);

        await using var context = await Browser.NewContextAsync(new()
        {
            ViewportSize = new() { Width = 1440, Height = 900 },
            ColorScheme = theme == "dark" ? ColorScheme.Dark : ColorScheme.Light,
        });
        await context.AddInitScriptAsync($"localStorage.setItem('ben-theme', '{theme}'); localStorage.setItem('layoutSettings', JSON.stringify({{ theme: '{theme}' }}));");
        var page = await context.NewPageAsync();
        await page.GotoAsync(CanvasUrl + "/");
        var ready = page.Locator(".bc-editor[data-bc-ready='true']");
        await ready.WaitForAsync(new() { Timeout = 60_000 });

        var board = (await page.Locator(".bc-board").BoundingBoxAsync())!;
        var nodes = page.Locator(".bc-node");

        async Task DropAt(double fx, double fy, IEnumerable<Flavour>? flavours = null, IEnumerable<MadeFile>? files = null)
        {
            var before = await nodes.CountAsync();
            await page.DropAsync(board.X + board.Width * fx, board.Y + board.Height * fy, flavours, files);
            await page.WaitForFunctionAsync($"() => document.querySelectorAll('.bc-node').length > {before}");
        }

        await DropAt(0.2, 0.25, [new Flavour("text/plain", "https://www.youtube.com/watch?v=abc")]);
        await DropAt(0.52, 0.28, files: [new MadeFile("jpeg-with-exif", "IMG_0001.jpg", "image/jpeg", Width: 480, Height: 320)]);
        await DropAt(0.83, 0.25,
        [
            new Flavour("text/plain", "Witness statement: footsteps on the stairs at 3am, then the door closed."),
            new Flavour("text/html", "<p><b>Witness statement:</b> footsteps on the stairs at <i>3am</i>, then the door closed.</p><script>alert(1)</script>"),
        ]);
        await DropAt(0.2, 0.7, [new Flavour("text/plain", "Recorder left on the landing.\n\nBattery died at 2:40am.")]);
        await DropAt(0.5, 0.72, [new Flavour("text/plain", "36.1340, -86.7962")]);
        await DropAt(0.8, 0.72, files: [new MadeFile("bytes", "floor-plan.pdf", "application/pdf", "%PDF-1.4\n%%EOF")]);

        await page.Locator(".bc-savestate", new() { HasTextString = "Saved on this device" }).WaitForAsync(new() { Timeout = 10_000 });
        await page.ReloadAsync();
        await ready.WaitForAsync(new() { Timeout = 60_000 });
        await page.WaitForFunctionAsync("() => { const i = document.querySelector('.bc-node--image img'); return i && i.complete && i.naturalWidth > 0 }");
        await page.Mouse.ClickAsync((float)(board.X + board.Width * 0.5), (float)(board.Y + board.Height * 0.95));
        await page.EvaluateAsync("async () => { await document.fonts.ready; }");
        await page.WaitForTimeoutAsync(400);
        await page.ScreenshotAsync(new() { Path = Path.Combine(folder, $"m4-pasted-board-after-reload-{theme}.png") });
    }
}
