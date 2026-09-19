using Microsoft.Playwright;
using Microsoft.Playwright.NUnit;

namespace Ben.Canvas.Playwright;

/// <summary>The sizes and input models the canvas is verified on.</summary>
public enum DeviceKind
{
    /// <summary>1280 x 800, mouse and keyboard.</summary>
    Desktop,

    /// <summary>768 x 1024, touch, device scale 2 (an iPad in portrait).</summary>
    Tablet,

    /// <summary>375 x 812, touch, device scale 3 (an iPhone).</summary>
    Phone,

    /// <summary>Playwright's iPhone 13 descriptor on the WebKit engine. Needs the webkit browser installed.</summary>
    WebKitPhone,

    /// <summary>Playwright's iPad (gen 7) descriptor on the WebKit engine. Needs the webkit browser installed.</summary>
    WebKitTablet,
}

/// <summary>
/// Shared setup for every canvas browser test.
/// </summary>
/// <remarks>
/// <para>Modelled on Ben.Web.Playwright's BenTestBase and WasmEditorTests. A WebAssembly host has its own
/// failure modes, and each gets one clear outcome here instead of a wall of timeouts: a host that is not
/// running makes every test Ignored with the command to start it, and a framework file that 404s (a
/// rebuilt host that was not restarted) fails the test that saw it.</para>
///
/// <para><b>Touch contexts.</b> <c>HasTouch</c> changes Chromium's input model, so the mouse helpers
/// belong to Desktop fixtures only; touch fixtures drive the page with Touchscreen and CDP touch events.</para>
/// </remarks>
public abstract class CanvasTestBase : PageTest
{
    protected CanvasTestBase() : this(DeviceKind.Desktop) { }

    protected CanvasTestBase(DeviceKind device) => Device = device;

    /// <summary>The device this fixture instance runs as.</summary>
    protected DeviceKind Device { get; }

    /// <summary>The canvas host, from BEN_CANVAS_URL.</summary>
    protected static string CanvasUrl => (Environment.GetEnvironmentVariable("BEN_CANVAS_URL") ?? "http://localhost:5125").TrimEnd('/');

    /// <summary>True when the tests run against a deployed site rather than a host on this machine.</summary>
    protected static bool IsRemote => new Uri(CanvasUrl).Host is not ("localhost" or "127.0.0.1");

    /// <summary>The Web API, from BEN_API_URL.</summary>
    protected static string ApiUrl => (Environment.GetEnvironmentVariable("BEN_API_URL") ?? "http://localhost:5252").TrimEnd('/');

    /// <summary>The seeded account the server tests sign in as.</summary>
    protected static string UserEmail => Environment.GetEnvironmentVariable("BEN_USER_EMAIL") ?? "sarah.mitchell@benco.dev";

    /// <summary>The seeded account's password. Never written to a file; a test that needs it is Ignored without it.</summary>
    protected static string UserPassword => RequiredSecret("BEN_USER_PASSWORD");

    /// <summary>Framework files that failed to load during the test.</summary>
    protected List<string> FrameworkFailures { get; } = [];

    /// <summary>The Browser Context options for <see cref="Device"/>.</summary>
    public override BrowserNewContextOptions ContextOptions() => ContextOptionsFor(Device, Playwright);

    /// <summary>The Browser Context options for a device.</summary>
    public static BrowserNewContextOptions ContextOptionsFor(DeviceKind device, IPlaywright playwright) => device switch
    {
        DeviceKind.Desktop => new() { ViewportSize = new() { Width = 1280, Height = 800 } },
        DeviceKind.Tablet => new() { ViewportSize = new() { Width = 768, Height = 1024 }, HasTouch = true, IsMobile = true, DeviceScaleFactor = 2 },
        DeviceKind.Phone => new() { ViewportSize = new() { Width = 375, Height = 812 }, HasTouch = true, IsMobile = true, DeviceScaleFactor = 3 },
        DeviceKind.WebKitPhone => new(playwright.Devices["iPhone 13"]),
        DeviceKind.WebKitTablet => new(playwright.Devices["iPad (gen 7)"]),
        _ => throw new ArgumentOutOfRangeException(nameof(device)),
    };

    /// <summary>Ignores the test when a secret environment variable is not set, rather than failing.</summary>
    protected static string RequiredSecret(string name)
    {
        var value = Environment.GetEnvironmentVariable(name);
        if (string.IsNullOrEmpty(value)) Assert.Ignore($"{name} is not set; this test signs in and needs it.");
        return value!;
    }

