using Ben.Canvas.Core.Model;
using Microsoft.Playwright;

namespace Ben.Canvas.Playwright.Capture;

/// <summary>
/// The same small board at desktop, iPad and iPhone sizes: the rail and panel, the panel laid over the board in
/// iPad portrait, and on the iPhone the bottom bar, the Add sheet and the properties sheet.
/// </summary>
/// <remarks>Opt-in with BEN_CANVAS_WALK=1, like <see cref="BoardWalk"/>.</remarks>
[Category("Capture")]
[NonParallelizable]
[TestFixture(DeviceKind.Desktop)]
[TestFixture(DeviceKind.Tablet)]
[TestFixture(DeviceKind.Phone)]
public sealed class DevicesWalk(DeviceKind device) : CanvasTestBase(device)
{
    private static CanvasDocument SmallBoard() => new()
    {
        Title = "Porch investigation",
        NextZ = 3,
        Nodes =
        [
            new CanvasNode { Type = CanvasNodeType.Card, X = 0, Y = 0, Width = 280, Height = 200, Z = 0, Data = new CardData { Title = "Cold spot by the stairs" } },
            new CanvasNode { Type = CanvasNodeType.Text, X = 340, Y = 20, Width = 220, Height = 120, Z = 1, Data = new TextData { Text = "Recorder left on the landing. Battery died at 2:40am." } },
            new CanvasNode { Type = CanvasNodeType.Link, X = 80, Y = 280, Width = 320, Height = 140, Z = 2, Data = new LinkData { Url = "https://www.youtube.com/watch?v=abc" } },
        ],
    };

    [Test]
    public async Task Capture_the_board_at_this_size()
    {
        if (Environment.GetEnvironmentVariable("BEN_CANVAS_WALK") != "1")
            Assert.Ignore("Set BEN_CANVAS_WALK=1 to capture the device walk.");

        var name = Device.ToString().ToLowerInvariant();
        await ImportBoardAsync(SmallBoard());
        await Page.ClickAsync("[data-bc-action='fit']");
        await Page.WaitForTimeoutAsync(400);

        var first = Nodes.First;
        var box = (await first.BoundingBoxAsync())!;
        if (Device == DeviceKind.Desktop) await Page.Mouse.ClickAsync(box.X + box.Width / 2, box.Y + 14);
        else await Page.Touchscreen.TapAsync(box.X + box.Width / 2, box.Y + 14);
        await Expect(Page.Locator(".bc-node[data-bc-selected]")).ToHaveCountAsync(1);

        if (Device == DeviceKind.Tablet && !await Page.Locator("#bc-props").IsVisibleAsync())
            await Page.ClickAsync("[data-bc-action='properties']");
        await Page.WaitForTimeoutAsync(300);
        await ShotAsync($"m5-{name}-board");

        if (Device == DeviceKind.Tablet)
        {
            await Page.SetViewportSizeAsync(1024, 768);
            await Page.ClickAsync("[data-bc-action='fit']");
            await Page.WaitForTimeoutAsync(400);
            await ShotAsync("m5-tablet-landscape-board");
        }

        if (Device != DeviceKind.Phone) return;

        await Page.ClickAsync(".bc-bar [data-bc-action='add-menu']");
        await Expect(Page.Locator(".bc-sheet--open")).ToBeVisibleAsync();
        await Page.WaitForTimeoutAsync(400);
        await ShotAsync("m5-phone-add-sheet");
        await Page.ClickAsync(".bc-sheet--open [data-bc-action='sheet-close']");
        await Expect(Page.Locator(".bc-sheet--open")).ToHaveCountAsync(0);

        await Page.ClickAsync(".bc-bar [data-bc-action='more']");
        await Page.ClickAsync(".bc-sheet--open [data-bc-action='edit']");
        await Expect(Page.Locator(".bc-sheet--open[aria-label='Properties']")).ToBeVisibleAsync();
        await Page.WaitForTimeoutAsync(400);
        await ShotAsync("m5-phone-properties-sheet");
    }
}
