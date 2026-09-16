using System.Text.RegularExpressions;
using Microsoft.Playwright;

namespace Ben.Canvas.Playwright.Tests;

/// <summary>
/// Every kind of block, connectors and groups, used the way a person uses them: add, edit, undo, connect,
/// style and group.
/// </summary>
[Category("Editing")]
[TestFixture(DeviceKind.Desktop)]
public sealed class CanvasBlocksTests(DeviceKind device) : CanvasTestBase(device)
{
    private async Task EditAsync(ILocator node)
    {
        var box = (await node.BoundingBoxAsync())!;
        await Page.Mouse.DblClickAsync(box.X + box.Width / 2, box.Y + 14);
        await Expect(node).ToHaveClassAsync(new Regex("bc-node--editing"));
    }

    private async Task<string> IdOf(ILocator node) => (await node.GetAttributeAsync("data-bc-node"))!;

    [TestCase("card", "file-text")]
    [TestCase("text", "edit-3")]
    [TestCase("message", "message-square")]
    [TestCase("map", "map-pin")]
    [TestCase("image", "image")]
    [TestCase("link", "link")]
    public async Task Every_addable_type_shows_its_own_head(string kind, string icon)
    {
        await StartCleanAsync();
        var node = await AddNodeAsync(kind);
        await Expect(node).ToHaveClassAsync(new Regex($"bc-node--{kind}"));
        await Expect(node.Locator($".bc-node__head use[href$='#{icon}']")).ToHaveCountAsync(1);
        Assert.That(await node.EvaluateAsync<string>("e => getComputedStyle(e).borderLeftWidth"), Is.EqualTo("3px"));
    }

    [Test]
    public async Task Card_fields_are_one_undo_step()
    {
        await StartCleanAsync();
        var card = await AddNodeAsync("card");
        var id = await IdOf(card);
        await EditAsync(card);

        await Page.FillAsync($"#bc-card-{Guid.Parse(id):N}-description", "Cold spot by the stairs");
        await Page.CheckAsync($"#bc-card-{Guid.Parse(id):N}-verified");
        await Page.Keyboard.PressAsync("Escape");

        await Expect(card).Not.ToHaveClassAsync(new Regex("bc-node--editing"));
        await Expect(card).ToContainTextAsync("Cold spot by the stairs");
        await Expect(card).ToContainTextAsync("Yes");

        await Board.FocusAsync();
        await Page.Keyboard.PressAsync("Control+z");
        await Expect(card).Not.ToContainTextAsync("Cold spot by the stairs");
        await Page.Keyboard.PressAsync("Control+Shift+Z");
        await Expect(card).ToContainTextAsync("Cold spot by the stairs");
    }

    [Test]
    public async Task A_note_is_typed_into_and_links_addresses()
    {
        await StartCleanAsync();
        var note = await AddNodeAsync("text");
        await EditAsync(note);
        await Page.Keyboard.TypeAsync("see https://example.com.");
        await Page.Keyboard.PressAsync("Escape");
        await Expect(note.Locator("a[href='https://example.com/']")).ToHaveCountAsync(1);
        await Expect(note).ToContainTextAsync("see https://example.com.");
    }

    [Test]
    public async Task A_message_can_be_made_bold()
    {
        await StartCleanAsync();
        var message = await AddNodeAsync("message");
        await EditAsync(message);
        var content = message.Locator(".k-editor [contenteditable='true']").First;
        await content.ClickAsync();
        await Page.Keyboard.TypeAsync("Footsteps");
        await Page.Keyboard.PressAsync("Control+a");
        await Page.Keyboard.PressAsync("Control+b");
        await Page.WaitForTimeoutAsync(200);
        await Page.Mouse.ClickAsync(700, 120);
        await Expect(message).Not.ToHaveClassAsync(new Regex("bc-node--editing"));
        await Expect(message.Locator(".bc-msg__body strong, .bc-msg__body b")).ToHaveTextAsync("Footsteps");
    }

