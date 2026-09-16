using Microsoft.Playwright;

namespace Ben.Canvas.Playwright.Tests;

/// <summary>
/// Editing the board on a desktop: add, drag, resize, zoom, pan, marquee, undo and redo, the keyboard-only
/// path and the context menu - in a real browser, against the real gesture script.
/// </summary>
[Category("Editing")]
[TestFixture(DeviceKind.Desktop)]
public sealed class CanvasEditingTests(DeviceKind device) : CanvasTestBase(device)
{
    [Test]
    public async Task Adding_a_card_puts_it_in_the_middle_and_selects_it()
    {
        await StartCleanAsync();
        var card = await AddNodeAsync("card");

        await Expect(card).ToHaveAttributeAsync("data-bc-selected", "true");
        var box = (await card.BoundingBoxAsync())!;
        var board = (await Board.BoundingBoxAsync())!;
        Assert.That(box.X + box.Width / 2, Is.EqualTo(board.X + board.Width / 2).Within(30));
        Assert.That(box.Y + box.Height / 2, Is.EqualTo(board.Y + board.Height / 2).Within(30));
    }

    [Test]
    public async Task Dragging_a_node_moves_it_by_the_pointer_delta()
    {
        await StartCleanAsync();
        var card = await AddNodeAsync("card");
        var before = await WorldRectAsync(card);

        await DragAsync(card, 120, 80);

        // One animation frame after pointer up the block must already be at its committed place (R7).
        await Page.EvaluateAsync("() => new Promise(r => requestAnimationFrame(() => r()))");
        var after = await WorldRectAsync(card);
        Assert.That(after.X - before.X, Is.EqualTo(120).Within(2));
        Assert.That(after.Y - before.Y, Is.EqualTo(80).Within(2));
        await Expect(card).Not.ToHaveClassAsync(new System.Text.RegularExpressions.Regex("bc-node--dragging"));
    }

    [Test]
    public async Task Dragging_at_half_zoom_moves_twice_as_far_in_the_world()
    {
        await StartCleanAsync();
        var card = await AddNodeAsync("card");
        await Page.ClickAsync("[data-bc-action='zoom-out']");
        await Page.ClickAsync("[data-bc-action='zoom-out']");
        await Page.ClickAsync("[data-bc-action='zoom-out']");
        await Page.WaitForTimeoutAsync(300);
        var zoom = await ZoomAsync();
        var before = await WorldRectAsync(card);

        await DragAsync(card, 120, 80);

        var after = await WorldRectAsync(card);
        Assert.That(after.X - before.X, Is.EqualTo(120 / zoom).Within(3));
        Assert.That(after.Y - before.Y, Is.EqualTo(80 / zoom).Within(3));
    }

    [Test]
    public async Task Resizing_from_the_corner_keeps_the_opposite_corner()
    {
        await StartCleanAsync();
        var card = await AddNodeAsync("card");
        var before = await WorldRectAsync(card);

        var handle = Page.Locator(".bc-handle--br");
        var hb = (await handle.BoundingBoxAsync())!;
        await DragFromAsync(hb.X + hb.Width / 2, hb.Y + hb.Height / 2, 80, 60);

        var after = await WorldRectAsync(card);
        Assert.That(after.X, Is.EqualTo(before.X).Within(1));
        Assert.That(after.Y, Is.EqualTo(before.Y).Within(1));
        Assert.That(after.W - before.W, Is.EqualTo(80).Within(2));
        Assert.That(after.H - before.H, Is.EqualTo(60).Within(2));

        hb = (await handle.BoundingBoxAsync())!;
        await DragFromAsync(hb.X + hb.Width / 2, hb.Y + hb.Height / 2, -1000, -1000);
        var small = await WorldRectAsync(card);
        Assert.That(small.W, Is.EqualTo(220).Within(1));
        Assert.That(small.H, Is.EqualTo(140).Within(1));
    }

    [Test]
    public async Task Undo_reverses_the_last_gesture_and_redo_restores_it()
    {
        await StartCleanAsync();
        var card = await AddNodeAsync("card");
        var start = await WorldRectAsync(card);
        await DragAsync(card, 100, 0);
        var moved = await WorldRectAsync(card);

        await Expect(Page.Locator(".bc-header [data-bc-action='undo']")).ToHaveAttributeAsync("title", new System.Text.RegularExpressions.Regex("^Undo move"));
        await Board.FocusAsync();
        await Page.Keyboard.PressAsync("Control+z");
        await EventuallyAsync(async () => (await WorldRectAsync(card)).X, start.X, 1);
        await Page.Keyboard.PressAsync("Control+Shift+Z");
        await EventuallyAsync(async () => (await WorldRectAsync(card)).X, moved.X, 1);
    }