    /// <summary>Skips the test when the canvas host is not running.</summary>
    [SetUp]
    public async Task SkipWhenTheCanvasHostIsNotRunningAsync()
    {
        using var probe = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
        try
        {
            var response = await probe.GetAsync($"{CanvasUrl}/_framework/dotnet.js");
            if (!response.IsSuccessStatusCode)
                Assert.Ignore($"The canvas host at {CanvasUrl} answered {(int)response.StatusCode} for its runtime.");
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            Assert.Ignore(
                $"Canvas host not running at {CanvasUrl}. Start it: " +
                "dotnet run --project Z:/_GitHub/VandyBen/Ben.Web.Website.Library.Manage/Messenger/Ben.Wasm.Canvas");
        }

        Page.Response += (_, r) =>
        {
            if (r.Status >= 400 && r.Url.Contains("/_framework/", StringComparison.Ordinal))
                FrameworkFailures.Add($"{r.Status} {r.Url}");
        };
    }

    /// <summary>Fails a test that saw a framework file 404 - the signature of a stale host.</summary>
    [TearDown]
    public void NoFrameworkFileFailedToLoad()
    {
        if (FrameworkFailures.Count > 0)
            Assert.Fail("Framework files failed to load, which leaves the host on its loading ring. Rebuild and restart the host:\n  "
                        + string.Join("\n  ", FrameworkFailures));
    }

    /// <summary>Loads a route and waits for the WebAssembly app to boot.</summary>
    protected async Task GoAsync(string route = "/")
    {
        await Page.GotoAsync($"{CanvasUrl}{route}");
        await Expect(Page.Locator("#app .loading-progress")).ToHaveCountAsync(0, new() { Timeout = 60_000 });
        await Expect(Page.Locator(".bc-editor, .bwc-login").First).ToBeVisibleAsync(new() { Timeout = 60_000 });
    }

    /// <summary>Clears local state (storage, OPFS and the IndexedDB stores) and reloads, so each test starts from an empty board.</summary>
    protected async Task StartCleanAsync()
    {
        await GoAsync();
        await Page.EvaluateAsync(
            """
            async () => {
              localStorage.clear();
              sessionStorage.clear();
              // The one-time storage notice is covered by its own test; here it would sit over what tests touch.
              localStorage.setItem('bc-persist-told', '1');
              try {
                const root = await navigator.storage.getDirectory();
                for await (const name of root.keys()) await root.removeEntry(name, { recursive: true });
              } catch { /* OPFS unavailable - nothing to clear */ }
              for (const name of ['bc-docs', 'bc-assets']) {
                await new Promise(done => { const r = indexedDB.deleteDatabase(name); r.onsuccess = r.onerror = r.onblocked = () => done(); });
              }
            }
            """);
        await Page.ReloadAsync();
        await WaitReadyAsync();
    }

    /// <summary>
    /// Waits until the editor has attached paste, opened the device store and restored the board. Pasting or
    /// asserting a restored board before then races the startup.
    /// </summary>
    protected async Task WaitReadyAsync() =>
        await Expect(Page.Locator(".bc-editor[data-bc-ready='true']")).ToHaveCountAsync(1, new() { Timeout = 60_000 });

    /// <summary>Starts clean and opens a board through the Import file input (works at every screen size).</summary>
    protected async Task ImportBoardAsync(Ben.Canvas.Core.Model.CanvasDocument board)
    {
        var path = Path.Combine(Path.GetTempPath(), $"board-{Guid.NewGuid():N}.ishcanvas");
        await File.WriteAllBytesAsync(path, Ben.Canvas.Core.Persistence.CanvasPackage.Write(board, []));
        await StartCleanAsync();
        await Page.SetInputFilesAsync("#bc-import", path);
        await Expect(Nodes).ToHaveCountAsync(board.Nodes.Count, new() { Timeout = 10_000 });
        File.Delete(path);
    }

    /// <summary>The board's pan and zoom as the gesture script last wrote them.</summary>
    protected Task<double[]> ViewAsync() =>
        Board.EvaluateAsync<double[]>("e => { const s = getComputedStyle(e); return [parseFloat(s.getPropertyValue('--bc-pan-x')) || 0, parseFloat(s.getPropertyValue('--bc-pan-y')) || 0, parseFloat(s.getPropertyValue('--bc-zoom')) || 1] }");

