using Microsoft.Playwright;
using Microsoft.Playwright.NUnit;

namespace Ben.Canvas.Playwright.Capture;

/// <summary>
/// Screenshots of every block type filled in, a labelled connector, and a group, in both themes at 1440 x 900.
/// </summary>
/// <remarks>Opt-in with BEN_CANVAS_WALK=1, like <see cref="BoardWalk"/>.</remarks>
[Category("Capture")]
[NonParallelizable]
public sealed class BlocksWalk : PageTest
{
    private static string CanvasUrl => (Environment.GetEnvironmentVariable("BEN_CANVAS_URL") ?? "http://localhost:5125").TrimEnd('/');

    [TestCase("dark")]
    [TestCase("light")]
    public async Task Capture_the_blocks(string theme)
    {
        if (Environment.GetEnvironmentVariable("BEN_CANVAS_WALK") != "1")
            Assert.Ignore("Set BEN_CANVAS_WALK=1 to capture the blocks walk.");

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
        var board = page.Locator(".bc-board");
        await board.WaitForAsync(new() { Timeout = 60_000 });
        await page.WaitForLoadStateAsync(LoadState.NetworkIdle);

        async Task Shot(string name)
        {
            await page.EvaluateAsync("async () => { await document.fonts.ready; }");
            await page.WaitForTimeoutAsync(400);
            await page.ScreenshotAsync(new() { Path = Path.Combine(folder, $"m3-{name}-{theme}.png") });
        }

        async Task<(ILocator Node, string Id)> Add(string kind)
        {
            await page.ClickAsync($".bc-rail [data-bc-action='add-{kind}']");
            var id = (await page.Locator(".bc-node[data-bc-selected]").Last.GetAttributeAsync("data-bc-node"))!;
            return (page.Locator($".bc-node[data-bc-node='{id}']"), Guid.Parse(id).ToString("N"));
        }

        async Task Drag(ILocator target, double dx, double dy)
        {
            var b = (await target.BoundingBoxAsync())!;
            await page.Mouse.MoveAsync(b.X + b.Width / 2, b.Y + 14);
            await page.Mouse.DownAsync();
            await page.Mouse.MoveAsync((float)(b.X + b.Width / 2 + dx), (float)(b.Y + 14 + dy), new() { Steps = 10 });
            await page.Mouse.UpAsync();
            await page.WaitForTimeoutAsync(150);
        }

        async Task Edit(ILocator node)
        {
            var b = (await node.BoundingBoxAsync())!;
            await page.Mouse.DblClickAsync(b.X + b.Width / 2, b.Y + 14);
            await page.Locator(".bc-node--editing").WaitForAsync();
        }

        async Task EndEdit()
        {
            await board.FocusAsync();
            await page.Keyboard.PressAsync("Escape");
            await page.WaitForTimeoutAsync(150);
        }

        var (card, cardId) = await Add("card");
        await Drag(card, -330, -230);
        await Edit(card);
        await page.FillAsync($"#bc-card-{cardId}-title", "Cold spot");
        await page.FillAsync($"#bc-card-{cardId}-description", "By the stairs, second floor");
        await page.SelectOptionAsync($"#bc-card-{cardId}-category", "Witness");
        await page.CheckAsync($"#bc-card-{cardId}-verified");
        await EndEdit();

        var (note, _) = await Add("text");
        await Drag(note, 20, -250);
        await Edit(note);
        await page.Keyboard.TypeAsync("Recorder left running from 11pm. Audio notes at https://example.com/audio.");
        await EndEdit();

        var (message, _) = await Add("message");
        await Drag(message, 360, -210);
        await Edit(message);
        await message.Locator(".k-editor [contenteditable='true']").First.ClickAsync();
        await page.Keyboard.TypeAsync("Heard footsteps above the kitchen.");
        await page.WaitForTimeoutAsync(300);
        await page.Mouse.ClickAsync(700, 880);
        await EndEdit();

        var (map, mapId) = await Add("map");
        await Drag(map, -330, 170);
        await Edit(map);
        await page.FillAsync($"#bc-map-addr-{mapId}", "Belmont Mansion, Nashville");
        await page.FillAsync($"#bc-map-ll-{mapId}", "36.1340, -86.7962");
        await page.Keyboard.PressAsync("Tab");
        await EndEdit();

        var (image, _) = await Add("image");
        await Drag(image, 30, 190);

        var (link, linkId) = await Add("link");
        await Drag(link, 370, 200);
        await Edit(link);
        await page.FillAsync($"#bc-link-url-{linkId}", "https://example.com/reports/belmont");
        await page.Keyboard.PressAsync("Tab");
        await EndEdit();

        await page.Mouse.ClickAsync(700, 880);
        await board.FocusAsync();
        await page.Keyboard.PressAsync("Home");
        await Shot("node-types");

        await card.ClickAsync();
        await board.FocusAsync();
        await page.Keyboard.PressAsync("c");
        await page.Keyboard.PressAsync("Enter");
        if (await page.Locator("#bc-prop-edge-label").CountAsync() > 0)
        {
            await page.FillAsync("#bc-prop-edge-label", "same night");
            await page.Keyboard.PressAsync("Tab");
        }

        await map.ClickAsync();
        await board.FocusAsync();
        await page.Keyboard.PressAsync("c");
        await page.Keyboard.PressAsync("Enter");
        await Shot("connectors");

        await page.Mouse.ClickAsync(700, 880);
        await map.ClickAsync();
        await image.ClickAsync(new() { Modifiers = [KeyboardModifier.Shift] });
        await board.FocusAsync();
        await page.Keyboard.PressAsync("ControlOrMeta+g");
        await page.Locator(".bc-group").WaitForAsync();
        await page.Keyboard.PressAsync("F2");
        if (await page.Locator(".bc-group__input").CountAsync() > 0)
        {
            await page.Locator(".bc-group__input").FillAsync("Grounds");
            await page.Locator(".bc-group__input").PressAsync("Enter");
        }

        await board.FocusAsync();
        await page.Keyboard.PressAsync("Home");
        await Shot("group");
    }
}
