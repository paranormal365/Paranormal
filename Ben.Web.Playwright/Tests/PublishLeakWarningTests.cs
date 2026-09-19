using Microsoft.Playwright;
using NUnit.Framework;

namespace Ben.Web.Playwright.Tests;

/// <summary>
/// Item 176: the case-title leak warning, walked through the real Edit Case page. The unit
/// tests prove the check; this proves the UI actually shows the sentence and that a second Save
/// still publishes — a warning the dialog discarded would be the sixth instance of the
/// server-guard-with-no-UI-path bug, so the UI path is the thing to verify.
/// </summary>
// Fixtures that drive the Edit Case page on the one seeded case, or upload to it, cannot run
// beside each other: each one changes the case, asserts, and restores, and in parallel one
// fixture's restore lands in the middle of another's assertion. The 2026-09-07 full run failed
// The_leak_warning_fires_before_save_not_after_it exactly that way, having passed twice in
// isolation and once beside PublishLeakWarningTests. NonParallelizable is what this suite already
// uses for shared seeded state — a dozen fixtures carry it for the same reason.
[TestFixture]
[Category("PublishLeakWarning")]
[NonParallelizable]
public class PublishLeakWarningTests : BenTestBase
{
    // Resolved from the seed, not hardcoded: the 2026-08-26 database rebuild regenerated every
    // org id, and a pasted GUID pins a test to one database that no longer exists.
    private string TghId = "";

    [SetUp]
    public async Task ResolveTgh() => TghId = await OrgIdBySlugAsync("paranormal365");

    // The Edit Case form is a page of its own since 2026-09-14 (it was a dialog). Its fields kept their ids.
    private ILocator TitleInput => Page.Locator("#casedetail-case-label-surname-city-0146");
    private ILocator MakePublic => Page.Locator("#case-public");
    private ILocator Pseudonym  => Page.Locator("#casedetail-public-pseudonym-88c6");
    private ILocator SaveButton => Page.Locator("#case-edit-save");

    /// <summary>Opens the Edit Case page from the case and returns its address, for the restore to go straight back to.</summary>
    private async Task<string> OpenEditPageAsync()
    {
        await ClickUntilUrlAsync(Page.Locator("#case-edit"), @"/cases/[0-9a-f\-]+/edit$");
        await Expect(TitleInput).ToBeVisibleAsync(new() { Timeout = 15_000 });
        return Page.Url;
    }

    /// <summary>
    /// Saved means the case page is showing again — its Edit Case button drawn — which only happens after the server
    /// said yes. Waiting on the address alone let a restore run while the case page was still loading.
    /// </summary>
    private async Task ExpectSavedAsync()
    {
        await Expect(Page).ToHaveURLAsync(new System.Text.RegularExpressions.Regex(@"/cases/[0-9a-f\-]+$"), new() { Timeout = 15_000 });
        await Expect(Page.Locator("#case-edit")).ToBeVisibleAsync(new() { Timeout = 15_000 });
    }

    /// <summary>
    /// Puts the seeded case back. Goes to the edit page by address rather than through the case page, and never skips:
    /// a restore that quietly did nothing renamed the shared case for every later run (2026-09-14).
    /// </summary>
    private async Task RestoreAsync(string editUrl, string title, string? pseudonym, bool isPublic)
    {
        await Page.GotoAsync(editUrl);
        await WaitForTheCircuitAsync();
        await Expect(TitleInput).ToBeVisibleAsync(new() { Timeout = 15_000 });

        await TitleInput.FillAsync(title);
        if (pseudonym is not null) await Pseudonym.FillAsync(pseudonym);
        await MakePublic.SetCheckedAsync(isPublic);
        await SaveButton.ClickAsync();

        // Either it saves, or the original label is itself one the check warns about and a second Save publishes it.
        var warning = Page.Locator("#case-title-leak-warning");
        await Expect(warning.Or(Page.Locator("#case-edit"))).ToBeVisibleAsync(new() { Timeout = 15_000 });
        if (await warning.IsVisibleAsync())
        {
            await Expect(Page.Locator("#case-leak-save-again")).ToBeVisibleAsync(new() { Timeout = 15_000 });
            await SaveButton.ClickAsync();
        }
        await ExpectSavedAsync();
    }

