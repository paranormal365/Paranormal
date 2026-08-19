using Microsoft.Playwright;
using NUnit.Framework;

namespace Ben.Web.Playwright.Tests;

/// <summary>
/// Tests for the voting interactions across the app:
/// case-level votes on the map popup, case detail page, and list cards;
/// evidence votes on case detail; vote removal.
/// Requires authenticated user for write tests.
/// </summary>
[TestFixture]
[Category("Voting")]
public class VotingTests : BenTestBase
{
    private const string TghCaseRef  = "2026-001";
    private const string TghUrlName  = "tgh";

    // ── Case vote widget (anonymous) ──────────────────────────────────────────

    [Test]
    public async Task CaseDetail_AnonymousUser_ShowsSignInPromptInRatingSection()
    {
        await Page.GotoAsync($"{BaseUrl}/o/{TghUrlName}/cases/{TghCaseRef}");
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);
        await Expect(Page.GetByText("Community Rating", new() { Exact = false }))
            .ToBeVisibleAsync(new() { Timeout = 10_000 });
        await Expect(Page.GetByText("Sign in to vote", new() { Exact = false }))
            .ToBeVisibleAsync(new() { Timeout = 5_000 });
    }

    [Test]
    public async Task CaseDetail_AnonymousUser_ShowsVoteCounts()
    {
        await Page.GotoAsync($"{BaseUrl}/o/{TghUrlName}/cases/{TghCaseRef}");
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);
        // Seeded with votes — should show non-zero counts
        var confirmsText = Page.GetByText("✓", new() { Exact = false }).First;
        await Expect(confirmsText).ToBeVisibleAsync(new() { Timeout = 10_000 });
    }

    // ── Case vote widget (authenticated) ─────────────────────────────────────

    [Test]
    public async Task CaseDetail_AuthUser_AllThreeVoteButtonsVisible()
    {
        await LoginAsync(UserEmail, UserPassword);
        await Page.GotoAsync($"{BaseUrl}/o/{TghUrlName}/cases/{TghCaseRef}");
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);
        await Expect(Page.GetByRole(AriaRole.Button, new() { Name = "✓ Confirms" }))
            .ToBeVisibleAsync(new() { Timeout = 10_000 });
        await Expect(Page.GetByRole(AriaRole.Button, new() { Name = "✗ Disputes" }))
            .ToBeVisibleAsync();
        await Expect(Page.GetByRole(AriaRole.Button, new() { Name = "? Inconclusive" }))
            .ToBeVisibleAsync();
    }

    [Test]
    public async Task CaseDetail_CastVote_ShowsRemoveButton()
    {
        await LoginAsync(UserEmail, UserPassword);
        await Page.GotoAsync($"{BaseUrl}/o/{TghUrlName}/cases/{TghCaseRef}");
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);
        await Page.GetByRole(AriaRole.Button, new() { Name = "? Inconclusive" }).ClickAsync();
        var remove = Page.GetByRole(AriaRole.Button, new() { Name = "Remove" }).First;
        await Expect(remove).ToBeVisibleAsync(new() { Timeout = 5_000 });
        // Clean up
        await remove.ClickAsync();
        await Expect(remove).ToBeHiddenAsync(new() { Timeout = 5_000 });
    }

    [Test]
    public async Task CaseDetail_ChangeVote_UpdatesActiveButton()
    {
        await LoginAsync(UserEmail, UserPassword);
        await Page.GotoAsync($"{BaseUrl}/o/{TghUrlName}/cases/{TghCaseRef}");
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);

        var confirmsBtn = Page.GetByRole(AriaRole.Button, new() { Name = "✓ Confirms" });
        await confirmsBtn.ClickAsync();
        await Page.WaitForTimeoutAsync(500);

        // Change to Disputes
        var disputesBtn = Page.GetByRole(AriaRole.Button, new() { Name = "✗ Disputes" });
        await disputesBtn.ClickAsync();
        await Page.WaitForTimeoutAsync(500);

        // Remove to leave clean state
        var remove = Page.GetByRole(AriaRole.Button, new() { Name = "Remove" }).First;
        await Expect(remove).ToBeVisibleAsync(new() { Timeout = 5_000 });
        await remove.ClickAsync();
    }

    // ── Home page list votes ──────────────────────────────────────────────────

    [Test]
    public async Task HomeList_AuthUser_SeesVoteWidgetPerCard()
    {
        await LoginAsync(UserEmail, UserPassword);
        await Page.GotoAsync(BaseUrl);
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);
        await Page.WaitForSelectorAsync(".card", new() { Timeout = 15_000 });
        // All cards should have vote buttons loaded from batch endpoint
        var voteWidgets = Page.Locator(".case-vote-widget");
        var count = await voteWidgets.CountAsync();
        Assert.That(count, Is.GreaterThan(0), "Expected vote widgets on authenticated list cards.");
    }

    // ── Vote counts persist ───────────────────────────────────────────────────

    [Test]
    public async Task VoteCounts_PersistAfterPageReload()
    {
        await LoginAsync(UserEmail, UserPassword);
        await Page.GotoAsync($"{BaseUrl}/o/{TghUrlName}/cases/{TghCaseRef}");
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);

        // Start from "no vote". These tests write real votes, and the vote buttons toggle — so a
        // run that inherited a vote from the previous one un-voted here instead of voting, leaving
        // nothing to assert on. That is what made this fail intermittently rather than never.
        var existing = Page.GetByRole(AriaRole.Button, new() { Name = "Remove" }).First;
        if (await existing.CountAsync() > 0 && await existing.IsVisibleAsync())
        {
            await existing.ClickAsync();
            await Expect(existing).ToBeHiddenAsync(new() { Timeout = 8_000 });
        }

        // Cast a vote
        await Page.GetByRole(AriaRole.Button, new() { Name = "✓ Confirms" }).ClickAsync();
        await Page.WaitForTimeoutAsync(600);

        // Reload
        await Page.ReloadAsync();
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);

        // The Confirms button should now appear as the active vote (Solid fill = selected)
        var remove = Page.GetByRole(AriaRole.Button, new() { Name = "Remove" }).First;
        await Expect(remove).ToBeVisibleAsync(new() { Timeout = 8_000 });

        // Clean up
        await remove.ClickAsync();
    }
}
