using Microsoft.Playwright;
using NUnit.Framework;

namespace Ben.Web.Playwright.Tests;

/// <summary>
/// Deleting a case from the SuperAdmin screen (item 183).
/// </summary>
/// <remarks>
/// <para><b>Nothing here presses the button</b>, for the reason the delete-user suite gives: these
/// run against whatever database the suite is pointed at, which has more than once been the live
/// one, and a test that actually deleted a case would be indistinguishable from the accident the
/// screen exists to prevent. What is driven is everything up to the commit — the grid's link in,
/// the preview, the two blocks of counts, and the typed title that gates the delete.</para>
///
/// <para>The delete itself is covered by <c>CasePurgeCoverageTests</c>, which derives the order
/// from the model. What only a browser can show is whether a SuperAdmin is told what they are
/// about to destroy.</para>
/// </remarks>
[TestFixture]
[Category("DeleteCase")]
public class AdminDeleteCaseTests : BenTestBase
{
    [Test]
    public async Task The_page_says_a_group_cannot_do_this()
    {
        await LoginAsync(SuperAdminEmail, SuperAdminPassword);
        await Page.GotoAsync($"{BaseUrl}/admin/delete-case");
        await WaitUntilLoadedAsync();

        await Expect(Page.GetByText("Groups cannot do this", new() { Exact = false }))
            .ToBeVisibleAsync(new() { Timeout = 20_000 });
        await Expect(Page.Locator("#case-picker")).ToBeVisibleAsync(new() { Timeout = 20_000 });
    }

    [Test]
    public async Task Nothing_can_be_deleted_until_a_case_is_chosen()
    {
        await LoginAsync(SuperAdminEmail, SuperAdminPassword);
        await Page.GotoAsync($"{BaseUrl}/admin/delete-case");
        await WaitUntilLoadedAsync();

        // The confirm button does not exist at all until a preview has been loaded — there is
        // nothing on this screen to click through out of habit.
        await Expect(Page.Locator("#confirm-delete-case")).ToHaveCountAsync(0);
    }

    [Test]
    public async Task The_all_cases_grid_links_into_the_delete_screen_with_the_case_chosen()
    {
        await LoginAsync(SuperAdminEmail, SuperAdminPassword);
        await Page.GotoAsync($"{BaseUrl}/admin/cases");
        await WaitUntilLoadedAsync();

        // Found by its tooltip, which is also its accessible name: an icon with no accessible
        // name would pass a screenshot and fail a screen reader.
        var deleteButton = Page.Locator("[title='Delete this case']").First;
        if (await deleteButton.CountAsync() == 0)
        {
            Assert.Ignore("No cases in this database to delete.");
            return;
        }

        await deleteButton.ClickAsync();

        await Expect(Page.Locator("[data-testid='case-purge-preview']"))
            .ToBeVisibleAsync(new() { Timeout = 20_000 });
    }

    [Test]
    public async Task The_preview_separates_what_dies_with_the_case_from_what_survives()
    {
        await LoginAsync(SuperAdminEmail, SuperAdminPassword);
        await Page.GotoAsync($"{BaseUrl}/admin/cases");
        await WaitUntilLoadedAsync();

        var deleteButton = Page.Locator("[title='Delete this case']").First;
        if (await deleteButton.CountAsync() == 0)
        {
            Assert.Ignore("No cases in this database to delete.");
            return;
        }
        await deleteButton.ClickAsync();
        await Expect(Page.Locator("[data-testid='case-purge-preview']"))
            .ToBeVisibleAsync(new() { Timeout = 20_000 });

        var body = await Page.InnerTextAsync("[data-testid='case-purge-preview']");
        Assert.That(body, Does.Contain("would destroy"));
        Assert.That(body, Does.Contain("timeline entries"));

        // And the delete stays dead until the title is typed back exactly.
        var confirm = Page.Locator("#confirm-delete-case");
        await Expect(confirm).ToBeVisibleAsync(new() { Timeout = 10_000 });
        await Expect(confirm).ToBeDisabledAsync();

        await Page.Locator("#confirm-title").FillAsync("not the title");
        await Expect(confirm).ToBeDisabledAsync();
    }

    /// <summary>
    /// The group-facing half of item 183: a case has no delete, and now says so instead of just
    /// not having one.
    /// </summary>
    [Test]
    public async Task A_group_is_told_a_case_is_closed_rather_than_deleted()
    {
        await LoginAsync(SuperAdminEmail, SuperAdminPassword);
        await Page.GotoAsync($"{BaseUrl}/admin/cases");
        await WaitUntilLoadedAsync();

        var open = Page.GetByRole(AriaRole.Button, new() { Name = "Open" }).First;
        if (await open.CountAsync() == 0)
        {
            Assert.Ignore("No cases in this database to open.");
            return;
        }
        await ClickUntilUrlAsync(open, @"/organizations/[0-9a-f\-]+/cases/[0-9a-f\-]+");

        var editBtn = Page.Locator("#case-edit");
        await Expect(editBtn).ToBeVisibleAsync(new() { Timeout = 10_000 });
        await editBtn.ClickAsync();

        await Expect(Page.Locator("[data-testid='cases-are-closed-not-deleted']"))
            .ToBeVisibleAsync(new() { Timeout = 10_000 });
    }
}
