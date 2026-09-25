using System.Text.RegularExpressions;
using Ben.Canvas.Core.Model;
using Ben.Canvas.Playwright.Touch;
using Microsoft.Playwright;

namespace Ben.Canvas.Playwright.Tests;

/// <summary>
/// The board by touch on an iPad and an iPhone size: pinch, one-finger pan and drag, a wobble that is still a
/// tap, a second finger that turns a drag into a pinch, long-press, double-tap, the phone sheet's grip, the
/// paste box a refused clipboard opens, and the photo picker. Driven through real CDP touch events.
/// </summary>
[Category("Touch")]
[TestFixture(DeviceKind.Tablet)]
[TestFixture(DeviceKind.Phone)]
public sealed class CanvasTouchTests(DeviceKind device) : CanvasTestBase(device)
{
    private TouchDriver _touch = null!;

    [SetUp]
    public async Task StartTouchAsync() => _touch = await TouchDriver.StartAsync(Page, Browser);

    private void PhoneOnly()
    {
        if (Device != DeviceKind.Phone) Assert.Ignore("A phone check.");
    }

    private static CanvasDocument TwoCards() => new()
    {
        Title = "Touch",
        NextZ = 2,
        Nodes =
        [
            new CanvasNode { Type = CanvasNodeType.Card, X = 40, Y = 60, Width = 220, Height = 140, Z = 0, Data = new CardData { Title = "Porch" } },
            new CanvasNode { Type = CanvasNodeType.Card, X = 40, Y = 320, Width = 220, Height = 140, Z = 1, Data = new CardData { Title = "Attic" } },
        ],
    };

    private async Task<(double X, double Y)> HeadOfAsync(ILocator node)
    {
        var box = (await node.BoundingBoxAsync())!;
        return (box.X + box.Width / 2, box.Y + 14);
    }

    /// <summary>A point where the board itself, not a block or a panel, is under the finger.</summary>
    private async Task<(double X, double Y)> EmptyAsync()
    {
        var p = await Page.EvaluateAsync<double[]?>("""
            () => {
              const b = document.querySelector('.bc-board').getBoundingClientRect()
              for (let fy = 0.1; fy < 0.9; fy += 0.05)
                for (let fx = 0.1; fx < 0.9; fx += 0.05) {
                  const x = b.left + b.width * fx, y = b.top + b.height * fy
                  const el = document.elementFromPoint(x, y)
                  if (el && el.closest('.bc-board') && !el.closest('.bc-node, .bc-group, .bc-zoom, .bc-edge, button, a')) return [x, y]
                }
              return null
            }
            """);
        Assert.That(p, Is.Not.Null, "No empty spot on the board.");
        return (p![0], p[1]);
    }

    /// <summary>Opens the board with the properties panel closed, so in iPad portrait nothing lies over it.</summary>
    private async Task OpenAsync()
    {
        await ImportBoardAsync(TwoCards());
        var props = Page.Locator("#bc-props");
        if (Device == DeviceKind.Tablet && await props.IsVisibleAsync())
        {
            await Page.Locator("[data-bc-action='properties']").ClickAsync();
            await Expect(props).ToBeHiddenAsync();
        }
    }

    [Test]
    public async Task A_pinch_zooms_about_its_centre_and_the_page_itself_does_not_zoom()
    {
        await OpenAsync();
        var box = (await Board.BoundingBoxAsync())!;
        var (cx, cy) = (box.X + box.Width / 2, box.Y + box.Height / 2);
        var before = await WorldAtAsync(cx, cy);
        var zoom0 = (await ViewAsync())[2];
        var width = await Page.EvaluateAsync<double>("() => document.documentElement.clientWidth");

        await _touch.PinchAsync(cx, cy, 200, 100);

        var zoom = (await ViewAsync())[2];
        Assert.That(zoom / zoom0, Is.EqualTo(0.5).Within(0.05));
        var after = await WorldAtAsync(cx, cy);
        Assert.That(after.X, Is.EqualTo(before.X).Within(4));
        Assert.That(after.Y, Is.EqualTo(before.Y).Within(4));
        Assert.That(await Page.EvaluateAsync<double>("() => document.documentElement.clientWidth"), Is.EqualTo(width));
    }

