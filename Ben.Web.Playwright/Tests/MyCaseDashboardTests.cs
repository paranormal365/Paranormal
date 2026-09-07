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

    // ── Navigation ─────────────────────────────────────────────────────────────

    [Test]
    public async Task MyCases_NavItem_VisibleAfterLogin()
    {
        await LoginAsync(ClientEmail, ClientPassword);
        await Page.GotoAsync(BaseUrl);
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);
        // "Visible" now means reachable: the entry lives inside the collapsed My Work group, and
        // FindSidebarLinkAsync surfaces it through the nav filter the way a person would.
        var myCasesLink = await FindSidebarLinkAsync("My Cases");
        await Expect(myCasesLink).ToBeVisibleAsync(new() { Timeout = 8_000 });
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
        // DevelopmentDataSeeder titles Daniel's case "Belmont Boulevard Residence" — renamed from
        // "Park Residence" by item 178, because Park is the client's surname and the title leaked it.
        var caseCard = Page.GetByText("Belmont Boulevard Residence", new() { Exact = false })
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
        await ClickUntilUrlAsync(card, @"/my-cases/[0-9a-f\-]+");
        await WaitUntilLoadedAsync();
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
        await ClickUntilUrlAsync(card, @"/my-cases/[0-9a-f\-]+");
        await WaitUntilLoadedAsync();

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
        await ClickUntilUrlAsync(card, @"/my-cases/[0-9a-f\-]+");
        await WaitUntilLoadedAsync();

        // Waited for, not read once. Every sibling test in this file uses an auto-waiting
        // Expect; this one alone took a single snapshot of the body, which passed only because
        // the card used to be a div whose @onclick could not fire until the circuit was live —
        // so the app was always warm by the time the detail page loaded. The card became a real
        // anchor in the 2026-09-06 evaluation's phase 1 (W-CL5), so the navigation now happens
        // on the first click, and the snapshot started landing before the case had loaded.
        // WaitUntilLoadedAsync cannot cover it: it returns as soon as no "Loading" text is on
        // the page, which is also true a moment before the page renders anything at all.
        await Expect(Main.GetByText("Nashville").Or(Main.GetByText("Park")).Or(Main.GetByText("#2026")).First)
            .ToBeVisibleAsync(new() { Timeout = 15_000 });
    }

    [Test]
    public async Task MyCaseDetail_ShowsLogOccurrenceButton()
    {
        await LoginAsync(ClientEmail, ClientPassword);
        await Page.GotoAsync($"{BaseUrl}/my-cases");
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);
        var card = Page.Locator(".card").First;
        if (!await card.IsVisibleAsync()) { Assert.Pass("No cases seeded."); return; }
        await ClickUntilUrlAsync(card, @"/my-cases/[0-9a-f\-]+");
        await WaitUntilLoadedAsync();

        // The ROLE, not the text: the log dialog's title is also "Log Occurrence" and BenModal
        // keeps it in the DOM while hidden, so a text match resolves to two elements.
        var logBtn = Page.GetByRole(AriaRole.Button, new() { Name = "Log Occurrence" });
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
        await ClickUntilUrlAsync(card, @"/my-cases/[0-9a-f\-]+");
        await WaitUntilLoadedAsync();

        // By role, not by text: "Log Occurrence" is also the dialog's own title, so a text match
        // becomes ambiguous the moment the dialog opens.
        var window = Page.Locator(".k-window, .modal.show");
        await ClickUntilAsync(Main.GetByRole(AriaRole.Button, new() { Name = "Log Occurrence" }).First, window);
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
        await ClickUntilUrlAsync(card, @"/my-cases/[0-9a-f\-]+");
        await WaitUntilLoadedAsync();

        var dialog = Page.Locator(".modal.show");
        await ClickUntilAsync(Main.GetByRole(AriaRole.Button, new() { Name = "Log Occurrence" }).First, dialog);

        // Scoped to the dialog: the case page also has an alias "Save" that is always enabled, and
        // an unscoped lookup found that one and reported the guard as broken.
        var saveBtn = dialog.GetByRole(AriaRole.Button, new() { Name = "Save", Exact = true });
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
        await ClickUntilUrlAsync(card, @"/my-cases/[0-9a-f\-]+");
        await WaitUntilLoadedAsync();

        var dialog = Page.Locator(".modal.show");
        await ClickUntilAsync(Main.GetByRole(AriaRole.Button, new() { Name = "Log Occurrence" }).First, dialog);

        var desc = dialog.Locator("[placeholder*='Describe' i]").First;
        await desc.FillAsync("Test occurrence from Playwright — loud knocking at 2 AM.");

        // Scoped to the dialog — see the note in the sibling test about the page's alias Save.
        var saveBtn = dialog.GetByRole(AriaRole.Button, new() { Name = "Save", Exact = true });
        await Expect(saveBtn).ToBeEnabledAsync(new() { Timeout = 3_000 });
        await saveBtn.ClickAsync();

        // Dialog should close after save
        await Expect(Page.Locator(".k-window, .modal.show")).ToBeHiddenAsync(new() { Timeout = 8_000 });

        // Poll rather than read once: the handler closes the dialog and *then* reloads the list, so
        // the moment the dialog hides is before the new occurrence exists on screen.
        await Expect(Main.GetByText("knocking", new() { Exact = false }).First)
            .ToBeVisibleAsync(new() { Timeout = 10_000 });
    }

    [Test]
    public async Task MyCaseDetail_ShowsInvestigations_WhenSeeded()
    {
        await LoginAsync(ClientEmail, ClientPassword);
        await Page.GotoAsync($"{BaseUrl}/my-cases");
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);
        var card = Page.Locator(".card").First;
        if (!await card.IsVisibleAsync()) { Assert.Pass("No cases seeded."); return; }
        await ClickUntilUrlAsync(card, @"/my-cases/[0-9a-f\-]+");
        await WaitUntilLoadedAsync();

        // Seeder creates "Initial Site Assessment" investigation for Daniel's case
        // Ask the content region, not the whole body: the sidebar always says "My Investigations",
        // so the guard was satisfied on every case and then asserted a section that is only there
        // when the case actually has investigations.
        var invSection = Main.GetByText("Investigations", new() { Exact = false });
        if (await invSection.CountAsync() > 0)
            await Expect(invSection.First).ToBeVisibleAsync(new() { Timeout = 5_000 });

        var body = await Page.InnerTextAsync("body");
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
        await ClickUntilUrlAsync(card, @"/my-cases/[0-9a-f\-]+");
        await WaitUntilLoadedAsync();

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
        await ClickUntilUrlAsync(Page.Locator(".card").Filter(new() { HasText = "Assigned" }).First,
                                 @"/my-requests/[0-9a-f\-]+");
        await WaitUntilLoadedAsync();

        // "View My Case →" when the request has a linked case, "View My Cases →" when it does not.
        // The plural-only match missed the very state this test is about — an assigned request is
        // exactly the one that has a case.
        var viewCasesBtn = Main.GetByText("View My Case", new() { Exact = false }).First;
        await Expect(viewCasesBtn).ToBeVisibleAsync(new() { Timeout = 8_000 });
    }
}
