using Microsoft.Playwright;

namespace Ben.Canvas.Playwright.Touch;

/// <summary>
/// Real multi-touch through the Chrome DevTools Protocol: taps, one-finger drags, long presses and pinches.
/// </summary>
/// <remarks>
/// Playwright's own Touchscreen can only tap. CDP's Input.dispatchTouchEvent drives the same touch pipeline a
/// phone does, including two fingers at once, which is the only way to prove pinch and the "second finger
/// cancels a drag" rule. Chromium only: the constructor ignores the test on any other engine.
/// </remarks>
public sealed class TouchDriver
{
    private readonly ICDPSession _cdp;
    private readonly IPage _page;

    private TouchDriver(IPage page, ICDPSession cdp)
    {
        _page = page;
        _cdp = cdp;
    }

    public static async Task<TouchDriver> StartAsync(IPage page, IBrowser browser)
    {
        if (browser.BrowserType.Name != "chromium") Assert.Ignore("CDP touch is Chromium-only.");
        return new TouchDriver(page, await page.Context.NewCDPSessionAsync(page));
    }

    private Task SendAsync(string type, params (double X, double Y, int Id)[] points) =>
        _cdp.SendAsync("Input.dispatchTouchEvent", new Dictionary<string, object>
        {
            ["type"] = type,
            ["touchPoints"] = points.Select(p => new Dictionary<string, object> { ["x"] = p.X, ["y"] = p.Y, ["id"] = p.Id, ["radiusX"] = 4, ["radiusY"] = 4, ["force"] = 1 }).ToArray(),
        });

    public async Task TapAsync(double x, double y)
    {
        await SendAsync("touchStart", (x, y, 1));
        await SendAsync("touchEnd");
        await _page.WaitForTimeoutAsync(60);
    }

    public async Task LongPressAsync(double x, double y, int milliseconds)
    {
        await SendAsync("touchStart", (x, y, 1));
        await _page.WaitForTimeoutAsync(milliseconds);
        await SendAsync("touchEnd");
        await _page.WaitForTimeoutAsync(80);
    }

    public async Task DragAsync(double x0, double y0, double x1, double y1, int steps = 12)
    {
        await SendAsync("touchStart", (x0, y0, 1));
        for (var i = 1; i <= steps; i++)
        {
            var t = (double)i / steps;
            await SendAsync("touchMove", (x0 + (x1 - x0) * t, y0 + (y1 - y0) * t, 1));
        }

        await SendAsync("touchEnd");
        await _page.WaitForTimeoutAsync(120);
    }

    /// <summary>Two fingers either side of a centre, moving from one distance apart to another.</summary>
    public async Task PinchAsync(double cx, double cy, double startDistance, double endDistance, int steps = 12)
    {
        var h = startDistance / 2;
        await SendAsync("touchStart", (cx - h, cy, 1), (cx + h, cy, 2));
        for (var i = 1; i <= steps; i++)
        {
            var d = (startDistance + (endDistance - startDistance) * i / steps) / 2;
            await SendAsync("touchMove", (cx - d, cy, 1), (cx + d, cy, 2));
        }

        await SendAsync("touchEnd");
        await _page.WaitForTimeoutAsync(150);
    }

    /// <summary>Starts a one-finger drag, then puts a second finger down and pinches before lifting both.</summary>
    public async Task DragThenPinchAsync(double x, double y, double dragBy, double cx, double cy)
    {
        await SendAsync("touchStart", (x, y, 1));
        for (var i = 1; i <= 8; i++) await SendAsync("touchMove", (x + dragBy * i / 8, y, 1));
        await SendAsync("touchStart", (x + dragBy, y, 1), (cx + 60, cy, 2));
        for (var i = 1; i <= 8; i++) await SendAsync("touchMove", (x + dragBy - i * 6, y, 1), (cx + 60 + i * 6, cy, 2));
        await SendAsync("touchEnd");
        await _page.WaitForTimeoutAsync(150);
    }
}
