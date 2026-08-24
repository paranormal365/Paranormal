using Microsoft.Playwright;
using NUnit.Framework;

namespace Ben.Web.Playwright.Tests;

/// <summary>
/// Item 176: the case-title leak warning, walked through the real Edit Case dialog. The unit
/// tests prove the check; this proves the UI actually shows the sentence and that a second Save
/// still publishes — a warning the dialog discarded would be the sixth instance of the
/// server-guard-with-no-UI-path bug, so the UI path is the thing to verify.
/// </summary>
[TestFixture]
[Category("PublishLeakWarning")]
public class PublishLeakWarningTests : BenTestBase
{
    [Test]
    public async Task Publishing_a_surname_title_warns_once_then_publishes_on_the_second_save()
    {
        await LoginAsync(UserEmail, UserPassword); // Sarah, TGH member
        if (!await OpenOrgCaseAsync("Tennessee Ghost Hunters", "Park"))
            Assert.Pass("TGH Park case not in the seed data; nothing to walk.");

        await Page.GetByRole(AriaRole.Button, new() { Name = "Edit Case" }).ClickAsync();
        var dialog = Page.Locator(".modal").Filter(new() { HasTextString = "Edit Case" }).First;
        var titleInput = dialog.Locator("#casedetail-case-label-surname-city-0146");
        await Expect(titleInput).ToBeVisibleAsync(new() { Timeout = 10_000 });

        // ENSURE the state the test needs, and remember what to put back — shared DB.
        var originalTitle = await titleInput.InputValueAsync();
        var makePublic = dialog.Locator("#case-public");
        var wasPublic = await makePublic.IsCheckedAsync();

        try
        {
            await titleInput.FillAsync("Park Residence, Nashville TN");
            if (!wasPublic) await makePublic.CheckAsync();

            var save = dialog.GetByRole(AriaRole.Button, new() { Name = "Save" });
            await save.ClickAsync();

            // First save: the warning, not the save. The sentence names what leaked.
            var warning = Page.Locator("#case-title-leak-warning");
            await Expect(warning).ToBeVisibleAsync(new() { Timeout = 10_000 });
            await Expect(warning).ToContainTextAsync("\"Park\"");
            await Expect(titleInput).ToBeVisibleAsync(); // dialog still open — nothing saved

            // Second save on the same text: warn-not-block means this one goes through.
            await save.ClickAsync();
            await Expect(titleInput).ToBeHiddenAsync(new() { Timeout = 10_000 });
        }
        finally
        {
            // Restore: reopen the dialog and put the original title and visibility back.
            var edit = Page.GetByRole(AriaRole.Button, new() { Name = "Edit Case" });
            if (await edit.CountAsync() > 0)
            {
                await edit.ClickAsync();
                await Expect(titleInput).ToBeVisibleAsync(new() { Timeout = 10_000 });
                await titleInput.FillAsync(originalTitle);
                if (!wasPublic) await makePublic.UncheckAsync();
                await dialog.GetByRole(AriaRole.Button, new() { Name = "Save" }).ClickAsync();
                await Expect(titleInput).ToBeHiddenAsync(new() { Timeout = 10_000 });
            }
        }
    }

    [Test]
    public async Task A_place_named_title_saves_public_with_no_warning_at_all()
    {
        await LoginAsync(UserEmail, UserPassword);
        if (!await OpenOrgCaseAsync("Tennessee Ghost Hunters", "Park"))
            Assert.Pass("TGH Park case not in the seed data; nothing to walk.");

        await Page.GetByRole(AriaRole.Button, new() { Name = "Edit Case" }).ClickAsync();
        var dialog = Page.Locator(".modal").Filter(new() { HasTextString = "Edit Case" }).First;
        var titleInput = dialog.Locator("#casedetail-case-label-surname-city-0146");
        await Expect(titleInput).ToBeVisibleAsync(new() { Timeout = 10_000 });

        var originalTitle = await titleInput.InputValueAsync();
        var makePublic = dialog.Locator("#case-public");
        var wasPublic = await makePublic.IsCheckedAsync();
        var pseudonym = dialog.Locator("#casedetail-public-pseudonym-88c6");
        var originalPseudonym = await pseudonym.InputValueAsync();

        try
        {
            // A clean title AND a clean pseudonym — the seeded pseudonym may carry the surname.
            await titleInput.FillAsync("The Belmont Farmhouse");
            await pseudonym.FillAsync("The Hargrove Family");
            if (!wasPublic) await makePublic.CheckAsync();

            await dialog.GetByRole(AriaRole.Button, new() { Name = "Save" }).ClickAsync();

            // No warning stop: the dialog just closes on the first save.
            await Expect(titleInput).ToBeHiddenAsync(new() { Timeout = 10_000 });
            Assert.That(await Page.Locator("#case-title-leak-warning").CountAsync(), Is.EqualTo(0));
        }
        finally
        {
            var edit = Page.GetByRole(AriaRole.Button, new() { Name = "Edit Case" });
            if (await edit.CountAsync() > 0)
            {
                await edit.ClickAsync();
                await Expect(titleInput).ToBeVisibleAsync(new() { Timeout = 10_000 });
                await titleInput.FillAsync(originalTitle);
                await pseudonym.FillAsync(originalPseudonym);
                if (!wasPublic) await makePublic.UncheckAsync();
                await dialog.GetByRole(AriaRole.Button, new() { Name = "Save" }).ClickAsync();
                await Expect(titleInput).ToBeHiddenAsync(new() { Timeout = 10_000 });
            }
        }
    }
}
