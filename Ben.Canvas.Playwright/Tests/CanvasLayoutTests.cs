using System.Text.RegularExpressions;
using Ben.Canvas.Core.Model;
using Ben.Canvas.Core.Persistence;
using Microsoft.Playwright;

namespace Ben.Canvas.Playwright.Tests;

/// <summary>
/// The editor's layout at desktop, iPad and iPhone widths: a rail and a properties panel from 768 px up, the
/// panel laid over the board in iPad portrait, and below 768 px a bottom bar with properties in a sheet.
/// Nothing scrolls sideways, the page never bounces, and every control is big enough for a finger.
/// </summary>
[Category("Layout")]
[TestFixture(DeviceKind.Desktop)]
[TestFixture(DeviceKind.Tablet)]
[TestFixture(DeviceKind.Phone)]
public sealed class CanvasLayoutTests(DeviceKind device) : CanvasTestBase(device)
{
    private const float Finger = 43.5f;

    private ILocator Bar => Page.Locator(".bc-bar");
    private ILocator Rail => Page.Locator(".bc-rail");
    private ILocator Props => Page.Locator("#bc-props");
    private ILocator Sheet => Page.Locator(".bc-sheet--open");

    /// <summary>Opens a board of six cards spread over 3000 px and one note, imported through the file input.</summary>
    private async Task SeedSpreadBoardAsync()
    {
        var nodes = new List<CanvasNode>();
        for (var i = 0; i < 6; i++)
            nodes.Add(new CanvasNode { Type = CanvasNodeType.Card, X = i * 600, Y = i % 2 * 3000, Width = 280, Height = 200, Z = i, Data = new CardData { Title = $"Card {i + 1}" } });
        nodes.Add(new CanvasNode { Type = CanvasNodeType.Text, X = 1500, Y = 1500, Width = 220, Height = 120, Z = 6, Data = new TextData { Text = "Middle" } });

        var path = Path.Combine(Path.GetTempPath(), $"spread-{Guid.NewGuid():N}.ishcanvas");
        await File.WriteAllBytesAsync(path, CanvasPackage.Write(new CanvasDocument { Title = "Spread", Nodes = nodes, NextZ = 7 }, []));
        await StartCleanAsync();
        await Page.SetInputFilesAsync("#bc-import", path);
        await Expect(Nodes).ToHaveCountAsync(7, new() { Timeout = 10_000 });
        File.Delete(path);
    }

    private async Task SizeAsync(int width, int height) => await Page.SetViewportSizeAsync(width, height);

    private async Task TapFirstNodeAsync()
    {
        var node = Nodes.First;
        await node.ScrollIntoViewIfNeededAsync();
        var box = (await node.BoundingBoxAsync())!;
        if (Device == DeviceKind.Desktop) await Page.Mouse.ClickAsync(box.X + box.Width / 2, box.Y + 14);
        else await Page.Touchscreen.TapAsync(box.X + box.Width / 2, box.Y + 14);
        await Expect(Page.Locator(".bc-node[data-bc-selected]")).ToHaveCountAsync(1);
    }

    private void Only(params DeviceKind[] kinds)
    {
        if (!kinds.Contains(Device)) Assert.Ignore($"Not a {Device} check.");
    }

    [Test]
    public async Task The_page_never_scrolls_sideways()
    {
        await SeedSpreadBoardAsync();
        await Page.ClickAsync("[data-bc-action='fit']");
        var fits = await Page.EvaluateAsync<bool>("() => document.documentElement.scrollWidth <= document.documentElement.clientWidth + 1 && window.scrollY === 0");
        Assert.That(fits, Is.True);
    }

    [Test]
    public async Task On_a_phone_the_bottom_bar_carries_the_tools()
    {
        Only(DeviceKind.Phone);
        await StartCleanAsync();
        await Expect(Bar).ToBeInViewportAsync();
        await Expect(Rail).ToBeHiddenAsync();

        var actions = await Bar.Locator("button, label").EvaluateAllAsync<string[]>("els => els.map(e => e.dataset.bcAction)");
        Assert.That(actions, Is.EqualTo(new[] { "add-menu", "paste", "undo", "redo", "more" }));

        var bar = (await Bar.BoundingBoxAsync())!;
        var fit = (await Page.Locator("[data-bc-action='fit']").BoundingBoxAsync())!;
        Assert.That(fit.Y + fit.Height, Is.LessThanOrEqualTo(bar.Y + 1), "The zoom controls sit above the bar.");
        await Expect(Page.Locator(".bc-minimap")).ToBeHiddenAsync();
    }

    /// <summary>
    /// Export and Import are handed to the header from the editor, so the header's scoped styles must still reach
    /// them: every header button has Undo's size and see-through background, not the browser's white button.
    /// </summary>
    [Test]
    public async Task Every_header_button_looks_like_the_others()
    {
        Only(DeviceKind.Desktop, DeviceKind.Tablet);
        await StartCleanAsync();
        var look = "e => { const s = getComputedStyle(e); return [s.backgroundColor, s.width, s.height, s.borderTopWidth].join('|') }";
        var undo = await Page.Locator(".bc-header [data-bc-action='undo']").EvaluateAsync<string>(look);
        foreach (var action in new[] { "export", "import", "properties", "help" })
            Assert.That(await Page.Locator($".bc-header [data-bc-action='{action}']").EvaluateAsync<string>(look), Is.EqualTo(undo), $"The {action} button does not look like Undo.");
    }

