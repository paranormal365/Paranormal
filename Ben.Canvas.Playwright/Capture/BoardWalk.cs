using Microsoft.Playwright;
using Microsoft.Playwright.NUnit;

namespace Ben.Canvas.Playwright.Capture;

/// <summary>
/// Screenshots of the desktop board: empty, with blocks and a connector, the context menu and the shortcut
/// list, in both themes at 1440 x 900.
/// </summary>
/// <remarks>Opt-in with BEN_CANVAS_WALK=1, like <see cref="ShellWalk"/>.</remarks>
[Category("Capture")]
[NonParallelizable]
public sealed class BoardWalk : PageTest
{
    private static string CanvasUrl => (Environment.GetEnvironmentVariable("BEN_CANVAS_URL") ?? "http://localhost:5125").TrimEnd('/');

    [TestCase("dark")]
    [TestCase("light")]
    public async Task Capture_the_desktop_board(string theme)
    {
        if (Environment.GetEnvironmentVariable("BEN_CANVAS_WALK") != "1")
            Assert.Ignore("Set BEN_CANVAS_WALK=1 to capture the board walk.");

        var folder = Environment.GetEnvironmentVariable("BEN_CANVAS_WALK_OUT")
                     ?? Path.Combine(TestContext.CurrentContext.WorkDirectory, "canvas-walk");
        Directory.CreateDirectory(folder);

        await using var context = await Browser.NewContextAsync(new()
        {
            ViewportSize = new() { Width = 1440, Height = 900 },
            DeviceScaleFactor = 1,
            ColorScheme = theme == "dark" ? ColorScheme.Dark : ColorScheme.Light,
        });
        await context.AddInitScriptAsync($"localStorage.setItem('ben-theme', '{theme}'); localStorage.setItem('layoutSettings', JSON.stringify({{ theme: '{theme}' }}));");
        var page = await context.NewPageAsync();
        await page.GotoAsync(CanvasUrl + "/");
        await page.Locator(".bc-board").WaitForAsync(new() { Timeout = 60_000 });
        await page.WaitForLoadStateAsync(LoadState.NetworkIdle);

        async Task Shot(string name)
        {
            await page.EvaluateAsync("async () => { await document.fonts.ready; }");
            await page.WaitForTimeoutAsync(250);
            await page.ScreenshotAsync(new() { Path = Path.Combine(folder, $"m2-desktop-{theme}-{name}.png") });
        }

        await Shot("01-empty");

        async Task<ILocator> Add(string kind)
        {
            await page.ClickAsync($".bc-rail [data-bc-action='add-{kind}']");
            var id = await page.Locator(".bc-node[data-bc-selected]").Last.GetAttributeAsync("data-bc-node");
            return page.Locator($".bc-node[data-bc-node='{id}']");
        }

        async Task Drag(ILocator node, double dx, double dy)
        {
            var b = (await node.BoundingBoxAsync())!;
            await page.Mouse.MoveAsync(b.X + b.Width / 2, b.Y + 14);
            await page.Mouse.DownAsync();
            await page.Mouse.MoveAsync((float)(b.X + b.Width / 2 + dx), (float)(b.Y + 14 + dy), new() { Steps = 10 });
            await page.Mouse.UpAsync();
            await page.WaitForTimeoutAsync(150);
        }

        var card = await Add("card");
        await Drag(card, -320, -120);
        var note = await Add("text");
        await Drag(note, 260, -140);
        var message = await Add("message");
        await Drag(message, 0, 170);

        await page.Locator(".bc-board").FocusAsync();
        await card.ClickAsync();
        await page.Keyboard.PressAsync("c");
        await page.Keyboard.PressAsync("Enter");
        await card.ClickAsync();
        await Shot("02-blocks-connector-selected");

        await message.ClickAsync(new() { Button = MouseButton.Right });
        await Shot("03-context-menu");
        await page.Keyboard.PressAsync("Escape");
        await page.Mouse.ClickAsync(700, 500);

        await page.Locator(".bc-board").FocusAsync();
        await page.Keyboard.PressAsync("?");
        await page.Locator(".modal").WaitForAsync();
        await Shot("04-shortcuts");
    }
}
