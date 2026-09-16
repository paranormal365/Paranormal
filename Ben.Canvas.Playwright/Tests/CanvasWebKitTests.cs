using System.Text.RegularExpressions;
using Ben.Canvas.Core.Model;
using Ben.Canvas.Core.Persistence;
using Microsoft.Playwright;
using Microsoft.Playwright.NUnit;

namespace Ben.Canvas.Playwright.Tests;

/// <summary>
/// The editor on the Safari engine (Playwright WebKit) with iPhone 13 and iPad descriptors: it boots, lays
/// out, takes taps, a pen lasso, the hardware keyboard, Cmd+V, the phone's paste box and a photo.
/// </summary>
/// <remarks>
/// <para>WebKit on Windows is not iOS Safari, and Playwright cannot inject multi-touch into WebKit, so pinch
/// and long-press are proven in Chromium (<see cref="CanvasTouchTests"/>). What these prove is the engine: its
/// CSS (dvh, safe areas), its clipboard and storage behaviour (OPFS writes, the IndexedDB fallback) and its
/// pointer events.</para>
/// <para>Needs the webkit browser: <c>playwright.ps1 install webkit</c>. Without it every test is Ignored.</para>
/// </remarks>
[Category("Touch")]
[Category("WebKit")]
[TestFixture("phone")]
[TestFixture("tablet")]
[NonParallelizable]
public sealed class CanvasWebKitTests(string device) : PlaywrightTest
{
    private static string CanvasUrl => (Environment.GetEnvironmentVariable("BEN_CANVAS_URL") ?? "http://localhost:5125").TrimEnd('/');

    private IBrowser _browser = null!;
    private IBrowserContext _context = null!;
    private IPage _page = null!;
    private readonly List<string> _errors = [];

    private bool Phone => device == "phone";

