using Microsoft.Playwright;
using NUnit.Framework;

namespace Ben.Web.Playwright.Tests;

/// <summary>
/// Presenting a research board: the cards are the slides.
/// </summary>
/// <remarks>
/// <para>Ben, 2026-09-16: "presentation mode like miro where you can create the cards like slides." The board the
/// development seed puts on the Belmont case is a chain of four joined cards and a note beside them, so it walks in
/// the order the arrows were drawn — which is the thing worth proving here, since it is the payoff for growing one
/// card out of another.</para>
///
/// <para><b>Needs the canvas host running</b> at <c>localhost:5125</c>; <c>scripts/run-e2e.sh</c> starts it.</para>
/// </remarks>
[TestFixture]
[Category("Canvas")]
public class CanvasPresentationTests : BenTestBase
{
    private ILocator Bar => Page.Locator(".bc-present");

    private ILocator Counter => Page.Locator("[data-bc-present-counter] .bc-present__count");

    /// <summary>Opens the seeded board in the canvas, signed in through the case's Research tab.</summary>
    private async Task OpenTheSeededBoardAsync()
    {
        await LoginAsync(SuperAdminEmail, SuperAdminPassword);
        Assert.That(await OpenOrgCaseAsync("Paranormal365", "Belmont"), Is.True,
            "the seeded Belmont case is not on this database");

        var boards = Main.Locator("[data-testid=case-research-boards]");
        await OpenTabAsync("Research", boards);
        await SkipAnyTourAsync();

        var seeded = boards.GetByText("Previous owners and where they are buried");
        await Expect(seeded).ToBeVisibleAsync(new() { Timeout = 15_000 });
        await seeded.ClickAsync();

        await Page.WaitForURLAsync(new System.Text.RegularExpressions.Regex(@"localhost:5125"), new() { Timeout = 30_000 });
        await Expect(Page.Locator("[data-bc-ready=true]")).ToBeVisibleAsync(new() { Timeout = 60_000 });
    }

    private async Task StartPresentingAsync()
    {
        await Page.Locator("[data-bc-action=present]").ClickAsync();
        await Expect(Bar).ToBeVisibleAsync(new() { Timeout = 15_000 });
    }

    [Test]
    public async Task Presenting_walks_the_cards_in_the_order_the_arrows_were_drawn()
    {
        await OpenTheSeededBoardAsync();
        await StartPresentingAsync();

        // Five stops: the four joined cards, then the note that is not joined to anything.
        await Expect(Counter).ToHaveTextAsync("1 / 5");
        await Expect(Page.Locator("[data-bc-present-counter]")).ToContainTextAsync("Built 1924");

        await Page.Keyboard.PressAsync("ArrowRight");
        await Expect(Counter).ToHaveTextAsync("2 / 5");
        await Expect(Page.Locator("[data-bc-present-counter]")).ToContainTextAsync("Sold 1951");

        await Page.Keyboard.PressAsync("ArrowLeft");
        await Expect(Counter).ToHaveTextAsync("1 / 5");
    }

    /// <summary>The first and last cards are ends, not a loop: a room reads a wrap as a mistake.</summary>
    [Test]
    public async Task The_walk_stops_at_both_ends()
    {
        await OpenTheSeededBoardAsync();
        await StartPresentingAsync();

        await Expect(Page.Locator("[data-bc-action=present-previous]")).ToBeDisabledAsync();
        await Page.Keyboard.PressAsync("End");
        await Expect(Counter).ToHaveTextAsync("5 / 5");
        await Expect(Page.Locator("[data-bc-action=present-next]")).ToBeDisabledAsync();

        await Page.Keyboard.PressAsync("Home");
        await Expect(Counter).ToHaveTextAsync("1 / 5");
    }

    /// <summary>The room looks at the card, so the editor's own furniture goes away while presenting.</summary>
    [Test]
    public async Task Presenting_hides_the_editor_and_Escape_brings_it_back()
    {
        await OpenTheSeededBoardAsync();
        await Expect(Page.Locator(".bc-rail")).ToBeVisibleAsync();

        await StartPresentingAsync();
        await Expect(Page.Locator(".bc-rail")).ToBeHiddenAsync();
        await Expect(Page.Locator(".bc-header")).ToBeHiddenAsync();

        await Page.Keyboard.PressAsync("Escape");
        await Expect(Bar).ToBeHiddenAsync(new() { Timeout = 15_000 });
        await Expect(Page.Locator(".bc-rail")).ToBeVisibleAsync();
    }

    /// <summary>
    /// A walk touches nothing — the board comes out at the revision it went in at.
    /// </summary>
    /// <remarks>
    /// The point is not tidiness: a board is presented in the meeting where the case is talked through, and if
    /// presenting wrote to it, somebody who may only read a case could not be the one driving.
    /// </remarks>
    [Test]
    public async Task Presenting_leaves_the_board_unchanged()
    {
        await OpenTheSeededBoardAsync();
        var before = await Page.EvaluateAsync<string>(
            "() => document.querySelector('.bc-header__title')?.textContent ?? ''");

        await StartPresentingAsync();
        await Page.Keyboard.PressAsync("ArrowRight");
        await Page.Keyboard.PressAsync("ArrowRight");
        await Page.Keyboard.PressAsync("Escape");
        await Expect(Bar).ToBeHiddenAsync(new() { Timeout = 15_000 });

        // Undo is still empty: nothing the walk did is on the board's history.
        await Expect(Page.Locator("[data-bc-action=undo]").First).ToBeDisabledAsync();
        Assert.That(await Page.EvaluateAsync<string>(
            "() => document.querySelector('.bc-header__title')?.textContent ?? ''"), Is.EqualTo(before));
    }
}
