using Microsoft.Playwright;
using NUnit.Framework;

namespace Ben.Web.Playwright.Tests;

/// <summary>
/// The users grid says who is signing in and how often (Ben, 2026-09-19).
/// </summary>
/// <remarks>
/// The columns come from a second call — one grouped query over SignInEvents — paired with the
/// accounts in the browser. That pairing is the part worth a test: a mismatched key would leave
/// every row reading "Never" and look exactly like a site nobody uses.
/// </remarks>
[TestFixture]
[Category("Admin")]
public class AdminUserSignInColumnsTests : BenTestBase
{
    private async Task OpenTheUsersGridAsync()
    {
        await LoginAsync(SuperAdminEmail, SuperAdminPassword);
        await Page.GotoAsync($"{BaseUrl}/admin/users");
        await WaitUntilLoadedAsync();
        await Expect(Main.Locator("table").First).ToBeVisibleAsync(new() { Timeout = 20_000 });
    }

    [Test]
    public async Task The_grid_carries_both_sign_in_columns()
    {
        await OpenTheUsersGridAsync();

        await Expect(Main.GetByText("Last sign-in", new() { Exact = true }).First)
            .ToBeVisibleAsync(new() { Timeout = 15_000 });
        await Expect(Main.GetByText("Sign-ins", new() { Exact = true }).First)
            .ToBeVisibleAsync(new() { Timeout = 15_000 });
    }

    /// <summary>
    /// The account this test signs in with has, by definition, just signed in — so at least one
    /// row must show a count rather than every row reading "Never".
    /// </summary>
    /// <remarks>
    /// This is the assertion that catches the failure mode the pairing invites. The summary is
    /// keyed by account id and joined in the browser; key it wrongly, or drop the call, and the
    /// page still renders perfectly with every row saying nobody has ever signed in.
    /// </remarks>
    [Test]
    public async Task Signing_in_is_counted_and_shown()
    {
        await OpenTheUsersGridAsync();

        // "Never" is correct for accounts nobody has used; it cannot be correct for EVERY row
        // when the person reading the page is signed in.
        var never = Main.GetByText("Never", new() { Exact = true });
        var rows  = Main.Locator("tbody tr");

        var rowCount   = await rows.CountAsync();
        var neverCount = await never.CountAsync();
        Assert.That(rowCount, Is.GreaterThan(0), "the users grid drew no rows at all");
        Assert.That(neverCount, Is.LessThan(rowCount),
            "every row says Never — the sign-in figures are not reaching the grid");
    }

    /// <summary>
    /// Sortable, because "who is signing in" and "how often" are orderings rather than lookups —
    /// and a Telerik column only sorts when it is bound to a field on the item the grid holds,
    /// which is why the grid moved to a row type rather than gaining two template columns.
    /// </summary>
    /// <remarks>
    /// <para>Asserted on the ORDER, not on the header's markup. A first version read the
    /// heading's <c>aria-sort</c> and failed while the column sorted perfectly well — it was
    /// testing how Kendo decorates a header, which is not the claim and is not ours to depend
    /// on.</para>
    ///
    /// <para>"Display Name" is clicked first as a control. It has sorted since this page existed,
    /// so if the reading below cannot see IT change either, the test is broken rather than the
    /// column — and it says so instead of blaming the new work.</para>
    /// </remarks>
    [Test]
    public async Task Both_columns_sort()
    {
        await OpenTheUsersGridAsync();

        var control = await SortingChangesTheOrderAsync("Display Name");
        Assert.That(control.Reordered, Is.True,
            "even the long-standing Display Name column did not reorder — this test cannot see "
          + $"sorting at all.\n  up:   {control.Ascending}\n  down: {control.Descending}");

        foreach (var heading in new[] { "Last sign-in", "Sign-ins" })
        {
            var read = await SortingChangesTheOrderAsync(heading);

            // The readings are reported, because "did not reorder" has two very different causes
            // and the old message could not tell them apart: a column that will not sort, and a
            // column where every value is the same so there is nothing to reorder.
            Assert.That(read.Reordered, Is.True,
                $"the {heading} column did not reorder the grid when its heading was pressed."
              + $"\n  up:   {read.Ascending}\n  down: {read.Descending}");
        }
    }

    /// <summary>
    /// Sorts a column each way round and answers whether the rows actually moved.
    /// </summary>
    /// <remarks>
    /// <para><b>Driven to a named direction, not pressed a fixed number of times.</b> The first
    /// version pressed the heading twice, on the assumption that one press sorts ascending and
    /// the next descending. Kendo's cycle has THREE states — ascending, descending, and unsorted
    /// — so where two presses land depends on where the column already was, and for the Sign-ins
    /// column they landed on descending and then unsorted. Unsorted restores the order the grid
    /// was already in, so both readings were identical and the test reported a column that would
    /// not sort. It sorted perfectly well (2026-09-21).</para>
    ///
    /// <para>The other two columns passed on the same flawed method, by luck of where they
    /// happened to be in the cycle — which is why this is fixed rather than special-cased.</para>
    /// </remarks>
    private async Task<(bool Reordered, string Ascending, string Descending)> SortingChangesTheOrderAsync(string heading)
    {
        var header = Main.Locator("th", new() { HasTextString = heading }).First;
        await Expect(header).ToBeVisibleAsync(new() { Timeout = 10_000 });

        var up   = await SortedTopRowAsync(header, "ascending");
        var down = await SortedTopRowAsync(header, "descending");

        return (up != down,
                $"{up}   [{heading}, ascending]",
                $"{down}   [{heading}, descending]");
    }

    /// <summary>
    /// Presses a heading until it is sorted the named way, then reads the top row.
    /// </summary>
    /// <remarks>
    /// At most four presses: three is a whole cycle, and a fourth means the column does not
    /// answer to being pressed at all — which is a real failure and is reported as one rather
    /// than looped on.
    /// </remarks>
    private async Task<string> SortedTopRowAsync(ILocator header, string direction)
    {
        for (var press = 0; press < 4; press++)
        {
            if (await header.GetAttributeAsync("aria-sort") == direction) return await FirstRowTextAsync();
            await header.ClickAsync();
            await WaitUntilLoadedAsync();
        }

        Assert.Fail($"the heading would not sort {direction} after a full cycle of presses — "
                  + $"it reports aria-sort={await header.GetAttributeAsync("aria-sort") ?? "(none)"}");
        return string.Empty;
    }

    private async Task<string> FirstRowTextAsync()
    {
        var first = Main.Locator("tbody tr").First;
        await Expect(first).ToBeVisibleAsync(new() { Timeout = 10_000 });
        return (await first.InnerTextAsync()).Trim();
    }
}