    [Test]
    public async Task Zoom_is_clamped_and_fit_shows_everything()
    {
        await StartCleanAsync();
        var (x, y) = await EmptySpotAsync();
        await Page.Mouse.MoveAsync((float)x, (float)y);
        await Page.Keyboard.DownAsync("Control");
        for (var i = 0; i < 40; i++) await Page.Mouse.WheelAsync(0, -200);
        await Page.Keyboard.UpAsync("Control");
        Assert.That(await ZoomAsync(), Is.EqualTo(4).Within(0.001));

        await Page.Keyboard.DownAsync("Control");
        for (var i = 0; i < 80; i++) await Page.Mouse.WheelAsync(0, 200);
        await Page.Keyboard.UpAsync("Control");
        Assert.That(await ZoomAsync(), Is.EqualTo(0.1).Within(0.001));

        await Page.ClickAsync("[data-bc-action='zoom-reset']");
        await AddNodeAsync("card");
        await AddNodeAsync("text");
        var far = await AddNodeAsync("message");
        await Page.ClickAsync("[data-bc-action='zoom-out']");
        await Page.ClickAsync("[data-bc-action='zoom-out']");
        await DragAsync(far, 500, 300);

        await Page.ClickAsync("[data-bc-action='fit']");
        await Page.WaitForTimeoutAsync(400);
        var board = (await Board.BoundingBoxAsync())!;
        foreach (var node in await Nodes.AllAsync())
        {
            var b = (await node.BoundingBoxAsync())!;
            Assert.That(b.X, Is.GreaterThanOrEqualTo(board.X - 1));
            Assert.That(b.Y, Is.GreaterThanOrEqualTo(board.Y - 1));
            Assert.That(b.X + b.Width, Is.LessThanOrEqualTo(board.X + board.Width + 1));
            Assert.That(b.Y + b.Height, Is.LessThanOrEqualTo(board.Y + board.Height + 1));
        }
    }

    [Test]
    public async Task Space_drag_pans()
    {
        await StartCleanAsync();
        var card = await AddNodeAsync("card");
        var rect = await WorldRectAsync(card);
        var pan = await PanXAsync();

        await Board.FocusAsync();
        var (x, y) = await EmptySpotAsync();
        await Page.Keyboard.DownAsync("Space");
        await DragFromAsync(x, y, 150, 40);
        await Page.Keyboard.UpAsync("Space");

        Assert.That(await PanXAsync(), Is.EqualTo(pan + 150).Within(2));
        Assert.That((await WorldRectAsync(card)).X, Is.EqualTo(rect.X));
    }

    [Test]
    public async Task Marquee_selects_what_it_covers()
    {
        await StartCleanAsync();
        var a = await AddNodeAsync("card");
        await DragAsync(a, -200, -150);
        var b = await AddNodeAsync("text");
        await DragAsync(b, -200, 150);
        var c = await AddNodeAsync("message");
        await DragAsync(c, 350, 0);

        var boxA = (await a.BoundingBoxAsync())!;
        var boxB = (await b.BoundingBoxAsync())!;
        var left = Math.Min(boxA.X, boxB.X) - 30;
        var top = boxA.Y - 30;
        await DragFromAsync(left, top, (boxA.Width + 60), (boxB.Y + boxB.Height - top + 30));

        await Expect(Page.Locator(".bc-node[data-bc-selected]")).ToHaveCountAsync(2);
    }

    [Test]
    public async Task Keyboard_only_edit()
    {
        await StartCleanAsync();
        var card = await AddNodeAsync("card");
        await Board.FocusAsync();
        var start = await WorldRectAsync(card);

        for (var i = 0; i < 5; i++) await Page.Keyboard.PressAsync("ArrowRight");
        await EventuallyAsync(async () => (await WorldRectAsync(card)).X, start.X + 5, 0.5);

        await Page.Keyboard.PressAsync("r");
        await Page.Keyboard.PressAsync("Shift+ArrowRight");
        await EventuallyAsync(async () => (await WorldRectAsync(card)).W, start.W + 10, 0.5);
        await Expect(Page.Locator("#bc-live")).ToContainTextAsync("Resized to");
        await Page.Keyboard.PressAsync("Escape");

        await Page.Keyboard.PressAsync("t");
        await Expect(Nodes).ToHaveCountAsync(2);
        await Page.Keyboard.PressAsync("c");
        await Page.Keyboard.PressAsync("Enter");
        await Expect(Page.Locator("g.bc-edge")).ToHaveCountAsync(1);
    }