    [Test]
    public async Task A_map_accepts_coordinates()
    {
        await StartCleanAsync();
        var map = await AddNodeAsync("map");
        var id = Guid.Parse(await IdOf(map)).ToString("N");
        await EditAsync(map);
        await Page.FillAsync($"#bc-map-ll-{id}", "hello");
        await Page.Keyboard.PressAsync("Tab");
        await Expect(map).ToContainTextAsync("were not understood");
        await Page.FillAsync($"#bc-map-ll-{id}", "36.16, -86.78");
        await Page.Keyboard.PressAsync("Tab");
        await Page.Keyboard.PressAsync("Escape");
        await map.Locator(".bc-node__body").ClickAsync();
        await Board.FocusAsync();
        await Page.Keyboard.PressAsync("Escape");
        await Expect(map).ToContainTextAsync("36.16000, -86.78000");
    }

    [Test]
    public async Task A_link_card_names_a_foreign_host()
    {
        await StartCleanAsync();
        var link = await AddNodeAsync("link");
        var id = Guid.Parse(await IdOf(link)).ToString("N");
        await EditAsync(link);
        await Page.FillAsync($"#bc-link-url-{id}", "https://example.com/a/b");
        await Page.Keyboard.PressAsync("Tab");
        await Board.FocusAsync();
        await Page.Keyboard.PressAsync("Escape");

        await Expect(link.Locator(".bc-link")).ToHaveClassAsync(new Regex("bc-link--away"));
        await Expect(link.Locator(".bc-link__kind")).ToHaveTextAsync("example.com");
        await Expect(link.Locator(".bc-link__open")).ToHaveAttributeAsync("rel", new Regex("noreferrer"));

        var pages = Context.Pages.Count;
        var box = (await link.BoundingBoxAsync())!;
        await Page.Mouse.ClickAsync(box.X + 40, box.Y + box.Height - 20);
        await Page.WaitForTimeoutAsync(300);
        Assert.That(Context.Pages.Count, Is.EqualTo(pages), "Clicking the card body must not open the page.");
    }

    [Test]
    public async Task A_link_is_fixed_height_and_a_file_is_not_resizable()
    {
        await StartCleanAsync();
        await AddNodeAsync("link");
        await Expect(Page.Locator(".bc-handle")).ToHaveCountAsync(2);
        await Expect(Page.Locator(".bc-handle--r")).ToHaveCountAsync(1);
        await Expect(Page.Locator(".bc-handle--l")).ToHaveCountAsync(1);
    }

    [Test]
    public async Task A_connector_label_colour_and_arrows_can_be_set()
    {
        await StartCleanAsync();
        var a = await AddNodeAsync("card");
        await DragAsync(a, -300, 0);
        var b = await AddNodeAsync("card");
        await DragAsync(b, 300, 0);
        await a.ClickAsync();
        await Board.FocusAsync();
        await Page.Keyboard.PressAsync("c");
        await Page.Keyboard.PressAsync("Enter");
        await Expect(Page.Locator("g.bc-edge[data-bc-selected]")).ToHaveCountAsync(1);

        await Page.FillAsync("#bc-prop-edge-label", "heard at 3am");
        await Page.Keyboard.PressAsync("Tab");
        await Expect(Page.Locator("g.bc-edge text")).ToHaveTextAsync("heard at 3am");

        await Page.ClickAsync("[aria-label='Colour Red']");
        await Expect(Page.Locator("g.bc-edge")).ToHaveClassAsync(new Regex("bc-edge--c3"));

        await Page.SelectOptionAsync("#bc-prop-edge-arrow", "Both");
        await Expect(Page.Locator("g.bc-edge .bc-edge__head")).ToHaveCountAsync(2);

        await Board.FocusAsync();
        await Page.Keyboard.PressAsync("Delete");
        await Expect(Page.Locator("g.bc-edge")).ToHaveCountAsync(0);
        await Page.Keyboard.PressAsync("Control+z");
        await Expect(Page.Locator("g.bc-edge")).ToHaveCountAsync(1);
    }