    [SetUp]
    public async Task OpenWebKitAsync()
    {
        var installed = Directory.Exists(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData) + "\\ms-playwright")
                        && Directory.EnumerateDirectories(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData) + "\\ms-playwright", "webkit-*").Any();
        if (!installed) Assert.Ignore("WebKit not installed: run playwright.ps1 install webkit.");

        using (var probe = new HttpClient { Timeout = TimeSpan.FromSeconds(5) })
        {
            try { (await probe.GetAsync($"{CanvasUrl}/_framework/dotnet.js")).EnsureSuccessStatusCode(); }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException) { Assert.Ignore($"Canvas host not running at {CanvasUrl}."); }
        }

        _browser = await Playwright.Webkit.LaunchAsync();
        var options = new BrowserNewContextOptions(Playwright.Devices[Phone ? "iPhone 13" : "iPad (gen 7)"])
        {
            ViewportSize = Phone ? new() { Width = 375, Height = 812 } : new() { Width = 768, Height = 1024 },
        };
        _context = await _browser.NewContextAsync(options);
        _page = await _context.NewPageAsync();
        _page.PageError += (_, message) => _errors.Add(message);
    }

    [TearDown]
    public async Task CloseWebKitAsync()
    {
        if (_context is not null) await _context.CloseAsync();
        if (_browser is not null) await _browser.CloseAsync();
    }

    private ILocator Ready => _page.Locator(".bc-editor[data-bc-ready='true']");

    private async Task StartCleanAsync()
    {
        await _page.GotoAsync(CanvasUrl + "/");
        await Expect(Ready).ToHaveCountAsync(1, new() { Timeout = 90_000 });
        await _page.EvaluateAsync("""
            async () => {
              localStorage.clear(); sessionStorage.clear(); localStorage.setItem('bc-persist-told', '1')
              try { const root = await navigator.storage.getDirectory(); for await (const name of root.keys()) await root.removeEntry(name, { recursive: true }) } catch { }
              for (const name of ['bc-docs', 'bc-assets']) await new Promise(done => { const r = indexedDB.deleteDatabase(name); r.onsuccess = r.onerror = r.onblocked = () => done() })
            }
            """);
        await _page.ReloadAsync();
        await Expect(Ready).ToHaveCountAsync(1, new() { Timeout = 90_000 });
    }

    private async Task ImportAsync(CanvasDocument board)
    {
        var path = Path.Combine(Path.GetTempPath(), $"webkit-{Guid.NewGuid():N}.ishcanvas");
        await File.WriteAllBytesAsync(path, CanvasPackage.Write(board, []));
        await StartCleanAsync();
        await _page.SetInputFilesAsync("#bc-import", path);
        await Expect(_page.Locator(".bc-node")).ToHaveCountAsync(board.Nodes.Count, new() { Timeout = 15_000 });
        File.Delete(path);
    }

    private static CanvasDocument ThreeCards() => new()
    {
        Title = "Safari",
        NextZ = 3,
        Nodes =
        [
            new CanvasNode { Type = CanvasNodeType.Card, X = 0, Y = 0, Width = 220, Height = 140, Z = 0, Data = new CardData { Title = "One" } },
            new CanvasNode { Type = CanvasNodeType.Card, X = 300, Y = 0, Width = 220, Height = 140, Z = 1, Data = new CardData { Title = "Two" } },
            new CanvasNode { Type = CanvasNodeType.Card, X = 0, Y = 600, Width = 220, Height = 140, Z = 2, Data = new CardData { Title = "Three" } },
        ],
    };

    private async Task CloseTabletPanelAsync()
    {
        if (Phone) return;
        var props = _page.Locator("#bc-props");
        if (await props.IsVisibleAsync()) await _page.Locator("[data-bc-action='properties']").ClickAsync();
        await Expect(props).ToBeHiddenAsync();
    }

    [Test]
    public async Task The_editor_boots_on_the_Safari_engine_without_errors()
    {
        await StartCleanAsync();
        await Expect(_page.Locator(".bc-board")).ToBeVisibleAsync();
        Assert.That(_errors, Is.Empty, string.Join("\n", _errors));
    }

    [Test]
    public async Task The_layout_matches_the_Chromium_contract_and_nothing_scrolls_sideways()
    {
        await StartCleanAsync();
        if (Phone)
        {
            await Expect(_page.Locator(".bc-bar")).ToBeVisibleAsync();
            await Expect(_page.Locator(".bc-rail")).ToBeHiddenAsync();
            foreach (var button in await _page.Locator(".bc-bar button").AllAsync())
            {
                var box = (await button.BoundingBoxAsync())!;
                Assert.That(Math.Min(box.Width, box.Height), Is.GreaterThanOrEqualTo(43.5f));
            }
        }
        else
        {
            await Expect(_page.Locator(".bc-rail")).ToBeVisibleAsync();
            await Expect(_page.Locator(".bc-bar")).ToBeHiddenAsync();
        }

        Assert.That(await _page.EvaluateAsync<bool>("() => document.documentElement.scrollWidth <= document.documentElement.clientWidth + 1"), Is.True);
    }

    [Test]
    public async Task A_tap_selects_a_block()
    {
        await ImportAsync(ThreeCards());
        await CloseTabletPanelAsync();
        var node = _page.Locator(".bc-node").First;
        var box = (await node.BoundingBoxAsync())!;
        await _page.Touchscreen.TapAsync(box.X + box.Width / 2, box.Y + 12);
        await Expect(node).ToHaveAttributeAsync("data-bc-selected", "true");
    }

    [Test]
    public async Task An_Apple_Pencil_lasso_selects_the_blocks_it_covers()
    {
        await ImportAsync(ThreeCards());
        await CloseTabletPanelAsync();
        await _page.Locator("[data-bc-action='zoom-out']").ClickAsync();
        await _page.Locator("[data-bc-action='zoom-out']").ClickAsync();
        await _page.WaitForTimeoutAsync(400);
        var boxes = new List<LocatorBoundingBoxResult>();
        foreach (var n in (await _page.Locator(".bc-node").AllAsync()).Take(2)) boxes.Add((await n.BoundingBoxAsync())!);
        var left = (double)boxes.Min(b => b.X) - 10;
        var top = (double)boxes.Min(b => b.Y) - 10;
        var right = (double)boxes.Max(b => b.X + b.Width) + 10;
        var bottom = (double)boxes.Max(b => b.Y + b.Height) + 10;
        var start = await _page.EvaluateAsync<bool>("([x, y]) => { const el = document.elementFromPoint(x, y); return !!el && !el.closest('.bc-node') }", new[] { left, top });
        Assert.That(start, Is.True, "The lasso must start on empty board.");

        await _page.EvaluateAsync("""
            ([x0, y0, x1, y1]) => {
              const board = document.querySelector('.bc-board')
              const at = (x, y, type) => new PointerEvent(type, { bubbles: true, cancelable: true, composed: true, pointerId: 7, pointerType: 'pen', isPrimary: true, button: type === 'pointermove' ? -1 : 0, buttons: type === 'pointerup' ? 0 : 1, clientX: x, clientY: y })
              document.elementFromPoint(x0, y0).dispatchEvent(at(x0, y0, 'pointerdown'))
              for (let i = 1; i <= 10; i++) board.dispatchEvent(at(x0 + (x1 - x0) * i / 10, y0 + (y1 - y0) * i / 10, 'pointermove'))
              board.dispatchEvent(at(x1, y1, 'pointerup'))
            }
            """, new[] { left, top, right, bottom });

        await Expect(_page.Locator(".bc-node[data-bc-selected]")).ToHaveCountAsync(2);
    }

    [Test]
    public async Task The_hardware_keyboard_undoes_with_Cmd_Z()
    {
        if (Phone) Assert.Ignore("An iPad with a keyboard.");
        await StartCleanAsync();
        await _page.Locator(".bc-rail [data-bc-action='add-card']").ClickAsync();
        await Expect(_page.Locator(".bc-node")).ToHaveCountAsync(1);
        await _page.Locator(".bc-board").FocusAsync();
        await _page.Keyboard.PressAsync("ControlOrMeta+z");
        await Expect(_page.Locator(".bc-node")).ToHaveCountAsync(0);
    }

    [Test]
    public async Task Cmd_V_on_the_board_pastes_in_Safari()
    {
        await StartCleanAsync();
        await CloseTabletPanelAsync();
        await _page.EvaluateAsync("""
            () => {
              const t = document.createElement('textarea')
              t.id = 'test-source'; t.value = 'Heard a door close upstairs'
              document.body.appendChild(t); t.focus(); t.select()
            }
            """);
        // ControlOrMeta: Cmd on a Mac and on iOS, Ctrl where Playwright's WebKit runs on Windows.
        await _page.Keyboard.PressAsync("ControlOrMeta+c");
        await _page.Locator(".bc-board").FocusAsync();
        await _page.Keyboard.PressAsync("ControlOrMeta+v");

        await Expect(_page.Locator(".bc-node--text")).ToContainTextAsync("Heard a door close upstairs", new() { Timeout = 10_000 });
    }

    [Test]
    public async Task A_refused_clipboard_opens_the_paste_box_in_Safari()
    {
        if (!Phone) Assert.Ignore("The paste box is the phone's answer.");
        await _page.AddInitScriptAsync("""
            Object.defineProperty(navigator, 'clipboard', { configurable: true, value: {
              read: () => Promise.reject(new DOMException('denied', 'NotAllowedError')),
              readText: () => Promise.reject(new DOMException('denied', 'NotAllowedError')),
            } })
            """);
        await StartCleanAsync();
        var paste = (await _page.Locator(".bc-bar [data-bc-action='paste']").BoundingBoxAsync())!;
        await _page.Touchscreen.TapAsync(paste.X + paste.Width / 2, paste.Y + paste.Height / 2);
        await Expect(_page.Locator(".bc-sheet--open textarea[aria-label='Paste here']")).ToBeVisibleAsync();
    }

    [Test]
    public async Task A_photo_is_kept_even_when_Safari_cannot_write_files()
    {
        await _page.AddInitScriptAsync("if (window.FileSystemFileHandle) FileSystemFileHandle.prototype.createWritable = undefined");
        await StartCleanAsync();
        var png = (await _page.EvaluateAsync<int[]>(
            "async () => { const c = document.createElement('canvas'); c.width = 80; c.height = 50; c.getContext('2d').fillRect(0, 0, 40, 25); const b = await new Promise(r => c.toBlob(r)); return [...new Uint8Array(await b.arrayBuffer())] }"))
            .Select(b => (byte)b).ToArray();

        await _page.SetInputFilesAsync("#bc-photo", new FilePayload { Name = "IMG_0001.png", MimeType = "image/png", Buffer = png });
        await _page.WaitForFunctionAsync("() => { const i = document.querySelector('.bc-node--image img'); return i && i.complete && i.naturalWidth === 80 }", null, new() { Timeout = 15_000 });

        await Expect(_page.Locator(".bc-savestate")).ToContainTextAsync("Saved on this device", new() { Timeout = 10_000 });
        await _page.ReloadAsync();
        await Expect(Ready).ToHaveCountAsync(1, new() { Timeout = 90_000 });
        await _page.WaitForFunctionAsync("() => { const i = document.querySelector('.bc-node--image img'); return i && i.complete && i.naturalWidth === 80 }", null, new() { Timeout = 15_000 });
    }

    [Test]
    public async Task The_phone_sheet_uses_dynamic_viewport_height()
    {
        if (!Phone) Assert.Ignore("A phone check.");
        await StartCleanAsync();
        var add = (await _page.Locator(".bc-bar [data-bc-action='add-menu']").BoundingBoxAsync())!;
        await _page.Touchscreen.TapAsync(add.X + add.Width / 2, add.Y + add.Height / 2);
        var sheet = _page.Locator(".bc-sheet--open");
        await Expect(sheet).ToHaveClassAsync(new Regex("bc-sheet--half"));
        await _page.WaitForTimeoutAsync(300);
        var ratio = await sheet.EvaluateAsync<double>("e => e.getBoundingClientRect().height / window.innerHeight");
        Assert.That(ratio, Is.EqualTo(0.45).Within(0.03));
    }
}