    /// <summary>An iPad in Split View is as narrow as a phone, so it gets the phone's layout.</summary>
    [Test]
    public async Task An_iPad_in_split_view_gets_the_phone_layout()
    {
        Only(DeviceKind.Tablet);
        await StartCleanAsync();
        await SizeAsync(507, 1024);

        await Expect(Bar).ToBeVisibleAsync();
        await Expect(Rail).ToBeHiddenAsync();
        var fits = await Page.EvaluateAsync<bool>("() => document.documentElement.scrollWidth <= document.documentElement.clientWidth + 1");
        Assert.That(fits, Is.True);
    }

    /// <summary>Turning the device keeps the header and the tools on screen, with nothing under the notch or home bar.</summary>
    [Test]
    public async Task Turning_the_device_keeps_the_header_and_the_tools_on_screen()
    {
        Only(DeviceKind.Phone, DeviceKind.Tablet);
        await StartCleanAsync();

        foreach (var (width, height) in new[] { (375, 812), (812, 375) })
        {
            await SizeAsync(width, height);
            var header = (await Page.Locator(".bc-header").BoundingBoxAsync())!;
            Assert.That(header.Y, Is.GreaterThanOrEqualTo(0), $"The header is off the top at {width}x{height}.");
            Assert.That(header.X + header.Width, Is.LessThanOrEqualTo(width + 1), $"The header runs off the side at {width}x{height}.");

            var tools = await Bar.IsVisibleAsync() ? Bar : Rail;
            var box = (await tools.BoundingBoxAsync())!;
            Assert.That(box.Y + box.Height, Is.LessThanOrEqualTo(height + 1), $"The tools are off the bottom at {width}x{height}.");
            Assert.That(box.X, Is.GreaterThanOrEqualTo(0), $"The tools are off the side at {width}x{height}.");
        }
    }

    /// <summary>The host's theme switch sits above the phone bar, where it can be seen and tapped.</summary>
    [Test]
    public async Task On_a_phone_the_theme_switch_sits_above_the_bar()
    {
        Only(DeviceKind.Phone);
        await StartCleanAsync();
        var toggle = Page.Locator(".bwc-theme-toggle");
        if (await toggle.CountAsync() == 0) Assert.Ignore("This host has no theme switch.");
        var bar = (await Bar.BoundingBoxAsync())!;
        var box = (await toggle.BoundingBoxAsync())!;
        Assert.That(box.Y + box.Height, Is.LessThanOrEqualTo(bar.Y + 1), "The switch must not sit on the bar.");
        var hit = await Page.EvaluateAsync<bool>("([x, y]) => !!document.elementFromPoint(x, y)?.closest('.bwc-theme-toggle')", new[] { (double)(box.X + box.Width / 2), (double)(box.Y + box.Height / 2) });
        Assert.That(hit, Is.True, "The switch must be tappable, not covered.");
    }
    [Test]
    public async Task Every_bar_button_is_big_enough_to_hit_with_a_finger()
    {
        Only(DeviceKind.Phone);
        await StartCleanAsync();
        foreach (var button in await Bar.Locator("button, label").AllAsync())
        {
            var box = (await button.BoundingBoxAsync())!;
            Assert.That(box.Width, Is.GreaterThanOrEqualTo(Finger), await button.GetAttributeAsync("data-bc-action"));
            Assert.That(box.Height, Is.GreaterThanOrEqualTo(Finger), await button.GetAttributeAsync("data-bc-action"));
        }
    }

    [Test]
    public async Task An_iPad_gets_the_rail_and_the_properties_panel()
    {
        Only(DeviceKind.Tablet);
        await SeedSpreadBoardAsync();
        await Expect(Rail).ToBeVisibleAsync();
        await Expect(Bar).ToBeHiddenAsync();
        await TapFirstNodeAsync();
        await Expect(Props).ToBeVisibleAsync();
        await Expect(Page.Locator(".bc-sheet--open")).ToHaveCountAsync(0);
    }

    [Test]
    public async Task An_iPad_in_portrait_lays_properties_over_the_board()
    {
        Only(DeviceKind.Tablet);
        await StartCleanAsync();
        if (!await Props.IsVisibleAsync()) await Page.ClickAsync("[data-bc-action='properties']");
        await Expect(Props).ToBeVisibleAsync();
        var open = (await Board.BoundingBoxAsync())!.Width;

        await Page.ClickAsync("[data-bc-action='properties']");
        await Expect(Props).ToBeHiddenAsync();
        var closed = (await Board.BoundingBoxAsync())!.Width;
        Assert.That(open, Is.EqualTo(closed).Within(1), "Opening properties must not shrink the board in portrait.");

        await Page.ClickAsync("[data-bc-action='properties']");
        var props = (await Props.BoundingBoxAsync())!;
        Assert.That(props.X + props.Width, Is.EqualTo(768).Within(1));
    }

