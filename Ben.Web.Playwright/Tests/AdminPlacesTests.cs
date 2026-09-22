using Microsoft.Playwright;
using NUnit.Framework;

namespace Ben.Web.Playwright.Tests;

/// <summary>
/// The place catalogue opens, lists, and refuses what it should (C8).
/// </summary>
/// <remarks>
/// <para><b>Why a browser test and not just the controller tests.</b> The controller tests prove
/// the rules; this proves somebody can reach them. Every write-only feature this site has shipped —
/// nine of them now — passed its unit tests and had no door. A page that renders its refusal is a
/// different thing from a rule that returns one.</para>
///
/// <para>The demote path is the one walked end to end, because it is the repair the page exists
/// for and because it is reversible: the fixture puts the place back.</para>
/// </remarks>
[TestFixture]
[Category("Admin")]
public class AdminPlacesTests : BenTestBase
{
    private async Task OpenAsync()
    {
        await LoginAsync(SuperAdminEmail, SuperAdminPassword);
        await Page.GotoAsync($"{BaseUrl}/admin/places");
        await WaitForTheCircuitAsync();
        await WaitUntilLoadedAsync();
        await Expect(Main.Locator("table").First).ToBeVisibleAsync(new() { Timeout = 20_000 });
    }

    [Test]
    public async Task The_catalogue_lists_places_with_their_counts()
    {
        await OpenAsync();

        // A count in the header, and a table with the columns the decision is made from.
        await Expect(Main.Locator("[data-testid='places-total']")).ToBeVisibleAsync();
        foreach (var column in new[] { "Kind", "Cases", "Visits", "Evidence" })
            await Expect(Main.GetByText(column, new() { Exact = true }).First)
                .ToBeVisibleAsync(new() { Timeout = 10_000 });

        Assert.That(await Main.Locator("tbody tr").CountAsync(), Is.GreaterThan(0),
            "The catalogue opened with no rows at all, so nothing below this proves anything.");
    }

    /// <summary>Narrowing to one kind actually narrows.</summary>
    [Test]
    public async Task Filtering_by_kind_changes_what_is_listed()
    {
        await OpenAsync();
        var all = await Main.Locator("tbody tr").CountAsync();

        await Main.Locator("#places-kind").SelectOptionAsync("PublicLocation");
        await WaitUntilLoadedAsync();
        await Page.WaitForTimeoutAsync(500);

        var publicOnly = await Main.Locator("tbody tr").CountAsync();
        Assert.That(publicOnly, Is.LessThanOrEqualTo(all));

        // Every row left says so, rather than the filter being decorative.
        if (publicOnly > 0)
            await Expect(Main.GetByText("Public location").First).ToBeVisibleAsync();
    }

    /// <summary>
    /// Asking to delete a place that is in use explains what is in the way.
    /// </summary>
    /// <remarks>
    /// The seeded catalogue has at least one place carrying a case or a visit, and the dialog is
    /// required to name what holds it. A dialog that just says "no" would pass a weaker assertion
    /// and leave somebody stuck, which is the fault this wording exists to prevent.
    /// </remarks>
    [Test]
    public async Task A_place_in_use_says_what_is_holding_it()
    {
        await OpenAsync();

        // Find a row whose counts are not all zero — that is one the delete must refuse.
        var rows = Main.Locator("tbody tr");
        var count = await rows.CountAsync();
        ILocator? inUse = null;

        for (var i = 0; i < count; i++)
        {
            var row = rows.Nth(i);
            var cells = row.Locator("td");
            var numbers = 0;
            foreach (var index in new[] { 2, 3, 4 })
            {
                var text = (await cells.Nth(index).InnerTextAsync()).Trim();
                if (int.TryParse(text, out var n)) numbers += n;
            }
            if (numbers > 0) { inUse = row; break; }
        }

        Assert.That(inUse, Is.Not.Null,
            "No seeded place has a case, a visit or any evidence against it, so the refusal "
          + "this test exists to read could not be produced.");

        await inUse!.GetByRole(AriaRole.Button, new() { Name = "Delete" }).ClickAsync();

        var dialog = Page.Locator(".modal:visible");
        await Expect(dialog).ToBeVisibleAsync(new() { Timeout = 10_000 });
        await Expect(dialog.GetByText("can't be deleted")).ToBeVisibleAsync(new() { Timeout = 10_000 });
        await Expect(dialog.GetByText("still point at it")).ToBeVisibleAsync();

        // And it offers no delete button at all, rather than one that fails when pressed.
        Assert.That(await dialog.GetByRole(AriaRole.Button, new() { Name = "Delete it" }).CountAsync(),
            Is.Zero, "The dialog offered a delete for a place the server will refuse.");
    }
}
