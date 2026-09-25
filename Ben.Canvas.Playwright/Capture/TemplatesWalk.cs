using Microsoft.Playwright;
using Microsoft.Playwright.NUnit;

namespace Ben.Canvas.Playwright.Capture;

/// <summary>
/// A screenshot of every board template, in both themes, exactly as somebody would first see it.
/// </summary>
/// <remarks>
/// <para>The templates are Ben's four pictures made real (2026-09-18), so what they LOOK like is the
/// thing being delivered — and a layout that reads in dark and not in light, or that opens with a
/// panel off the edge of the view, is a defect the unit tests cannot see. They assert what a template
/// contains; this shows it.</para>
///
/// <para>The board is opened by its own link — <c>#template=&lt;id&gt;</c>, signed out, with no case —
/// which is also a check worth having: no sign-in, no server and no case is the leanest path a
/// template can take, and it is the one a first-time visitor takes.</para>
///
/// <para>Opt-in with BEN_CANVAS_WALK=1, like the other walks. The pictures land in
/// BEN_CANVAS_WALK_OUT, and the help images are cut from them.</para>
/// </remarks>
[Category("Capture")]
[NonParallelizable]
public sealed class TemplatesWalk : PageTest
{
    private static string CanvasUrl => (Environment.GetEnvironmentVariable("BEN_CANVAS_URL") ?? "http://localhost:5125").TrimEnd('/');

    /// <summary>Blank has nothing to photograph; the other four are the pictures Ben sent.</summary>
    private static readonly string[] Templates = ["moodboard", "research-plan", "family-tree", "deck"];

    [TestCase("dark")]
    [TestCase("light")]
    public async Task Capture_the_templates(string theme)
    {
        if (Environment.GetEnvironmentVariable("BEN_CANVAS_WALK") != "1")
            Assert.Ignore("Set BEN_CANVAS_WALK=1 to capture the templates walk.");

        var folder = Environment.GetEnvironmentVariable("BEN_CANVAS_WALK_OUT")
                     ?? Path.Combine(TestContext.CurrentContext.WorkDirectory, "canvas-walk");
        Directory.CreateDirectory(folder);

        var found = new List<string>();
        var empty = new List<string>();

        await using var context = await Browser.NewContextAsync(new()
        {
            ViewportSize = new() { Width = 1440, Height = 900 },
            DeviceScaleFactor = 1,
            ColorScheme = theme == "dark" ? ColorScheme.Dark : ColorScheme.Light,
        });
        await context.AddInitScriptAsync($"localStorage.setItem('ben-theme', '{theme}'); localStorage.setItem('layoutSettings', JSON.stringify({{ theme: '{theme}' }}));");

        foreach (var id in Templates)
        {
            // A page of its own each time: a template is what somebody sees on a FIRST load, and
            // reusing one page would carry the last board's device copy into the next shot.
            var page = await context.NewPageAsync();
            await page.GotoAsync($"{CanvasUrl}/#template={id}");

            var board = page.Locator(".bc-board");
            await board.WaitForAsync(new() { Timeout = 60_000 });
            await page.Locator("[data-bc-ready=true]").WaitForAsync(new() { Timeout = 60_000 });

            // Frame it the way the editor does on a new board, then let the fonts land.
            await board.FocusAsync();
            await page.Keyboard.PressAsync("Home");
            await page.EvaluateAsync("async () => { await document.fonts.ready; }");
            await page.WaitForTimeoutAsync(500);

            var blocks = await page.Locator(".bc-node").CountAsync();
            var groups = await page.Locator(".bc-group").CountAsync();
            var title = await page.Locator(".bc-header__title").InnerTextAsync();

            found.Add($"{id}: {blocks} block(s), {groups} panel(s), titled \"{title}\"");

            // An empty board here means the template did not arrive — the failure the fragment race
            // produced on 2026-09-18, and one a picture would not obviously show.
            if (blocks == 0 && groups == 0) empty.Add(id);

            await page.ScreenshotAsync(new() { Path = Path.Combine(folder, $"m9-template-{id}-{theme}.png") });
            await page.CloseAsync();
        }

        File.WriteAllLines(Path.Combine(folder, $"m9-templates-{theme}.txt"), found);
        foreach (var line in found) TestContext.Out.WriteLine(line);

        Assert.That(empty, Is.Empty, "these templates opened with nothing on them: " + string.Join(", ", empty));
    }
}
