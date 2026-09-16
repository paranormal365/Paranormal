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
/// <para><b>Needs the canvas host running</b> at <c>localhost:5125</c>. <c>scripts/run-e2e.sh</c> starts it as a
/// fourth host, so a full run exercises this for real. There is no switch to turn on any more: research IS boards
/// since 2026-09-16, and this fixture fails rather than skips — a skip here would hide a broken handover behind a
/// green run, which is exactly what it did while the flag existed.</para>
/// </remarks>
[TestFixture]
[Category("Canvas")]
public class CanvasResearchHandoverTests : BenTestBase
{
    private async Task OpenResearchTabAsync()
    {
        await LoginAsync(SuperAdminEmail, SuperAdminPassword);
        Assert.That(await OpenOrgCaseAsync("Paranormal365", "Belmont"), Is.True,
            "the seeded Belmont case is not on this database, so there is nothing to open research on");

        var boards = Main.Locator("[data-testid=case-research-boards]");
        await OpenTabAsync("Research", boards);
        await SkipAnyTourAsync();
        await Expect(boards).ToBeVisibleAsync(new() { Timeout = 15_000 });
    }

    [Test]
    public async Task The_Research_tab_lists_boards_and_offers_a_new_one()
    {
        await OpenResearchTabAsync();

        await Expect(Page.Locator("#research-new-board")).ToBeVisibleAsync(new() { Timeout = 15_000 });
    }

    [Test]
    public async Task Starting_a_board_opens_the_canvas_on_this_case_already_signed_in()
    {
        await OpenResearchTabAsync();

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