    [Test]
    public async Task Clicking_near_a_connector_selects_it()
    {
        await StartCleanAsync();
        var a = await AddNodeAsync("card");
        await DragAsync(a, -300, 0);
        var b = await AddNodeAsync("card");
        await DragAsync(b, 300, 0);
        await a.ClickAsync();
        await Board.FocusAsync();
        await Page.Keyboard.PressAsync("c");
        await Page.Keyboard.PressAsync("Enter");
        await Page.Mouse.ClickAsync(10, 10);
        await Board.FocusAsync();
        await Page.Keyboard.PressAsync("Escape");
        await Expect(Page.Locator("g.bc-edge[data-bc-selected]")).ToHaveCountAsync(0);

        var mid = await Page.Locator("g.bc-edge .bc-edge__line").EvaluateAsync<double[]>(
            "p => { const l = p.getTotalLength(); const pt = p.getPointAtLength(l / 2); const m = p.getScreenCTM(); return [pt.x * m.a + m.e, pt.y * m.d + m.f]; }");
        await Page.Mouse.ClickAsync((float)mid[0], (float)mid[1] + 3);
        await Expect(Page.Locator("g.bc-edge[data-bc-selected]")).ToHaveCountAsync(1);
        await Expect(Page.Locator("#bc-props")).ToContainTextAsync("Connector");
    }

    [Test]
    public async Task Grouping_moves_members_together()
    {
        await StartCleanAsync();
        var a = await AddNodeAsync("card");
        await DragAsync(a, -200, -80);
        var b = await AddNodeAsync("text");
        await DragAsync(b, 120, 60);

        await Board.FocusAsync();
        await Page.Keyboard.PressAsync("Control+a");
        await Page.Keyboard.PressAsync("Control+g");
        await Expect(Page.Locator(".bc-group")).ToHaveCountAsync(1);

        var ra = await WorldRectAsync(a);
        var rb = await WorldRectAsync(b);
        await DragAsync(Page.Locator(".bc-group__label"), 50, 0);
        Assert.That((await WorldRectAsync(a)).X - ra.X, Is.EqualTo(50).Within(3));
        Assert.That((await WorldRectAsync(b)).X - rb.X, Is.EqualTo(50).Within(3));

        await Board.FocusAsync();
        await Page.Keyboard.PressAsync("Control+z");
        await EventuallyAsync(async () => (await WorldRectAsync(a)).X, ra.X, 1);
        await EventuallyAsync(async () => (await WorldRectAsync(b)).X, rb.X, 1);
    }

    [Test]
    public async Task Dropping_a_node_into_a_group_makes_it_a_member()
    {
        await StartCleanAsync();
        var a = await AddNodeAsync("card");
        await Board.FocusAsync();
        await Page.Keyboard.PressAsync("Control+g");
        await Expect(Page.Locator(".bc-group")).ToHaveCountAsync(1);

        var outsider = await AddNodeAsync("text");
        await DragAsync(outsider, 0, 250);
        var group = (await Page.Locator(".bc-group").BoundingBoxAsync())!;
        var ob = (await outsider.BoundingBoxAsync())!;
        await DragAsync(outsider, group.X + group.Width / 2 - (ob.X + ob.Width / 2), group.Y + group.Height / 2 - 46 - (ob.Y + 14));
        await Expect(outsider).ToHaveAttributeAsync("aria-label", new Regex("in group"));

        var before = await WorldRectAsync(outsider);
        await DragAsync(Page.Locator(".bc-group__label"), 40, 0);
        Assert.That((await WorldRectAsync(outsider)).X - before.X, Is.EqualTo(40).Within(3));
    }

    [Test]
    public async Task Renaming_a_group_with_F2()
    {
        await StartCleanAsync();
        await AddNodeAsync("card");
        await Board.FocusAsync();
        await Page.Keyboard.PressAsync("Control+g");
        await Page.ClickAsync(".bc-group__label");
        await Board.FocusAsync();
        await Page.Keyboard.PressAsync("F2");
        var input = Page.Locator(".bc-group__input");
        await input.FillAsync("Basement");
        await input.PressAsync("Enter");
        await Expect(Page.Locator(".bc-group__label")).ToContainTextAsync("Basement");
        await Expect(Page.Locator(".bc-group")).ToHaveAttributeAsync("aria-label", new Regex("^Group: Basement"));
    }
}
