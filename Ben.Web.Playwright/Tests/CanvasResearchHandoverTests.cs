using Microsoft.Playwright;
using NUnit.Framework;

namespace Ben.Web.Playwright.Tests;

/// <summary>
/// The case's Research tab hands a board to the canvas editor, already signed in.
/// </summary>
/// <remarks>
/// <para>Ben, 2026-09-16: research is written on the canvas, on the person's own machine, and kept as a draft until it
/// is published. The site's half of that is this handover: a one-use code in the URL's fragment, the case and group
/// beside it, and the canvas opening on them.</para>
///
/// <para><b>Needs the canvas host running</b> at <c>localhost:5125</c> (<c>dotnet run --project Ben.Wasm.Canvas</c>)
/// and <c>features.canvas-editor</c> switched on. Neither is true on a plain e2e run, so this fixture skips rather
/// than fails when the tab is not there — the suite must not go red for a switch nobody turned on.</para>
/// </remarks>
[TestFixture]
[Category("Canvas")]
public class CanvasResearchHandoverTests : BenTestBase
{
    private async Task<bool> OpenResearchTabAsync()
    {
        await LoginAsync(SuperAdminEmail, SuperAdminPassword);
        if (!await OpenOrgCaseAsync("Paranormal365", "Belmont")) return false;

        // Whichever shape the tab is in: boards when the canvas is on, the block-editor pages when it is off. Waiting
        // on the boards alone would fail the suite for a switch nobody turned on, which is what it did once.
        var boards = Main.Locator("[data-testid=case-research-boards]");
        await OpenTabAsync("Research", boards.Or(Main.Locator("#research-new-page")).First);
        await SkipAnyTourAsync();

        if (await boards.CountAsync() == 0) return false;   // the canvas is switched off on this database
        await Expect(boards).ToBeVisibleAsync(new() { Timeout = 15_000 });
        return true;
    }

    [Test]
    public async Task The_Research_tab_lists_boards_and_offers_a_new_one()
    {
        if (!await OpenResearchTabAsync())
            Assert.Ignore("The canvas editor is switched off on this database, or the seeded case is not here.");

        await Expect(Page.Locator("#research-new-board")).ToBeVisibleAsync(new() { Timeout = 15_000 });
    }

    [Test]
    public async Task Starting_a_board_opens_the_canvas_on_this_case_already_signed_in()
    {
        if (!await OpenResearchTabAsync())
            Assert.Ignore("The canvas editor is switched off on this database, or the seeded case is not here.");

        var caseUrl = Page.Url;
        await Page.Locator("#research-new-board").ClickAsync();

        // The canvas is a separate application: the site leaves, carrying the handover in the fragment.
        await Page.WaitForURLAsync(new System.Text.RegularExpressions.Regex(@"localhost:5125"), new() { Timeout = 30_000 });
        var fragment = new Uri(Page.Url).Fragment;
        TestContext.Out.WriteLine($"handover: {fragment}");

        Assert.That(fragment, Does.Contain("handoff="), "no sign-in code was handed over");
        Assert.That(fragment, Does.Contain("case="), "the canvas was not told which case the board belongs to");
        Assert.That(fragment, Does.Contain("org="), "the canvas was not told the group");

        var caseId = System.Text.RegularExpressions.Regex.Match(caseUrl, @"/cases/([0-9a-f\-]+)").Groups[1].Value;
        Assert.That(fragment, Does.Contain(caseId), "the canvas was handed a different case");

        // And the editor comes up rather than the sign-in page.
        await Expect(Page.Locator(".bc-board, [data-bc-board], .bc-shell").First)
            .ToBeVisibleAsync(new() { Timeout = 30_000 });
    }
}
