using Microsoft.Playwright;
using NUnit.Framework;

namespace Ben.Web.Playwright.Tests;

/// <summary>
/// Editing a case happens on a page of its own, with room for a description of any length.
/// </summary>
/// <remarks>
/// Beta feedback, 2026-09-14. The Edit Case dialog gave the description a 200px box under a toolbar that wrapped to
/// three rows, and its Make Public box sat below its own label. Ben then asked for the form to leave the dialog
/// altogether: "the case definition can be different lengths." Saving, the leak warning and the privacy retrofit are
/// walked by PublishLeakWarningTests; this covers the page's shape and that leaving it does not lose work quietly.
/// </remarks>
[TestFixture]
[Category("CaseManagement")]
[NonParallelizable]   // the one seeded case, shared with PublishLeakWarningTests and LookAndTruthsTests
public class CaseEditPageTests : BenTestBase
{
    private async Task OpenBelmontEditPageAsync()
    {
        await LoginAsync(UserEmail, UserPassword);   // Sarah — Paranormal365 administrator
        if (!await OpenOrgCaseAsync("Paranormal365", "Belmont"))
            Assert.Ignore("The seeded Belmont case is not in this database.");

        await ClickUntilUrlAsync(Page.Locator("#case-edit"), @"/cases/[0-9a-f\-]+/edit$");
        await Expect(Page.Locator("#casedetail-case-label-surname-city-0146")).ToBeVisibleAsync(new() { Timeout = 15_000 });
        await Expect(Page.Locator("#case-edit-description .k-editor")).ToBeVisibleAsync(new() { Timeout = 15_000 });
    }

    [Test]
    public async Task EditCase_IsAPage_WithATallDescriptionEditor()
    {
        await Page.SetViewportSizeAsync(1280, 800);
        await OpenBelmontEditPageAsync();

        Assert.That(await Page.Locator(".modal.show").CountAsync(), Is.EqualTo(0), "Edit Case opened a dialog, not a page");

        var editor = (await Page.Locator("#case-edit-description .k-editor").BoundingBoxAsync())!;
        Assert.That(editor.Height, Is.GreaterThanOrEqualTo(300), $"the description editor is only {editor.Height:0}px tall");

        // The short toolbar fits one row at desktop width: its height is about one button's.
        var toolbar = (await Page.Locator("#case-edit-description .k-toolbar").BoundingBoxAsync())!;
        Assert.That(toolbar.Height, Is.LessThan(60), $"the toolbar wrapped: {toolbar.Height:0}px tall");
    }

    [Test]
    public async Task EditCase_FitsAPhone()
    {
        // Reached at desktop width — the helper walks the group list, whose phone layout it does not know — then
        // loaded again at phone width, which is what is being tested.
        await Page.SetViewportSizeAsync(1280, 800);
        await OpenBelmontEditPageAsync();
        await Page.SetViewportSizeAsync(375, 812);
        await Page.ReloadAsync();
        await WaitForTheCircuitAsync();
        await Expect(Page.Locator("#case-edit-description .k-editor")).ToBeVisibleAsync(new() { Timeout = 15_000 });

        var overflow = await Page.EvaluateAsync<int>("() => document.documentElement.scrollWidth - document.documentElement.clientWidth");
        Assert.That(overflow, Is.LessThanOrEqualTo(0), $"the page scrolls sideways by {overflow}px on a phone");

        var editor = (await Page.Locator("#case-edit-description .k-editor").BoundingBoxAsync())!;
        Assert.That(editor.X + editor.Width, Is.LessThanOrEqualTo(375 + 1), "the description editor runs off the right edge");

        await Expect(Page.Locator("#case-edit-save")).ToBeInViewportAsync();
    }

    [Test]
    public async Task MakePublic_SitsOnTheLineOfItsLabel()
    {
        await Page.SetViewportSizeAsync(1280, 800);
        await OpenBelmontEditPageAsync();

        var box = (await Page.Locator("#case-public").BoundingBoxAsync())!;
        var label = (await Page.Locator("label[for='case-public']").BoundingBoxAsync())!;
        var boxMiddle = box.Y + box.Height / 2;
        var labelMiddle = label.Y + label.Height / 2;

        Assert.That(Math.Abs(boxMiddle - labelMiddle), Is.LessThan(4),
            $"the checkbox's middle is {boxMiddle - labelMiddle:0.#}px off its label's");
        Assert.That(box.X, Is.LessThan(label.X), "the checkbox is not before its label");
    }

    [Test]
    public async Task LeavingWithChanges_Asks_AndStayingKeepsThem()
    {
        await Page.SetViewportSizeAsync(1280, 800);
        await OpenBelmontEditPageAsync();

        var pseudonym = Page.Locator("#casedetail-public-pseudonym-88c6");
        var original = await pseudonym.InputValueAsync();
        const string typed = "Unsaved pseudonym — do not keep";

        await pseudonym.FillAsync(typed);
        await pseudonym.BlurAsync();
        await Expect(Page.Locator("[data-testid='case-edit-actions']")).ToContainTextAsync("Unsaved changes");

        await Page.GetByRole(AriaRole.Link, new() { Name = "Back to the case" }).First.ClickAsync();

        var stay = Page.Locator("#leave-guard-stay");
        await Expect(stay).ToBeVisibleAsync(new() { Timeout = 10_000 });
        await stay.ClickAsync();

        await Expect(Page).ToHaveURLAsync(new System.Text.RegularExpressions.Regex(@"/edit$"));
        await Expect(pseudonym).ToHaveValueAsync(typed);

        // Cancel is the deliberate way out: it does not ask, and nothing was saved.
        await Page.Locator("#case-edit-cancel").ClickAsync();
        await Expect(Page).ToHaveURLAsync(new System.Text.RegularExpressions.Regex(@"/cases/[0-9a-f\-]+$"), new() { Timeout = 15_000 });

        await ClickUntilUrlAsync(Page.Locator("#case-edit"), @"/cases/[0-9a-f\-]+/edit$");
        await Expect(Page.Locator("#casedetail-public-pseudonym-88c6")).ToHaveValueAsync(original, new() { Timeout = 15_000 });
    }

    [Test]
    public async Task NewCase_WritesItsDescriptionInTheEditor()
    {
        await LoginAsync(UserEmail, UserPassword);
        var orgId = await OrgIdBySlugAsync("paranormal365");
        await Page.GotoAsync($"{BaseUrl}/organizations/{orgId}/cases/new");
        await WaitForTheCircuitAsync();

        await Expect(Page.Locator("#casecreatepage-description-483a .k-editor")).ToBeVisibleAsync(new() { Timeout = 15_000 });
        Assert.That(await Page.Locator("#casecreatepage-description-483a textarea").CountAsync(), Is.EqualTo(0),
            "New Case still has a plain text box for the description");
    }
}
