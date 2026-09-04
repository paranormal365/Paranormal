using Microsoft.Playwright;
using NUnit.Framework;

namespace Ben.Web.Playwright.Tests;

/// <summary>
/// Deleting a person from the SuperAdmin screen.
/// </summary>
/// <remarks>
/// <para><b>Nothing here presses the button.</b> These tests run against whatever database the
/// suite is pointed at — which has more than once been the live one — and a test that actually
/// deleted somebody would be indistinguishable from the accident this screen exists to prevent.
/// What is driven is everything up to the commit: the button on the users grid, the preview it
/// opens, the two columns of counts, the sentence about whether the row survives, and the typed
/// confirmation that gates the delete.</para>
///
/// <para>That is also where the bugs live. The purge itself is covered by unit and model tests;
/// what only a browser can show is whether a SuperAdmin is actually told what they are about to
/// do.</para>
/// </remarks>
[TestFixture]
[Category("DeleteUser")]
public class AdminDeleteUserTests : BenTestBase
{
    [Test]
    public async Task The_users_grid_has_a_delete_button_that_opens_the_delete_screen()
    {
        await LoginAsync(SuperAdminEmail, SuperAdminPassword);
        await Page.GotoAsync($"{BaseUrl}/admin/users");
        await WaitUntilLoadedAsync();

        // Ben asked for a small trash button on the row, not a word. Found by its tooltip, which
        // is also its accessible name — an icon with no accessible name would pass a screenshot
        // and fail a screen reader.
        var deleteButton = Page.Locator("[title='Delete this user']").First;
        await Expect(deleteButton).ToBeVisibleAsync(new() { Timeout = 20_000 });

        await deleteButton.ClickAsync();

        await Expect(Page.Locator("[data-testid='purge-preview']"))
            .ToBeVisibleAsync(new() { Timeout = 20_000 });
    }

    [Test]
    public async Task The_preview_separates_what_is_destroyed_from_what_is_kept()
    {
        await LoginAsync(SuperAdminEmail, SuperAdminPassword);
        await Page.GotoAsync($"{BaseUrl}/admin/users");
        await WaitUntilLoadedAsync();

        await Page.Locator("[title='Delete this user']").First.ClickAsync();
        var preview = Page.Locator("[data-testid='purge-preview']");
        await Expect(preview).ToBeVisibleAsync(new() { Timeout = 20_000 });

        var text = await preview.InnerTextAsync();

        // The counts are the whole safety of the screen. A preview that rendered its frame and no
        // numbers would look fine and tell a SuperAdmin nothing.
        Assert.That(text, Does.Contain("would destroy"));
        Assert.That(text, Does.Contain("personal field sessions"));
        Assert.That(text, Does.Contain("memberships"));

        // And the honest sentence: which of the two outcomes this will be.
        await Expect(Page.Locator("[data-testid='row-outcome']")).ToBeVisibleAsync();
        var outcome = await Page.Locator("[data-testid='row-outcome']").InnerTextAsync();
        Assert.That(outcome,
            Does.Contain("removed completely").Or.Contain("will remain, emptied"),
            "the screen must say, before the button, whether the account row itself survives");
    }

    [Test]
    public async Task The_delete_button_stays_dead_until_the_name_is_typed_exactly()
    {
        await LoginAsync(SuperAdminEmail, SuperAdminPassword);
        await Page.GotoAsync($"{BaseUrl}/admin/users");
        await WaitUntilLoadedAsync();

        await Page.Locator("[title='Delete this user']").First.ClickAsync();
        await Expect(Page.Locator("[data-testid='purge-preview']"))
            .ToBeVisibleAsync(new() { Timeout = 20_000 });

        // A refusal screen has no confirm box at all, which is correct and would make the rest of
        // this test meaningless rather than failing. Skip rather than assert something false.
        if (await Page.Locator("[data-testid='refusal']").CountAsync() > 0)
            Assert.Ignore("the first row is the last SuperAdmin, which is refused by design");

        var confirm = Page.Locator("#confirm-delete");
        await Expect(confirm).ToBeDisabledAsync();

        await Page.Locator("#confirm-name").FillAsync("definitely not the right name");
        await Expect(confirm).ToBeDisabledAsync();

        // The one confirmation that cannot be clicked through out of habit. Nothing here presses
        // it — this only proves it becomes pressable for the right name and not for a wrong one.
        var expected = await Page.Locator("#confirm-name").GetAttributeAsync("placeholder");
        await Page.Locator("#confirm-name").FillAsync(expected!);
        await Expect(confirm).ToBeEnabledAsync();
    }

    [Test]
    public async Task A_regular_user_cannot_open_the_delete_screen()
    {
        await LoginAsync(UserEmail, UserPassword);
        await Page.GotoAsync($"{BaseUrl}/admin/delete-user");
        await WaitUntilLoadedAsync();

        var body = await Main.InnerTextAsync();
        Assert.That(body, Does.Not.Contain("would destroy"),
            "a regular user reached the screen that deletes people");
        Assert.That(await Page.Locator("[data-testid='purge-preview']").CountAsync(), Is.EqualTo(0));
    }
}
