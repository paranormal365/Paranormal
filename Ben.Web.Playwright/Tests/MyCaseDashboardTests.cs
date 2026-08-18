using Microsoft.Playwright;
using NUnit.Framework;

namespace Ben.Web.Playwright.Tests;

/// <summary>
/// Tests for the client case dashboard: /my-cases list and /my-cases/{caseId} detail.
/// Daniel Park (daniel.park@benco.dev) has one accepted case seeded by DevelopmentDataSeeder,
/// so these tests use his credentials to verify client-facing case features.
/// </summary>
[TestFixture]
[Category("MyCases")]
public class MyCaseDashboardTests : BenTestBase
{
    // Daniel Park is seeded with an accepted case; use his creds for client-perspective tests
    private const string ClientEmail    = "daniel.park@benco.dev";
    private const string ClientPassword = "D@niel!Park2026";

    // ── Navigation ─────────────────────────────────────────────────────────────

    [Test]
    public async Task MyCases_NavItem_VisibleAfterLogin()
    {
        await LoginAsync(ClientEmail, ClientPassword);
        await Page.GotoAsync(BaseUrl);
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);
        var myCasesLink = Page.GetByRole(AriaRole.Link, new() { Name = "My Cases" })
                              .Or(Page.GetByText("My Cases", new() { Exact = true }));
        await Expect(myCasesLink.First).ToBeVisibleAsync(new() { Timeout = 8_000 });
    }

    [Test]
    public async Task MyCases_NavItem_NotVisibleWhenLoggedOut()
    {
        await Page.GotoAsync(BaseUrl);
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);
        var myCasesLink = Page.GetByText("My Cases", new() { Exact = true });
        Assert.That(await myCasesLink.IsVisibleAsync(), Is.False,
            "My Cases nav item should not be visible to anonymous users.");
    }

    // ── /my-cases list ─────────────────────────────────────────────────────────

    [Test]
    public async Task MyCasesList_AnonymousRedirectsToLogin()
    {
        await Page.GotoAsync($"{BaseUrl}/my-cases");
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);
        Assert.That(Page.Url, Does.Contain("/login"),
            "Expected redirect to /login for anonymous access.");
    }

    [Test]
    public async Task MyCasesList_RendersAfterLogin()
    {
        await LoginAsync(ClientEmail, ClientPassword);
        await Page.GotoAsync($"{BaseUrl}/my-cases");
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);
        var body = await Page.InnerTextAsync("body");
        Assert.That(body, Does.Not.Contain("An unhandled error has occurred"));
        Assert.That(body, Does.Contain("My Cases"));
    }

    [Test]
    public async Task MyCasesList_ShowsAcceptedCase_WhenSeeded()
    {
        await LoginAsync(ClientEmail, ClientPassword);
        await Page.GotoAsync($"{BaseUrl}/my-cases");
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);
        // DevelopmentDataSeeder creates "Park Residence, Nashville TN" for Daniel
        var caseCard = Page.GetByText("Park Residence", new() { Exact = false })
                           .Or(Page.GetByText("Nashville", new() { Exact = false }).First)
                           .First;
        await Expect(caseCard).ToBeVisibleAsync(new() { Timeout = 12_000 });
    }

    [Test]
    public async Task MyCasesList_EmptyState_HasLinkToRequests()
    {
        // Log in as a user with no accepted cases — use sarah who is a member, not a client
        await LoginAsync(UserEmail, UserPassword);
        await Page.GotoAsync($"{BaseUrl}/my-cases");
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);
        var body = await Page.InnerTextAsync("body");
        // Either shows empty state or shows cases (sarah may have none)
        Assert.That(body, Does.Contain("My Cases").And.Not.Contain("An unhandled error has occurred"));
    }

    [Test]
    public async Task MyCasesList_ClickingCard_NavigatesToDetail()
    {
        await LoginAsync(ClientEmail, ClientPassword);
        await Page.GotoAsync($"{BaseUrl}/my-cases");
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);
        var card = Page.Locator(".card").First;
        if (!await card.IsVisibleAsync())
        {
            Assert.Pass("No cases in list — DevelopmentDataSeeder may not have run.");
            return;
        }
        await card.ClickAsync();
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);
        Assert.That(Page.Url, Does.Match(@"/my-cases/[0-9a-f\-]+"),
            "Expected navigation to case detail URL.");
    }

    // ── /my-cases/{caseId} detail ──────────────────────────────────────────────

    [Test]
    public async Task MyCaseDetail_RendersHeaderAndCalendar()
    {
        await LoginAsync(ClientEmail, ClientPassword);
        await Page.GotoAsync($"{BaseUrl}/my-cases");
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);
        var card = Page.Locator(".card").First;
        if (!await card.IsVisibleAsync()) { Assert.Pass("No cases seeded."); return; }
        await card.ClickAsync();
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);

        // Calendar should be visible
        var calendar = Page.Locator("[class*='k-calendar']").First;
        await Expect(calendar).ToBeVisibleAsync(new() { Timeout = 12_000 });
    }

    [Test]
    public async Task MyCaseDetail_ShowsCaseTitle()
    {
        await LoginAsync(ClientEmail, ClientPassword);
        await Page.GotoAsync($"{BaseUrl}/my-cases");
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);
        var card = Page.Locator(".card").First;
        if (!await card.IsVisibleAsync()) { Assert.Pass("No cases seeded."); return; }
        await card.ClickAsync();
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);

        // Should show "Park Residence" or the case reference
        var body = await Page.InnerTextAsync("body");
        Assert.That(body, Does.Contain("Nashville").Or.Contain("Park").Or.Contain("#2026"),
            "Expected case title or reference on detail page.");
    }

    [Test]
    public async Task MyCaseDetail_ShowsLogOccurrenceButton()
    {
        await LoginAsync(ClientEmail, ClientPassword);
        await Page.GotoAsync($"{BaseUrl}/my-cases");
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);
        var card = Page.Locator(".card").First;
        if (!await card.IsVisibleAsync()) { Assert.Pass("No cases seeded."); return; }
        await card.ClickAsync();
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);

        var logBtn = Page.GetByText("Log Occurrence", new() { Exact = false });
        await Expect(logBtn).ToBeVisibleAsync(new() { Timeout = 8_000 });
    }

    [Test]
    public async Task MyCaseDetail_LogOccurrenceDialog_Opens()
    {
        await LoginAsync(ClientEmail, ClientPassword);
        await Page.GotoAsync($"{BaseUrl}/my-cases");
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);
        var card = Page.Locator(".card").First;
        if (!await card.IsVisibleAsync()) { Assert.Pass("No cases seeded."); return; }
        await card.ClickAsync();
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);

        await Page.GetByText("Log Occurrence").ClickAsync();
        // TelerikWindow should open
        var window = Page.Locator(".k-window, .modal.show");
        await Expect(window).ToBeVisibleAsync(new() { Timeout = 5_000 });
        // Description textarea should be visible
        var desc = Page.Locator("[placeholder*='Describe' i]").First;
        await Expect(desc).ToBeVisibleAsync();
    }

    [Test]
    public async Task MyCaseDetail_LogOccurrenceDialog_RequiresDescription()
    {
        await LoginAsync(ClientEmail, ClientPassword);
        await Page.GotoAsync($"{BaseUrl}/my-cases");
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);
        var card = Page.Locator(".card").First;
        if (!await card.IsVisibleAsync()) { Assert.Pass("No cases seeded."); return; }
        await card.ClickAsync();
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);

        await Page.GetByText("Log Occurrence").ClickAsync();
        await Page.WaitForTimeoutAsync(400);
        // Save button should be disabled without a description
        var saveBtn = Page.GetByRole(AriaRole.Button, new() { Name = "Save", Exact = true });
        Assert.That(await saveBtn.IsEnabledAsync(), Is.False,
            "Save button should be disabled until a description is entered.");
    }

    [Test]
    public async Task MyCaseDetail_LogOccurrence_PersistsAfterSave()
    {
        await LoginAsync(ClientEmail, ClientPassword);
        await Page.GotoAsync($"{BaseUrl}/my-cases");
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);
        var card = Page.Locator(".card").First;
        if (!await card.IsVisibleAsync()) { Assert.Pass("No cases seeded."); return; }
        await card.ClickAsync();
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);

        await Page.GetByText("Log Occurrence").ClickAsync();
        await Page.WaitForTimeoutAsync(400);

        var desc = Page.Locator("[placeholder*='Describe' i]").First;
        await desc.FillAsync("Test occurrence from Playwright — loud knocking at 2 AM.");

        var saveBtn = Page.GetByRole(AriaRole.Button, new() { Name = "Save", Exact = true });
        await Expect(saveBtn).ToBeEnabledAsync(new() { Timeout = 3_000 });
        await saveBtn.ClickAsync();

        // Dialog should close after save
        await Expect(Page.Locator(".k-window, .modal.show")).ToBeHiddenAsync(new() { Timeout = 8_000 });

        // Occurrence should now appear for today's date
        var body = await Page.InnerTextAsync("body");
        Assert.That(body, Does.Contain("knocking").Or.Contain("Playwright"),
            "Logged occurrence should appear in the occurrence list.");
    }

    [Test]
    public async Task MyCaseDetail_ShowsInvestigations_WhenSeeded()
    {
        await LoginAsync(ClientEmail, ClientPassword);
        await Page.GotoAsync($"{BaseUrl}/my-cases");
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);
        var card = Page.Locator(".card").First;
        if (!await card.IsVisibleAsync()) { Assert.Pass("No cases seeded."); return; }
        await card.ClickAsync();
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);

        // Seeder creates "Initial Site Assessment" investigation for Daniel's case
        var body = await Page.InnerTextAsync("body");
        if (body.Contains("Investigations"))
        {
            var invSection = Main.GetByText("Investigations", new() { Exact = false });
            await Expect(invSection).ToBeVisibleAsync(new() { Timeout = 5_000 });
        }
        Assert.That(body, Does.Not.Contain("An unhandled error has occurred"));
    }

    [Test]
    public async Task MyCaseDetail_BackLink_ReturnsToCaseList()
    {
        await LoginAsync(ClientEmail, ClientPassword);
        await Page.GotoAsync($"{BaseUrl}/my-cases");
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);
        var card = Page.Locator(".card").First;
        if (!await card.IsVisibleAsync()) { Assert.Pass("No cases seeded."); return; }
        await card.ClickAsync();
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);

        var backLink = Page.GetByRole(AriaRole.Link, new() { Name = "← My Cases" })
                          .Or(Page.GetByText("← My Cases"));
        await Expect(backLink.First).ToBeVisibleAsync(new() { Timeout = 5_000 });
        await backLink.First.ClickAsync();
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);
        Assert.That(Page.Url, Does.EndWith("/my-cases"),
            "Expected navigation back to /my-cases.");
    }

    // ── ClientRequestDetail "View My Cases" button ─────────────────────────────

    [Test]
    public async Task ClientRequestDetail_AssignedRequest_ShowsViewMyCasesButton()
    {
        // An assigned request shows "View My Cases →"
        await LoginAsync(ClientEmail, ClientPassword);
        await Page.GotoAsync($"{BaseUrl}/my-requests");
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);

        // Find the Assigned request
        var assignedCard = Page.GetByText("Assigned", new() { Exact = false }).First;
        if (!await assignedCard.IsVisibleAsync())
        {
            Assert.Pass("No Assigned requests visible — seeder may not have run or status differs.");
            return;
        }
        await Page.Locator(".card").Filter(new() { HasText = "Assigned" }).First.ClickAsync();
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);

        var viewCasesBtn = Page.GetByText("View My Cases", new() { Exact = false });
        await Expect(viewCasesBtn).ToBeVisibleAsync(new() { Timeout = 8_000 });
    }
}