    [Test]
    public async Task Publishing_a_surname_title_warns_once_then_publishes_on_the_second_save()
    {
        await LoginAsync(UserEmail, UserPassword); // Sarah, TGH member
        if (!await OpenOrgCaseAsync("Paranormal365", "Belmont"))
            Assert.Pass("TGH Park case not in the seed data; nothing to walk.");

        var editUrl = await OpenEditPageAsync();

        // ENSURE the state the test needs, and remember what to put back — shared DB.
        var originalTitle = await TitleInput.InputValueAsync();
        var wasPublic = await MakePublic.IsCheckedAsync();

        try
        {
            await TitleInput.FillAsync("Park Residence, Nashville TN");
            if (!wasPublic) await MakePublic.CheckAsync();

            await SaveButton.ClickAsync();

            // First save: the warning, not the save. The sentence names what leaked.
            var warning = Page.Locator("#case-title-leak-warning");
            await Expect(warning).ToBeVisibleAsync(new() { Timeout = 10_000 });
            await Expect(warning).ToContainTextAsync("\"Park\"");
            await Expect(Page).ToHaveURLAsync(new System.Text.RegularExpressions.Regex(@"/edit$")); // still editing — nothing saved

            // The warning alone does not prove the first Save was handled: the live check draws it as soon as the label
            // is left, before Save runs. "Save again" is drawn only once that Save has stopped at it.
            await Expect(Page.Locator("#case-leak-save-again")).ToBeVisibleAsync(new() { Timeout = 10_000 });

            // Second save on the same text: warn-not-block means this one goes through.
            await SaveButton.ClickAsync();
            await ExpectSavedAsync();
        }
        finally
        {
            await RestoreAsync(editUrl, originalTitle, pseudonym: null, wasPublic);
        }
    }

    /// <summary>
    /// Item 182: the retrofit door is present and wired on a case's Edit Case page.
    /// </summary>
    /// <remarks>
    /// Deliberately does NOT click it. The retrofit is destructive by design — it makes a case
    /// private and clears its exact coordinates — and there is no endpoint to delete a case
    /// afterwards (item 183), so a clicking test would either degrade the seeded case or leave a
    /// permanent one behind on every run. What the button DOES is covered thoroughly by
    /// CasePrivacyRetrofitTests; what this covers is that a real administrator can reach it,
    /// which is the half unit tests cannot see.
    /// </remarks>
    [Test]
    public async Task The_privacy_retrofit_is_reachable_from_a_cases_edit_page()
    {
        await LoginAsync(UserEmail, UserPassword);   // Sarah — TGH administrator
        if (!await OpenOrgCaseAsync("Paranormal365", "Belmont"))
            Assert.Pass("TGH Park case not in the seed data; nothing to walk.");

        await OpenEditPageAsync();

        var apply = Page.Locator("#case-apply-privacy");
        await Expect(apply).ToBeVisibleAsync(new() { Timeout = 10_000 });
        await Expect(apply).ToBeEnabledAsync();

        // Leave without saving or applying anything.
        await Page.Locator("#case-edit-cancel").ClickAsync();
        await ExpectSavedAsync();
    }

    [Test]
    public async Task A_place_named_title_saves_public_with_no_warning_at_all()
    {
        await LoginAsync(UserEmail, UserPassword);
        if (!await OpenOrgCaseAsync("Paranormal365", "Belmont"))
            Assert.Pass("TGH Park case not in the seed data; nothing to walk.");

        var editUrl = await OpenEditPageAsync();

        var originalTitle = await TitleInput.InputValueAsync();
        var wasPublic = await MakePublic.IsCheckedAsync();
        var originalPseudonym = await Pseudonym.InputValueAsync();

        try
        {
            // A clean title AND a clean pseudonym — the seeded pseudonym may carry the surname.
            await TitleInput.FillAsync("The Belmont Farmhouse");
            await Pseudonym.FillAsync("The Hargrove Family");
            if (!wasPublic) await MakePublic.CheckAsync();

            await SaveButton.ClickAsync();

            // No warning stop: the first save goes straight back to the case.
            await ExpectSavedAsync();
            Assert.That(await Page.Locator("#case-title-leak-warning").CountAsync(), Is.EqualTo(0));
        }
        finally
        {
            await RestoreAsync(editUrl, originalTitle, originalPseudonym, wasPublic);
        }
    }
}