    /// <summary>The world point under a viewport point.</summary>
    protected async Task<(double X, double Y)> WorldAtAsync(double clientX, double clientY)
    {
        var box = (await Board.BoundingBoxAsync())!;
        var v = await ViewAsync();
        return ((clientX - box.X - v[0]) / v[2], (clientY - box.Y - v[1]) / v[2]);
    }
    /// <summary>Signs in through the host's own login page.</summary>
    protected async Task SignInAsync()
    {
        await GoAsync("/login");
        await Page.FillAsync("#email", UserEmail);
        await Page.FillAsync("#password", UserPassword);
        await Page.ClickAsync("button[type='submit']");
        await Expect(Page.Locator(".bc-editor")).ToBeVisibleAsync(new() { Timeout = 60_000 });
    }

    // ── Board helpers ──────────────────────────────────────────────────

    protected ILocator Board => Page.Locator(".bc-board");

    protected ILocator Nodes => Page.Locator(".bc-node");

    /// <summary>Clicks a rail button and waits for the new block.</summary>
    protected async Task<ILocator> AddNodeAsync(string kind)
    {
        var before = await Nodes.CountAsync();
        await Page.ClickAsync($".bc-rail [data-bc-action='add-{kind}']");
        await Expect(Nodes).ToHaveCountAsync(before + 1);
        // Pinned to the block's id: a "selected block" locator would follow the selection to the next block.
        var id = await Page.Locator(".bc-node[data-bc-selected]").Last.GetAttributeAsync("data-bc-node");
        return Page.Locator($".bc-node[data-bc-node='{id}']");
    }

    /// <summary>A mouse drag from the centre of <paramref name="from"/> by a screen delta.</summary>
    protected async Task DragAsync(ILocator from, double dx, double dy, int steps = 12)
    {
        var box = await from.BoundingBoxAsync() ?? throw new InvalidOperationException("Nothing to drag.");
        var x = box.X + box.Width / 2;
        var y = box.Y + Math.Min(16, box.Height / 2);
        await DragFromAsync(x, y, dx, dy, steps);
    }

    protected async Task DragFromAsync(double x, double y, double dx, double dy, int steps = 12)
    {
        await Page.Mouse.MoveAsync((float)x, (float)y);
        await Page.Mouse.DownAsync();
        await Page.Mouse.MoveAsync((float)(x + dx), (float)(y + dy), new() { Steps = steps });
        await Page.Mouse.UpAsync();
        await Page.WaitForTimeoutAsync(100);
    }

    /// <summary>The block's world rectangle, read from its inline style.</summary>
    protected static async Task<(double X, double Y, double W, double H)> WorldRectAsync(ILocator node)
    {
        var r = await node.EvaluateAsync<double[]>("e => [parseFloat(e.style.left), parseFloat(e.style.top), parseFloat(e.style.width), parseFloat(e.style.height)]");
        return (r[0], r[1], r[2], r[3]);
    }

    /// <summary>Waits up to five seconds for a measured value to come within tolerance.</summary>
    protected static async Task EventuallyAsync(Func<Task<double>> read, double expected, double tolerance)
    {
        var last = double.NaN;
        for (var i = 0; i < 50; i++)
        {
            last = await read();
            if (Math.Abs(last - expected) <= tolerance) return;
            await Task.Delay(100);
        }

        Assert.Fail($"Expected {expected} (within {tolerance}) but the value stayed at {last}.");
    }

    protected Task<double> ZoomAsync() =>
        Board.EvaluateAsync<double>("e => parseFloat(getComputedStyle(e).getPropertyValue('--bc-zoom')) || 1");

    protected Task<double> PanXAsync() =>
        Board.EvaluateAsync<double>("e => parseFloat(getComputedStyle(e).getPropertyValue('--bc-pan-x')) || 0");

    /// <summary>A screen point on empty board, away from the empty-state card and controls.</summary>
    protected async Task<(double X, double Y)> EmptySpotAsync(double fx = 0.15, double fy = 0.2)
    {
        var box = await Board.BoundingBoxAsync() ?? throw new InvalidOperationException("No board.");
        return (box.X + box.Width * fx, box.Y + box.Height * fy);
    }

    /// <summary>Waits for network, fonts and images to settle, then saves a screenshot.</summary>
    protected async Task<string> ShotAsync(string name)
    {
        var folder = Environment.GetEnvironmentVariable("BEN_CANVAS_WALK_OUT")
                     ?? Path.Combine(TestContext.CurrentContext.WorkDirectory, "canvas-walk");
        Directory.CreateDirectory(folder);

        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);
        await Page.EvaluateAsync("async () => { await document.fonts.ready; await Promise.all([...document.images].map(i => i.decode().catch(() => {}))); }");

        var path = Path.Combine(folder, $"{name}.png");
        await Page.ScreenshotAsync(new() { Path = path });
        return path;
    }
}