    [Test]
    public async Task In_landscape_properties_take_their_own_column()
    {
        Only(DeviceKind.Tablet);
        await StartCleanAsync();
        await SizeAsync(1024, 768);
        if (!await Props.IsVisibleAsync()) await Page.ClickAsync("[data-bc-action='properties']");
        var open = (await Board.BoundingBoxAsync())!.Width;
        await Page.ClickAsync("[data-bc-action='properties']");
        await Expect(Props).ToBeHiddenAsync();
        var closed = (await Board.BoundingBoxAsync())!.Width;
        Assert.That(closed - open, Is.EqualTo(320).Within(2));
    }

    [Test]
    public async Task The_properties_panel_remembers_being_closed()
    {
        Only(DeviceKind.Desktop);
        await StartCleanAsync();
        await Expect(Props).ToBeVisibleAsync();
        await Page.ClickAsync("[data-bc-action='properties']");
        await Expect(Props).ToBeHiddenAsync();

        await Page.ReloadAsync();
        await WaitReadyAsync();
        await Expect(Props).ToBeHiddenAsync();
        var layout = await Page.EvaluateAsync<string>("() => localStorage.getItem('bc-layout')");
        Assert.That(layout, Does.Contain("\"propsOpen\":false"));
    }

    [Test]
    public async Task A_phone_opens_properties_as_a_sheet()
    {
        Only(DeviceKind.Phone);
        await SeedSpreadBoardAsync();
        await TapFirstNodeAsync();
        await Page.ClickAsync(".bc-bar [data-bc-action='more']");
        await Page.ClickAsync(".bc-sheet--open [data-bc-action='edit']");

        var sheet = Page.Locator(".bc-sheet--open[role='dialog'][aria-label='Properties']");
        await Expect(sheet).ToBeVisibleAsync();
        Assert.That(await sheet.GetAttributeAsync("aria-modal"), Is.EqualTo("false"));
        var box = (await sheet.BoundingBoxAsync())!;
        Assert.That(box.Y, Is.GreaterThanOrEqualTo(0.40 * 812));
        var aboveIsBoard = await Page.EvaluateAsync<bool>("y => !!document.elementFromPoint(187, y)?.closest('.bc-board')", (double)(box.Y - 40));
        Assert.That(aboveIsBoard, Is.True, "The board above the sheet stays usable.");
    }

    [Test]
    public async Task Every_handle_and_port_is_big_enough_to_hit_with_a_finger()
    {
        Only(DeviceKind.Tablet);
        await SeedSpreadBoardAsync();
        Assert.That(await Page.EvaluateAsync<bool>("() => matchMedia('(pointer: coarse)').matches"), Is.True, "This context should report a coarse pointer.");
        await TapFirstNodeAsync();

        var handle = await Page.Locator("[data-bc-handle='br']").EvaluateAsync<double[]>("e => { const s = getComputedStyle(e, '::before'); return [parseFloat(s.width), parseFloat(s.height)] }");
        Assert.That(handle[0], Is.GreaterThanOrEqualTo(Finger));
        Assert.That(handle[1], Is.GreaterThanOrEqualTo(Finger));
    }

    [Test]
    public async Task The_header_never_wraps()
    {
        await StartCleanAsync();
        foreach (var (w, h) in Device switch
                 {
                     DeviceKind.Desktop => new[] { (1280, 800) },
                     DeviceKind.Tablet => [(768, 1024), (1024, 768)],
                     _ => [(375, 812)],
                 })
        {
            await SizeAsync(w, h);
            var header = (await Page.Locator(".bc-header").BoundingBoxAsync())!;
            Assert.That(header.Height, Is.LessThanOrEqualTo(56.5), $"{w}x{h}");
        }
    }

    [Test]
    public async Task A_phone_does_not_rubber_band()
    {
        Only(DeviceKind.Phone);
        await StartCleanAsync();
        Assert.That(await Page.EvaluateAsync<string>("() => getComputedStyle(document.body).position"), Is.EqualTo("fixed"));
        Assert.That(await Page.EvaluateAsync<string>("() => getComputedStyle(document.documentElement).overscrollBehaviorY"), Is.EqualTo("none"));
    }

    [Test]
    public async Task Reduced_motion_turns_off_the_sheet_animation()
    {
        Only(DeviceKind.Phone);
        await Page.EmulateMediaAsync(new() { ReducedMotion = ReducedMotion.Reduce });
        await StartCleanAsync();
        await Page.ClickAsync(".bc-bar [data-bc-action='add-menu']");
        await Expect(Sheet).ToBeVisibleAsync();
        Assert.That(await Sheet.EvaluateAsync<string>("e => getComputedStyle(e).transitionDuration"), Is.EqualTo("0s"));
        Assert.That(await Page.Locator(".bc-world").EvaluateAsync<string>("e => getComputedStyle(e).transitionDuration"), Does.Match(new Regex("^0s")));
    }
}