    [Test]
    public async Task One_finger_on_empty_board_pans_and_moves_no_block()
    {
        await OpenAsync();
        var node = Nodes.First;
        var rect = await WorldRectAsync(node);
        var view = await ViewAsync();
        var (x, y) = await EmptyAsync();

        await _touch.DragAsync(x, y, x - 80, y + 120);

        var moved = await ViewAsync();
        Assert.That(moved[0] - view[0], Is.EqualTo(-80).Within(3));
        Assert.That(moved[1] - view[1], Is.EqualTo(120).Within(3));
        Assert.That((await WorldRectAsync(node)).X, Is.EqualTo(rect.X));
        Assert.That(await Page.EvaluateAsync<double>("() => window.scrollY"), Is.EqualTo(0), "The page itself must not scroll.");
    }

    [Test]
    public async Task One_finger_on_a_block_moves_it_as_one_undo_step()
    {
        await OpenAsync();
        var node = Nodes.First;
        var rect = await WorldRectAsync(node);
        var zoom = (await ViewAsync())[2];
        var (x, y) = await HeadOfAsync(node);

        await _touch.DragAsync(x, y, x + 60, y + 40);

        var after = await WorldRectAsync(node);
        Assert.That(after.X - rect.X, Is.EqualTo(60 / zoom).Within(3));
        Assert.That(after.Y - rect.Y, Is.EqualTo(40 / zoom).Within(3));
        await Expect(Page.Locator("[data-bc-action='undo']:visible")).ToHaveAttributeAsync("aria-label", new Regex("^Undo move"));
    }

    [Test]
    public async Task A_small_wobble_is_still_a_tap()
    {
        await OpenAsync();
        var node = Nodes.First;
        var rect = await WorldRectAsync(node);
        var (x, y) = await HeadOfAsync(node);

        await _touch.DragAsync(x, y, x + 6, y + 3, steps: 3);

        await Expect(node).ToHaveAttributeAsync("data-bc-selected", "true");
        Assert.That((await WorldRectAsync(node)).X, Is.EqualTo(rect.X));
    }

    [Test]
    public async Task A_second_finger_turns_a_drag_into_a_pinch_and_the_block_goes_back()
    {
        await OpenAsync();
        var node = Nodes.First;
        var rect = await WorldRectAsync(node);
        var zoom = (await ViewAsync())[2];
        var (x, y) = await HeadOfAsync(node);
        var box = (await Board.BoundingBoxAsync())!;

        await _touch.DragThenPinchAsync(x, y, 60, box.X + box.Width / 2, box.Y + box.Height / 2);

        await EventuallyAsync(async () => (await WorldRectAsync(node)).X, rect.X, 1);
        Assert.That((await ViewAsync())[2], Is.Not.EqualTo(zoom).Within(0.001));
        await Expect(Page.Locator(".bc-node--dragging")).ToHaveCountAsync(0);
    }

    [Test]
    public async Task A_long_press_on_a_block_opens_its_menu_and_on_empty_board_the_board_menu()
    {
        await OpenAsync();
        var (x, y) = await HeadOfAsync(Nodes.First);
        await _touch.LongPressAsync(x, y, 650);
        var menu = Page.Locator("[role='menu']");
        await Expect(menu).ToContainTextAsync("Duplicate");
        await Expect(menu).ToContainTextAsync("Delete");

        await Page.Keyboard.PressAsync("Escape");
        var (ex, ey) = await EmptyAsync();
        await _touch.LongPressAsync(ex, ey, 650);
        await Expect(Page.Locator("[role='menu']")).ToContainTextAsync("Add card here");
    }

    [Test]
    public async Task A_short_press_does_not_open_a_menu()
    {
        await OpenAsync();
        var (x, y) = await EmptyAsync();
        await _touch.LongPressAsync(x, y, 250);
        await Page.WaitForTimeoutAsync(400);
        await Expect(Page.Locator("[role='menu']:visible")).ToHaveCountAsync(0);
    }

    [Test]
    public async Task A_double_tap_on_empty_board_starts_a_note()
    {
        await OpenAsync();
        var (x, y) = await EmptyAsync();
        await _touch.TapAsync(x, y);
        await Page.WaitForTimeoutAsync(90);
        await _touch.TapAsync(x, y);
        await Expect(Page.Locator(".bc-node--text")).ToHaveCountAsync(1);
    }

