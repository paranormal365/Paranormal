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
    /// <summary>
    /// Voting ships behind a switch, and this suite runs against deployments where it is off.
    /// </summary>
    /// <remarks>
    /// The whole fixture is about the vote widget, so the gate belongs on the fixture. With the
    /// feature off the widget is not rendered anywhere and every test here fails on a site that
    /// is behaving exactly as configured.
    /// </remarks>
    [SetUp]
    public async Task SkipWhenVotingIsSwitchedOff()
        => await SkipIfFeatureOffAsync("features.voting");

    private const string TghCaseRef  = "2026-001";
    private const string TghUrlName  = "paranormal365";

    // ── The widget, as it is drawn since 2026-09-11 ───────────────────────────
    //
    // The vote is ONE button ("Vote") that opens the three choices; once cast it is labelled with
    // the reader's own vote and pressing it again takes the vote back. The separate Remove button
    // and the "Community Rating" heading these tests used to look for are gone.

    /// <summary>The one vote button on a case's own page.</summary>
    private ILocator VoteButton => Page.Locator(".vote-actions__vote > .vote-btn");

    /// <summary>The three choices that hang under the vote button while it is open.</summary>
    private ILocator Choices => Page.Locator(".vote-choices[role=menu]");

    private ILocator Choice(string name) => Choices.GetByRole(AriaRole.Button, new() { Name = name });

    private async Task OpenCaseAsync()
    {
        await Page.GotoAsync($"{BaseUrl}/o/{TghUrlName}/cases/{TghCaseRef}");
        await WaitUntilLoadedAsync();
        await Expect(VoteButton).ToBeVisibleAsync(new() { Timeout = 15_000 });
    }

    /// <summary>
    /// Leaves the reader with no vote on the case. These tests write real votes, so a run can
    /// inherit one from the last.
    /// </summary>
    private async Task StartWithNoVoteAsync()
    {
        // Signed in and drawn for that reader first: a widget that rendered before sign-in resolved
        // reloads its summary, and reading the button before then reads a stranger's.
        await Expect(Page.Locator(".vote-actions__signin")).ToHaveCountAsync(0, new() { Timeout = 15_000 });
        if (await VoteButton.GetAttributeAsync("aria-pressed") == "true")
            await TakeTheVoteBackAsync();
    }

    private async Task TakeTheVoteBackAsync()
    {
        await VoteButton.ClickAsync();
        await Expect(VoteButton).ToHaveAttributeAsync("aria-pressed", "false", new() { Timeout = 8_000 });
        await Expect(VoteButton).ToHaveAttributeAsync("aria-label", "Vote");
    }

    private async Task CastAsync(string choice)
    {
        await ClickUntilAsync(VoteButton, Choices);
        await Choice(choice).ClickAsync();
        await Expect(VoteButton).ToHaveAttributeAsync("aria-pressed", "true", new() { Timeout = 8_000 });
    }

    // ── Case vote widget (anonymous) ──────────────────────────────────────────

    [Test]
    public async Task CaseDetail_AnonymousUser_ShowsSignInPromptUnderTheVoteButton()
    {
        await OpenCaseAsync();
        await Expect(Page.Locator(".vote-actions__signin")).ToContainTextAsync("Sign in to vote");
        await Expect(VoteButton).ToHaveAttributeAsync("aria-label", "Vote");
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
    public async Task CaseDetail_AuthUser_TheVoteButtonOffersAllThreeChoices()
    {
        await LoginAsync(UserEmail, UserPassword);
        await OpenCaseAsync();
        await StartWithNoVoteAsync();

        await ClickUntilAsync(VoteButton, Choices);
        await Expect(Choice("Confirms the findings")).ToBeVisibleAsync();
        await Expect(Choice("Disputes the findings")).ToBeVisibleAsync();
        await Expect(Choice("Inconclusive — can't say either way")).ToBeVisibleAsync();
    }

    /// <summary>
    /// Item #103's worst case, as a regression test. The home page's vote summaries used to be
    /// loaded behind a bare IsAuthenticated read during the initial load — and auth resolves
    /// asynchronously after the circuit connects, so on a HARD navigation (a fresh page load, not
    /// a client-side nav) a signed-in user's vote widgets were drawn for a stranger until they
    /// paged or re-sorted. This test signs in, then does a full GotoAsync to the home page — the
    /// hard-nav case — and requires the cards' widgets to be drawn for a signed-in reader.
    ///
    /// <para>Honesty note: the race resolves in auth's favour locally, so this cannot prove the fix
    /// and is kept as a smoke test of the flow. The enforcing regression barrier is
    /// AuthReadyPrerenderGuardTests.Every_reader_of_auth_state_follows_its_resolution, a source
    /// scan that fails the moment the wait is removed, timing be damned.</para>
    /// </summary>
    [Test]
    public async Task Home_HardNavigationWhileSignedIn_ShowsVoteWidgetsOnCards()
    {
        await LoginAsync(UserEmail, UserPassword);

        // GotoAsync is a full page load: prerender, new circuit, auth resolving late — the exact
        // sequence that raced the old code.
        await Page.GotoAsync(BaseUrl);

        var widgets = Page.Locator(".case-vote-widget");
        await Expect(widgets.Locator(".vote-actions__vote > .vote-btn").First)
            .ToBeVisibleAsync(new() { Timeout = 15_000 });
        await Expect(widgets.Locator(".vote-actions__signin")).ToHaveCountAsync(0, new() { Timeout = 15_000 });
    }

    [Test]
    public async Task CaseDetail_CastVote_TheButtonSaysItAndTakesItBack()
    {
        await LoginAsync(UserEmail, UserPassword);
        await OpenCaseAsync();
        await StartWithNoVoteAsync();

        await CastAsync("Inconclusive — can't say either way");
        await Expect(VoteButton).ToHaveAttributeAsync("aria-label", "Your vote: can't say. Press to take it back.");

        await TakeTheVoteBackAsync();
    }

    [Test]
    public async Task CaseDetail_ChangeVote_UpdatesTheButton()
    {
        await LoginAsync(UserEmail, UserPassword);
        await OpenCaseAsync();
        await StartWithNoVoteAsync();

        await CastAsync("Confirms the findings");
        await Expect(VoteButton).ToHaveAttributeAsync("aria-label", "Your vote: confirms. Press to take it back.");

        // Changing a vote is taking it back and choosing again.
        await TakeTheVoteBackAsync();
        await CastAsync("Disputes the findings");
        await Expect(VoteButton).ToHaveAttributeAsync("aria-label", "Your vote: disputes. Press to take it back.");

        await TakeTheVoteBackAsync();
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
        await OpenCaseAsync();
        await StartWithNoVoteAsync();

        await CastAsync("Confirms the findings");

        await Page.ReloadAsync();
        await WaitUntilLoadedAsync();

        await Expect(VoteButton).ToHaveAttributeAsync("aria-label", "Your vote: confirms. Press to take it back.",
            new() { Timeout = 15_000 });

        await TakeTheVoteBackAsync();
    }
}