    [Test]
    public async Task Right_click_offers_the_menu()
    {
        await StartCleanAsync();
        var card = await AddNodeAsync("card");
        await card.ClickAsync(new() { Button = MouseButton.Right });

        var menu = Page.Locator("[role='menu']");
        await Expect(menu).ToContainTextAsync("Duplicate");
        await Expect(menu).ToContainTextAsync("Lock");
        await Expect(menu).ToContainTextAsync("Delete");

        await menu.Locator("button", new() { HasText = "Lock" }).ClickAsync();
        await Expect(card).ToHaveAttributeAsync("data-bc-locked", "true");
        await Expect(Page.Locator(".bc-handle")).ToHaveCountAsync(0);
    }

    [Test]
    public async Task Double_click_on_empty_makes_a_note()
    {
        await StartCleanAsync();
        await AddNodeAsync("card");
        var (x, y) = await EmptySpotAsync();
        await Page.Mouse.DblClickAsync((float)x, (float)y);
        await Expect(Page.Locator(".bc-node--text")).ToHaveCountAsync(1);
        await Expect(Page.Locator(".bc-node--text.bc-node--editing")).ToHaveCountAsync(1);
    }

    [Test]
    public async Task Locked_nodes_do_not_move()
    {
        await StartCleanAsync();
        var card = await AddNodeAsync("card");
        await Board.FocusAsync();
        await Page.Keyboard.PressAsync("Control+Shift+L");
        await Expect(card).ToHaveAttributeAsync("data-bc-locked", "true");
        var before = await WorldRectAsync(card);

        await DragAsync(card, 150, 90);

        var after = await WorldRectAsync(card);
        Assert.That(after.X, Is.EqualTo(before.X));
        await Expect(Page.Locator("#bc-live")).ToContainTextAsync("locked");
    }

    [Test]
    public async Task Delete_removes_and_undo_brings_back()
    {
        await StartCleanAsync();
        await AddNodeAsync("card");
        await Board.FocusAsync();
        await Page.Keyboard.PressAsync("Delete");
        await Expect(Nodes).ToHaveCountAsync(0);
        await Page.Keyboard.PressAsync("Control+z");
        await Expect(Nodes).ToHaveCountAsync(1);
    }

    [Test]
    public async Task A_connector_follows_a_dragged_block()
    {
        await StartCleanAsync();
        var a = await AddNodeAsync("card");
        await DragAsync(a, -300, 0);
        var b = await AddNodeAsync("card");
        await DragAsync(b, 300, 0);

        await a.ClickAsync();
        var port = Page.Locator(".bc-port[data-bc-port='Right']");
        var pb = (await port.BoundingBoxAsync())!;
        var bb = (await b.BoundingBoxAsync())!;
        await DragFromAsync(pb.X + pb.Width / 2, pb.Y + pb.Height / 2, bb.X + bb.Width / 2 - (pb.X + pb.Width / 2), bb.Y + bb.Height / 2 - (pb.Y + pb.Height / 2));
        await Expect(Page.Locator("g.bc-edge")).ToHaveCountAsync(1);

        var line = Page.Locator("g.bc-edge .bc-edge__line");
        var before = await line.GetAttributeAsync("d");
        await DragAsync(b, 0, 120);
        await Expect(line).Not.ToHaveAttributeAsync("d", before!);
    }

    [Test]
    public async Task Browser_save_dialog_does_not_open_on_ctrl_s()
    {
        await StartCleanAsync();
        await Board.FocusAsync();
        var downloaded = false;
        Page.Download += (_, _) => downloaded = true;
        await Page.Keyboard.PressAsync("Control+s");
        await Page.WaitForTimeoutAsync(300);
        Assert.That(downloaded, Is.False);
        await AddNodeAsync("card");
    }

    [Test]
    public async Task No_console_errors_while_editing()
    {
        var errors = new List<string>();
        Page.Console += (_, m) => { if (m.Type == "error") errors.Add(m.Text); };
        await StartCleanAsync();
        var card = await AddNodeAsync("card");
        await DragAsync(card, 60, 40);
        await Board.FocusAsync();
        await Page.Keyboard.PressAsync("Control+z");
        Assert.That(errors, Is.Empty, string.Join("\n", errors));
    }
}