    [Test]
    public async Task The_sheet_grip_drags_to_full_height_and_down_to_closed()
    {
        PhoneOnly();
        await StartCleanAsync();
        var add = (await Page.Locator(".bc-bar [data-bc-action='add-menu']").BoundingBoxAsync())!;
        await _touch.TapAsync(add.X + add.Width / 2, add.Y + add.Height / 2);
        var sheet = Page.Locator(".bc-sheet--open");
        await Expect(sheet).ToHaveClassAsync(new Regex("bc-sheet--half"));

        var grip = (await Page.Locator(".bc-sheet__grip").BoundingBoxAsync())!;
        await _touch.DragAsync(grip.X + grip.Width / 2, grip.Y + 10, grip.X + grip.Width / 2, grip.Y - 300);
        await Expect(sheet).ToHaveClassAsync(new Regex("bc-sheet--full"));
        Assert.That(await Page.EvaluateAsync<string>("() => localStorage.getItem('bc-layout')"), Does.Contain("\"sheetSnap\":\"full\""));

        grip = (await Page.Locator(".bc-sheet__grip").BoundingBoxAsync())!;
        await _touch.DragAsync(grip.X + grip.Width / 2, grip.Y + 10, grip.X + grip.Width / 2, grip.Y + 700);
        await Expect(Page.Locator(".bc-sheet--open")).ToHaveCountAsync(0);
    }

    [Test]
    public async Task A_refused_clipboard_opens_a_box_to_paste_into()
    {
        PhoneOnly();
        await Page.AddInitScriptAsync("""
            Object.defineProperty(navigator, 'clipboard', { configurable: true, value: {
              read: () => Promise.reject(new DOMException('denied', 'NotAllowedError')),
              readText: () => Promise.reject(new DOMException('denied', 'NotAllowedError')),
            } })
            """);
        await StartCleanAsync();
        var paste = (await Page.Locator(".bc-bar [data-bc-action='paste']").BoundingBoxAsync())!;
        await _touch.TapAsync(paste.X + paste.Width / 2, paste.Y + paste.Height / 2);

        var box = Page.Locator(".bc-sheet--open textarea[aria-label='Paste here']");
        await Expect(box).ToBeVisibleAsync();
        await box.EvaluateAsync("""
            el => {
              el.focus()
              const dt = new DataTransfer()
              dt.setData('text/plain', 'Seen at 3am on the landing')
              el.dispatchEvent(new ClipboardEvent('paste', { clipboardData: dt, bubbles: true, cancelable: true }))
            }
            """);

        await Expect(Page.Locator(".bc-node--text")).ToContainTextAsync("Seen at 3am");
        await Expect(Page.Locator(".bc-sheet--open")).ToHaveCountAsync(0);
    }

    [Test]
    public async Task A_photo_from_the_picker_becomes_a_picture()
    {
        PhoneOnly();
        await StartCleanAsync();
        var png = (await Page.EvaluateAsync<int[]>(
            "async () => { const c = new OffscreenCanvas(90, 60); c.getContext('2d').fillRect(0, 0, 45, 30); return [...new Uint8Array(await (await c.convertToBlob()).arrayBuffer())] }"))
            .Select(b => (byte)b).ToArray();

        await Page.SetInputFilesAsync("#bc-photo", new FilePayload { Name = "IMG_0007.png", MimeType = "image/png", Buffer = png });

        await Page.WaitForFunctionAsync("() => { const i = document.querySelector('.bc-node--image img'); return i && i.complete && i.naturalWidth === 90 }");
    }

    [Test]
    public async Task The_paste_button_reads_the_clipboard_from_a_tap()
    {
        await Context.GrantPermissionsAsync(["clipboard-read", "clipboard-write"], new() { Origin = CanvasUrl });
        await StartCleanAsync();
        await Page.EvaluateAsync("() => navigator.clipboard.writeText('https://x.com/ishaunted/status/1')");
        var selector = Device == DeviceKind.Phone ? ".bc-bar [data-bc-action='paste']" : ".bc-rail [data-bc-action='paste']";
        var paste = (await Page.Locator(selector).BoundingBoxAsync())!;

        await _touch.TapAsync(paste.X + paste.Width / 2, paste.Y + paste.Height / 2);

        await Expect(Page.Locator(".bc-node--link")).ToContainTextAsync("x.com");
    }
}
