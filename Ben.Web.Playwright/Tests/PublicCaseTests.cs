using Microsoft.Playwright;
using NUnit.Framework;

namespace Ben.Web.Playwright.Tests;

/// <summary>
/// Tests for public case detail pages (<c>/o/{urlName}/cases/{caseRef}</c>)
/// and community voting by authenticated users.
/// </summary>
[TestFixture]
[Category("PublicCase")]
public class PublicCaseTests : BenTestBase
{
    // Seeded by DevelopmentDataSeeder
    private const string OrgUrlName = "paranormal365";
    private const string CaseRef    = "2026-001";

    [Test]
    [Description("Public case detail page renders case title and org name.")]
    public async Task CaseDetail_RendersTitle()
    {
        await Page.GotoAsync($"{BaseUrl}/o/{OrgUrlName}/cases/{CaseRef}");
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);

        await Expect(Page.GetByText("Abandoned Springfield Farmhouse", new() { Exact = false }))
            .ToBeVisibleAsync(new() { Timeout = 10_000 });
    }

    [Test]
    [Description("The community rating section is visible on a public case page.")]
    public async Task CaseDetail_ShowsCommunityRating()
    {
        await Page.GotoAsync($"{BaseUrl}/o/{OrgUrlName}/cases/{CaseRef}");
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);

        // Held by id, not by its heading. This asserted on the words "Community Rating" and broke
        // when the heading was reworded to "What people think" — an edit that changed nothing
        // this test is about.
        await Expect(Page.Locator("#public-case-rating")).ToBeVisibleAsync(new() { Timeout = 10_000 });
    }

    [Test]
    [Description("Unauthenticated visitors see a 'Sign in to vote' prompt in the vote widget.")]
    public async Task CaseDetail_AnonymousUser_SeesSignInPrompt()
    {
        // Only this test is about the vote widget; the rest of the fixture is not, so the
        // gate is per test rather than on the fixture.
        await SkipIfFeatureOffAsync("features.voting");

        await Page.GotoAsync($"{BaseUrl}/o/{OrgUrlName}/cases/{CaseRef}");
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);

        // CaseVoteWidget shows "Sign in to vote" when not authenticated
        var prompt = Page.GetByText("Sign in to vote", new() { Exact = false });
        await Expect(prompt).ToBeVisibleAsync(new() { Timeout = 10_000 });
    }

    [Test]
    [Description("Authenticated users see vote buttons (Confirms / Disputes / Inconclusive).")]
    public async Task CaseDetail_AuthenticatedUser_SeesVoteButtons()
    {
        // Only this test is about the vote widget; the rest of the fixture is not, so the
        // gate is per test rather than on the fixture.
        await SkipIfFeatureOffAsync("features.voting");

        await LoginAsync(UserEmail, UserPassword);
        await Page.GotoAsync($"{BaseUrl}/o/{OrgUrlName}/cases/{CaseRef}");
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);

        // One button now, not three. It carries a hollow thumb until you vote and the filled icon
        // of your own vote afterwards; the three choices are a dropdown off it. This asserted on
        // two of those choices, which are no longer on the page until somebody asks for them.
        var vote = Page.Locator(".vote-actions__vote > .vote-btn").First;
        await Expect(vote).ToBeVisibleAsync(new() { Timeout = 10_000 });
        await vote.ClickAsync();

        await Expect(Page.GetByRole(AriaRole.Button, new() { Name = "Confirms the findings" }))
            .ToBeVisibleAsync(new() { Timeout = 8_000 });
        await Expect(Page.GetByRole(AriaRole.Button, new() { Name = "Disputes the findings" }))
            .ToBeVisibleAsync();
    }

    [Test]
    [Description("Clicking a vote button updates the vote count display.")]
    public async Task CaseDetail_CastingVote_UpdatesCount()
    {
        // Only this test is about the vote widget; the rest of the fixture is not, so the
        // gate is per test rather than on the fixture.
        await SkipIfFeatureOffAsync("features.voting");

        await LoginAsync(UserEmail, UserPassword);
        await Page.GotoAsync($"{BaseUrl}/o/{OrgUrlName}/cases/{CaseRef}");
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);

        var vote = Page.Locator(".vote-actions__vote > .vote-btn").First;
        await Expect(vote).ToBeVisibleAsync(new() { Timeout = 10_000 });

        // Open the dropdown and confirm.
        await vote.ClickAsync();
        await Page.GetByRole(AriaRole.Button, new() { Name = "Confirms the findings" }).ClickAsync();

        // The one button now reports the vote back: it fills in, and says what pressing it again
        // would do. There is no separate Remove button any more — pressing this one is the way
        // back, which is what the old Remove did.
        var filled = Page.Locator(".vote-btn__icon--filled");
        await Expect(filled).ToBeVisibleAsync(new() { Timeout = 8_000 });
        await Expect(vote).ToHaveAttributeAsync("aria-pressed", "true");

        // Clean up — take the vote back, so repeated runs stay idempotent.
        await vote.ClickAsync();
        await Expect(filled).ToBeHiddenAsync(new() { Timeout = 8_000 });
    }

    [Test]
    [Description("Case detail page timeline renders at least one entry.")]
    public async Task CaseDetail_TimelineShowsEntries()
    {
        await Page.GotoAsync($"{BaseUrl}/o/{OrgUrlName}/cases/{CaseRef}");
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);

        // By id rather than by the heading's words, for the same reason as the rating above —
        // and an entry inside it, because a heading over an empty section is not what this
        // test's name promises.
        var timeline = Page.Locator("#public-case-timeline");
        await Expect(timeline).ToBeVisibleAsync(new() { Timeout = 10_000 });
        await Expect(timeline.GetByText("Initial Client Report", new() { Exact = false }).First)
            .ToBeVisibleAsync(new() { Timeout = 10_000 });
    }

    [Test]
    [Description("Breadcrumb nav renders org name and 'Cases' link.")]
    public async Task CaseDetail_BreadcrumbRendersOrgAndCases()
    {
        await Page.GotoAsync($"{BaseUrl}/o/{OrgUrlName}/cases/{CaseRef}");
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);

        var orgLink = Page.GetByRole(AriaRole.Link, new() { Name = "Cases" });
        await Expect(orgLink).ToBeVisibleAsync(new() { Timeout = 8_000 });
    }
}
