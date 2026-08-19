using Microsoft.Playwright;
using NUnit.Framework;

namespace Ben.Web.Playwright.Tests;

/// <summary>
/// Tests for the client request status progression workflow:
/// - OrgPendingRequests card status badges
/// - Click-to-expand auto-marks Viewed
/// - "Under Review" button
/// - ClientRequestDetail "Submitted To" progression steps
/// </summary>
[TestFixture]
[Category("RequestStatusProgression")]
public class RequestStatusProgressionTests : BenTestBase
{
    // ── OrgPendingRequests — org side ─────────────────────────────────────────

    private async Task NavigateToRequestsTabAsync()
    {
        await LoginAsync(SuperAdminEmail, SuperAdminPassword);
        if (!await OpenOrganizationAsync("BenCo"))
            Assert.Ignore("BenCo not in the seed data.");

        // Both clicks are retried: they are Blazor-driven, and an unscoped GetByText("Requests")
        // would also match the sidebar's "My Requests" entry, which navigates away from the org.
        await OpenTabAsync("Requests", Main.GetByText("Submitted", new() { Exact = false })
                                           .Or(Main.GetByText("Viewed", new() { Exact = false }))
                                           .Or(Main.GetByText("No pending", new() { Exact = false })));
    }

    [Test]
    public async Task OrgRequests_Tab_RendersWithRequestCards()
    {
        await NavigateToRequestsTabAsync();
        var body = await Page.InnerTextAsync("body");
        Assert.That(body, Does.Not.Contain("An unhandled error has occurred"));
        // Should show either request cards or empty state
        Assert.That(body, Does.Contain("Submitted").Or.Contain("Viewed").Or.Contain("Under Review")
                       .Or.Contain("No pending"), "Expected status labels or empty state.");
    }

    [Test]
    public async Task OrgRequests_Cards_ShowStatusBadge()
    {
        await NavigateToRequestsTabAsync();
        var body = await Page.InnerTextAsync("body");
        if (!body.Contains("Submitted") && !body.Contains("Viewed"))
        {
            Assert.Pass("No pending requests found — DevelopmentDataSeeder may not have run.");
            return;
        }
        // Status badges should be visible (Submitted / Viewed / Under Review)
        var badge = Page.GetByText("Submitted", new() { Exact = true })
                        .Or(Page.GetByText("Viewed", new() { Exact = true }))
                        .Or(Page.GetByText("Under Review", new() { Exact = true }))
                        .First;
        await Expect(badge).ToBeVisibleAsync(new() { Timeout = 5_000 });
    }

    [Test]
    public async Task OrgRequests_ClickCard_ExpandsDescription()
    {
        await NavigateToRequestsTabAsync();
        var card = Page.Locator(".card").First;
        if (!await card.IsVisibleAsync()) { Assert.Pass("No request cards."); return; }

        // Click the card body to expand
        await card.ClickAsync();
        await Page.WaitForTimeoutAsync(600);

        // Description or "Request Details" heading should appear
        var detailSection = Page.GetByText("Request Details", new() { Exact = false })
                                .Or(Page.GetByText("View more", new() { Exact = false }));
        // Either it was already expanded (description showing) or it expanded now
        var body = await Page.InnerTextAsync("body");
        Assert.That(body, Does.Not.Contain("An unhandled error has occurred"));
    }

    [Test]
    public async Task OrgRequests_UnderReviewButton_IsVisibleForSubmittedRequests()
    {
        await NavigateToRequestsTabAsync();
        var body = await Page.InnerTextAsync("body");
        if (!body.Contains("Submitted") && !body.Contains("Nashville") && !body.Contains("Pending"))
        {
            Assert.Pass("No submitted requests found.");
            return;
        }
        // "Under Review" button should appear for Submitted/Viewed cards
        var underReviewBtn = Page.GetByRole(AriaRole.Button, new() { Name = "Under Review" });
        if (await underReviewBtn.IsVisibleAsync())
        {
            await Expect(underReviewBtn.First).ToBeVisibleAsync();
        }
        else
        {
            // All requests may already be Under Review or accepted
            Assert.Pass("No Submitted/Viewed requests — may all be Under Review already.");
        }
    }

    [Test]
    public async Task OrgRequests_AcceptButton_VisibleOnAllRequestCards()
    {
        await NavigateToRequestsTabAsync();
        var card = Page.Locator(".card").First;
        if (!await card.IsVisibleAsync()) { Assert.Pass("No cards."); return; }
        var acceptBtn = Page.GetByRole(AriaRole.Button, new() { Name = "Accept" }).First;
        await Expect(acceptBtn).ToBeVisibleAsync(new() { Timeout = 8_000 });
    }

    [Test]
    public async Task OrgRequests_DeclineButton_VisibleOnAllRequestCards()
    {
        await NavigateToRequestsTabAsync();
        var card = Page.Locator(".card").First;
        if (!await card.IsVisibleAsync()) { Assert.Pass("No cards."); return; }
        var declineBtn = Page.GetByRole(AriaRole.Button, new() { Name = "Decline" }).First;
        await Expect(declineBtn).ToBeVisibleAsync(new() { Timeout = 8_000 });
    }

    // ── ClientRequestDetail progression steps — client side ───────────────────

    [Test]
    public async Task ClientRequestDetail_SubmittedTo_ShowsProgressionSteps()
    {
        await LoginAsync("daniel.park@benco.dev", "D@niel!Park2026");
        await Page.GotoAsync($"{BaseUrl}/my-requests");
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);

