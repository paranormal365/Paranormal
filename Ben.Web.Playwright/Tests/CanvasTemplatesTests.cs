using Microsoft.Playwright;
using NUnit.Framework;

namespace Ben.Web.Playwright.Tests;

/// <summary>
/// New board offers something to start from, and what somebody picks is what opens.
/// </summary>
/// <remarks>
/// <para>Ben sent four boards as pictures on 2026-09-18 — a moodboard, a research plan, a family tree
/// and a presentation deck — and asked for these types as well. The layouts are pure functions with
/// their own tests (<c>BoardTemplateTests</c>); what only a browser can show is that the choice
/// survives the trip: the site is a Blazor Server app, the editor is a separate WebAssembly
/// application on another port, and the only thing joining them is a URL fragment. A template id
/// dropped anywhere along that path opens a blank board, silently, and the site would look like the
/// click did nothing.</para>
///
/// <para><b>Needs the canvas host running</b> at <c>localhost:5125</c>, which
/// <c>scripts/run-e2e.sh</c> starts as a fourth host. This fixture fails rather than skips, for the
/// reason written on <see cref="CanvasResearchHandoverTests"/>.</para>
/// </remarks>
[TestFixture]
[Category("Canvas")]
public class CanvasTemplatesTests : BenTestBase
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
    public async Task New_board_offers_the_five_boards_to_start_from()
    {
        await OpenResearchTabAsync();
        await Page.Locator("#research-new-board").ClickAsync();

        var list = Page.Locator("[data-testid=board-template-list]");
        await Expect(list).ToBeVisibleAsync(new() { Timeout = 15_000 });

        // Deliberately hard-coded: adding a template is meant to move this number, and the ids are
        // pinned against the canvas's own catalogue by CanvasBoardTemplateCatalogueTests.
        await Expect(list.Locator("button")).ToHaveCountAsync(5);

        foreach (var id in new[] { "blank", "moodboard", "research-plan", "family-tree", "deck" })
            await Expect(Page.Locator($"#board-template-{id}")).ToBeVisibleAsync();
    }

    /// <summary>
    /// The deck: four slide frames, which is also the shape presenting reads.
    /// </summary>
    [Test]
    public async Task New_board_from_the_deck_template_opens_with_its_frames()
    {
        await OpenResearchTabAsync();

        await Page.Locator("#research-new-board").ClickAsync();
        var offer = Page.Locator("#board-template-deck");
        await Expect(offer).ToBeVisibleAsync(new() { Timeout = 15_000 });
        await offer.ClickAsync();

        await Page.WaitForURLAsync(new System.Text.RegularExpressions.Regex(@"localhost:5125"), new() { Timeout = 30_000 });
        await Expect(Page.Locator("[data-bc-ready=true]")).ToBeVisibleAsync(new() { Timeout = 60_000 });

        // Four panels and their contents, not the case's existing board: this is the seam that would
        // have broken silently, because every load opens the case's newest board afterwards.
        await Expect(Page.Locator(".bc-group")).ToHaveCountAsync(4, new() { Timeout = 30_000 });
        await Expect(Page.Locator(".bc-node")).ToHaveCountAsync(8, new() { Timeout = 30_000 });
        await Expect(Page.Locator(".bc-header__title")).ToContainTextAsync("Presentation deck");
    }

    /// <summary>
    /// The family tree: right angles, and an empty picture frame above every name.
    /// </summary>
    /// <remarks>
    /// <b>Ben, 2026-09-18:</b> "include a place to put a photo of the person - if they want to do that
    /// or even just using a male and female icon. It should be up to the end user." Empty is the
    /// shipped state, so what is asserted is the frame's own words: a filled frame, or a silhouette
    /// somebody has to delete, would both fail this.
    /// </remarks>
    [Test]
    public async Task The_family_tree_gives_every_person_an_empty_photo_frame()
    {
        await OpenResearchTabAsync();

        await Page.Locator("#research-new-board").ClickAsync();
        var offer = Page.Locator("#board-template-family-tree");
        await Expect(offer).ToBeVisibleAsync(new() { Timeout = 15_000 });
        await offer.ClickAsync();

        await Page.WaitForURLAsync(new System.Text.RegularExpressions.Regex(@"localhost:5125"), new() { Timeout = 30_000 });
        await Expect(Page.Locator("[data-bc-ready=true]")).ToBeVisibleAsync(new() { Timeout = 60_000 });

        await Expect(Page.Locator(".bc-image__placeholder")).ToHaveCountAsync(6, new() { Timeout = 30_000 });
        await Expect(Page.Locator(".bc-image__placeholder").First).ToContainTextAsync("No picture yet");

        // And the lines: one marriage, three parent-to-child, two sibling links and one grandchild.
        // The same number is pinned in BoardTemplateTests, so the two move together.
        await Expect(Page.Locator(".bc-edge__line")).ToHaveCountAsync(7, new() { Timeout = 15_000 });
    }

    /// <summary>
    /// Blank still opens a blank board, which is what New board did before there was a choice.
    /// </summary>
    [Test]
    public async Task Blank_is_still_one_click_away()
    {
        await OpenResearchTabAsync();

        await Page.Locator("#research-new-board").ClickAsync();
        var offer = Page.Locator("#board-template-blank");
        await Expect(offer).ToBeVisibleAsync(new() { Timeout = 15_000 });
        await offer.ClickAsync();

        await Page.WaitForURLAsync(new System.Text.RegularExpressions.Regex(@"localhost:5125"), new() { Timeout = 30_000 });
        await Expect(Page.Locator("[data-bc-ready=true]")).ToBeVisibleAsync(new() { Timeout = 60_000 });

        await Expect(Page.Locator(".bc-group")).ToHaveCountAsync(0, new() { Timeout = 15_000 });
    }
}
