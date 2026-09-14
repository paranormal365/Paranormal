using System.Text.Json;
using Microsoft.Playwright;
using NUnit.Framework;

namespace Ben.Web.Playwright.Tests;

/// <summary>
/// A grid's action buttons sit inside an ordinary table cell, so the line under a row runs the whole way across.
/// </summary>
/// <remarks>
/// <para>
/// Beta feedback, 2026-09-14: on /admin/org-subscriptions the border under each row stopped where the Actions
/// column began. The cause was one theme rule making every <c>td.k-command-cell</c> <c>display: flex</c>: a flex
/// <c>&lt;td&gt;</c> is no longer a table cell, its own box is only as tall as its buttons, and its bottom border
/// sits above the rest of the row's. Every grid with a command column had it; this page was where it showed.
/// </para>
/// <para>
/// Measured rather than screenshotted: the cell must be a table cell, reach the bottom of its row, and hold every
/// one of its buttons inside its box (so the fix cannot trade a broken line for a clipped Delete — item 139).
/// </para>
/// </remarks>
[TestFixture]
[Category("Admin")]
public class GridCommandCellTests : BenTestBase
{
    private static readonly string[] Pages =
    [
        "/admin/org-subscriptions",
        "/admin/file-types",
        "/admin/users",
        "/admin/subscription-tiers",
    ];

    private sealed record CellMeasure(string Display, double CellBottom, double RowBottom, int Buttons, int Outside);

    [Test]
    public async Task CommandCells_AreTableCells_ThatReachTheBottomOfTheirRow()
    {
        await LoginAsync(SuperAdminEmail, SuperAdminPassword);

        var problems = new List<string>();
        var measured = 0;

        foreach (var path in Pages)
        {
            await Page.GotoAsync($"{BaseUrl}{path}", new() { Timeout = 25_000 });
            await WaitForTheCircuitAsync();
            await WaitUntilLoadedAsync();

            var cell = Page.Locator(".k-grid td.k-command-cell").First;
            try
            {
                await cell.WaitForAsync(new() { Timeout = 10_000 });
            }
            catch (TimeoutException)
            {
                // A grid with no rows has no command cell to measure; the other pages still do.
                continue;
            }

            var json = await cell.EvaluateAsync<string>(@"td => {
                const row = td.closest('tr');
                const c = td.getBoundingClientRect();
                const r = row.getBoundingClientRect();
                const buttons = [...td.querySelectorAll('.k-button')];
                const outside = buttons.filter(b => {
                    const x = b.getBoundingClientRect();
                    return x.left < c.left - 1 || x.right > c.right + 1 || x.top < c.top - 1 || x.bottom > c.bottom + 1;
                }).length;
                return JSON.stringify({ display: getComputedStyle(td).display, cellBottom: c.bottom,
                    rowBottom: r.bottom, buttons: buttons.length, outside });
            }");
            var m = JsonSerializer.Deserialize<CellMeasure>(json, new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
            measured++;

            if (m.Display != "table-cell")
                problems.Add($"{path}: command cell is display:{m.Display}, not a table cell");
            if (Math.Abs(m.CellBottom - m.RowBottom) > 1)
                problems.Add($"{path}: command cell ends at {m.CellBottom:0.#}px but its row at {m.RowBottom:0.#}px");
            if (m.Buttons == 0)
                problems.Add($"{path}: command cell holds no buttons");
            if (m.Outside > 0)
                problems.Add($"{path}: {m.Outside} of {m.Buttons} buttons spill outside their cell");
        }

        Assert.That(measured, Is.GreaterThanOrEqualTo(2),
            "Fewer than two of the grids had a row to measure, so this test proves nothing about the theme rule.");
        Assert.That(problems, Is.Empty, string.Join("\n", problems));
    }
}

/// <summary>
/// Row actions are icons whose words appear as a tooltip on hover and are the button's accessible name.
/// </summary>
/// <remarks>
/// Ben, 2026-09-14: "don't use text in buttons in a grid. Use icon buttons with a tooltip." The tooltip is one
/// TelerikTooltip in the layout, delegated to <c>.ben-grid-action[title]</c>; this proves it reaches rows a grid
/// rendered after the page had already loaded, which is the part a per-page check in the browser would not.
/// </remarks>
[TestFixture]
[Category("Admin")]
public class GridActionTooltipTests : BenTestBase
{
    [Test]
    public async Task HoveringAnIconAction_ShowsItsWords_AndTheWordsAreItsName()
    {
        await LoginAsync(SuperAdminEmail, SuperAdminPassword);
        await Page.GotoAsync($"{BaseUrl}/admin/file-types", new() { Timeout = 25_000 });
        await WaitForTheCircuitAsync();
        await WaitUntilLoadedAsync();

        var edit = Page.Locator(".k-grid").GetByRole(AriaRole.Button, new() { Name = "Edit", Exact = true }).First;
        await Expect(edit).ToBeVisibleAsync(new() { Timeout = 15_000 });

        Assert.That((await edit.InnerTextAsync()).Trim(), Is.Empty, "the Edit action still shows words instead of an icon");
        await Expect(edit.Locator("svg")).ToHaveCountAsync(1);

        // Hover as a person does — move onto it, and off and on again if nothing showed. The tooltip's listener is attached
        // after the grid is drawn, and a pointer already resting on the button when it attaches never "enters" it: the test
        // hovered once, the moment the rows appeared, and failed five runs in six on unchanged code (2026-09-14).
        var tip = Page.Locator(".k-tooltip:visible");
        for (var attempt = 0; attempt < 5 && await tip.CountAsync() == 0; attempt++)
        {
            await Page.Mouse.MoveAsync(0, 0);
            await edit.HoverAsync();
            try { await Expect(tip).ToBeVisibleAsync(new() { Timeout = 1_500 }); }
            catch (PlaywrightException) { /* not yet attached — move off and back */ }
        }
        await Expect(tip).ToBeVisibleAsync(new() { Timeout = 1_000 });
        await Expect(tip).ToContainTextAsync("Edit");
    }
}
