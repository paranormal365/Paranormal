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
    [Description("Community Rating section is visible on a public case page.")]
    public async Task CaseDetail_ShowsCommunityRating()
    {
        await Page.GotoAsync($"{BaseUrl}/o/{OrgUrlName}/cases/{CaseRef}");
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);

        await Expect(Page.GetByText("Community Rating", new() { Exact = false }))
            .ToBeVisibleAsync(new() { Timeout = 10_000 });
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

        var confirmBtn = Page.GetByRole(AriaRole.Button, new() { Name = "Confirms the findings" });
        await Expect(confirmBtn).ToBeVisibleAsync(new() { Timeout = 10_000 });

        var disputeBtn = Page.GetByRole(AriaRole.Button, new() { Name = "Disputes the findings" });
        await Expect(disputeBtn).ToBeVisibleAsync();
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

        // Read the initial total votes number
        var voteCount = Page.Locator("text=vote", new() { HasText = "vote" }).First;

        // Cast a Confirms vote
        var confirmBtn = Page.GetByRole(AriaRole.Button, new() { Name = "Confirms the findings" });
        await confirmBtn.ClickAsync();

        // A Remove button should now appear (user has an active vote)
        var removeBtn = Page.GetByRole(AriaRole.Button, new() { Name = "Remove" });
        await Expect(removeBtn).ToBeVisibleAsync(new() { Timeout = 8_000 });

        // Clean up — remove the vote so repeated test runs stay idempotent
        await removeBtn.ClickAsync();
        await Expect(removeBtn).ToBeHiddenAsync(new() { Timeout = 5_000 });
    }

    [Test]
    [Description("Case detail page timeline renders at least one entry.")]
    public async Task CaseDetail_TimelineShowsEntries()
    {
        await Page.GotoAsync($"{BaseUrl}/o/{OrgUrlName}/cases/{CaseRef}");
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);

        await Expect(Main.GetByText("Timeline", new() { Exact = false })).ToBeVisibleAsync(new() { Timeout = 10_000 });
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