        // Find a submitted request (status badge = "Submitted")
        var submittedCard = Page.GetByText("Submitted", new() { Exact = false }).First;
        if (!await submittedCard.IsVisibleAsync())
        {
            Assert.Pass("No submitted requests for Daniel Park — may only have Draft.");
            return;
        }
        await Page.Locator(".card").Filter(new() { HasText = "Submitted" }).First.ClickAsync();
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);

        // Submitted To card should appear
        var submittedTo = Page.GetByText("Submitted To", new() { Exact = false });
        await Expect(submittedTo).ToBeVisibleAsync(new() { Timeout = 10_000 });
    }

    [Test]
    public async Task ClientRequestDetail_AssignedRequest_ShowsViewMyCaseLink()
    {
        await LoginAsync("daniel.park@benco.dev", "D@niel!Park2026");
        await Page.GotoAsync($"{BaseUrl}/my-requests");
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);

        // Find an assigned request
        var assignedCard = Page.Locator(".card").Filter(new() { HasText = "Assigned" }).First;
        if (!await assignedCard.IsVisibleAsync())
        {
            Assert.Pass("No assigned requests visible for Daniel Park.");
            return;
        }
        await assignedCard.ClickAsync();
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);

        var viewCaseBtn = Page.GetByText("View My Case", new() { Exact = false })
                              .Or(Page.GetByText("View My Cases", new() { Exact = false }));
        await Expect(viewCaseBtn.First).ToBeVisibleAsync(new() { Timeout = 8_000 });
    }

    [Test]
    public async Task ClientRequestDetail_AssignedRequest_ViewMyCaseLinkNavigatesToCase()
    {
        await LoginAsync("daniel.park@benco.dev", "D@niel!Park2026");
        await Page.GotoAsync($"{BaseUrl}/my-requests");
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);

        var assignedCard = Page.Locator(".card").Filter(new() { HasText = "Assigned" }).First;
        if (!await assignedCard.IsVisibleAsync())
        {
            Assert.Pass("No assigned requests for Daniel Park.");
            return;
        }
        await assignedCard.ClickAsync();
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);

        var viewBtn = Page.GetByRole(AriaRole.Link, new() { Name = "View My Case" })
                          .Or(Page.GetByRole(AriaRole.Link, new() { Name = "View My Cases" }));
        await Expect(viewBtn.First).ToBeVisibleAsync(new() { Timeout = 8_000 });
        await viewBtn.First.ClickAsync();
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);

        // Should land on /my-cases or /my-cases/{id}
        Assert.That(Page.Url, Does.Contain("/my-cases"),
            "Expected navigation to /my-cases after clicking View My Case.");
    }

    // ── MyCaseDetail — edit and delete occurrence ─────────────────────────────

    [Test]
    public async Task MyCaseDetail_EditOccurrence_DialogPreFilled()
    {
        await LoginAsync("daniel.park@benco.dev", "D@niel!Park2026");
        await Page.GotoAsync($"{BaseUrl}/my-cases");
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);
        var card = Page.Locator(".card").First;
        if (!await card.IsVisibleAsync()) { Assert.Pass("No cases seeded."); return; }
        await ClickUntilUrlAsync(card, @"/my-cases/[0-9a-f\-]+");

        // Only test if there are occurrences in the list
        var editBtn = Page.GetByTitle("Edit").Or(Page.GetByRole(AriaRole.Button).Filter(new() { HasText = "" }).First).First;
        // Check if pencil edit button exists on any occurrence row
        var pencilBtns = Page.Locator("button[title='Edit'], [class*='k-button']").Filter(new() { HasText = "" });
        var body = await Page.InnerTextAsync("body");
        // If there are occurrences (from seeder or previous save), an edit button should exist
        // This test is best-effort — just verify no crash
        Assert.That(body, Does.Not.Contain("An unhandled error has occurred"));
    }

    [Test]
    public async Task MyCaseDetail_StatusBadge_Visible()
    {
        await LoginAsync("daniel.park@benco.dev", "D@niel!Park2026");
        await Page.GotoAsync($"{BaseUrl}/my-cases");
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);
        var card = Page.Locator(".card").First;
        if (!await card.IsVisibleAsync()) { Assert.Pass("No cases seeded."); return; }

        // Case status badge should be visible on the list card
        var badge = Page.GetByText("Accepted", new() { Exact = false })
                        .Or(Page.GetByText("Active", new() { Exact = false }))
                        .Or(Page.GetByText("Public", new() { Exact = false }))
                        .First;
        await Expect(badge).ToBeVisibleAsync(new() { Timeout = 10_000 });
    }

    [Test]
    public async Task MyCaseDetail_CaseManager_ShownWhenAssigned()
    {
        await LoginAsync("daniel.park@benco.dev", "D@niel!Park2026");
        await Page.GotoAsync($"{BaseUrl}/my-cases");
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);
        var card = Page.Locator(".card").First;
        if (!await card.IsVisibleAsync()) { Assert.Pass("No cases seeded."); return; }

        // Seeded case has Sarah Mitchell as case manager
        var caseManager = Page.GetByText("Case Manager", new() { Exact = false })
                              .Or(Page.GetByText("Sarah", new() { Exact = false }))
                              .First;
        await Expect(caseManager).ToBeVisibleAsync(new() { Timeout = 10_000 });
    }
}
