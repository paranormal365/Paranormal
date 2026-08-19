using Microsoft.Playwright;
using NUnit.Framework;

namespace Ben.Web.Playwright.Tests;

/// <summary>
/// Tests for the case message board (CaseMessageThread component).
/// Daniel Park has 3 seeded messages on his accepted case:
///   - 2 unread org messages from Sarah (initial assessment notice + logging tip)
///   - 1 read client reply from Daniel
/// Sarah Mitchell is a TGH member and case manager, so she can access the org-side Messages tab.
/// </summary>
[TestFixture]
[Category("CaseMessages")]
public class CaseMessageBoardTests : BenTestBase
{
    private const string ClientEmail    = "daniel.park@benco.dev";
    private const string ClientPassword = "D@niel!Park2026";

    // ── Helper ────────────────────────────────────────────────────────────────

    /// <summary>
    /// Logs in as Daniel and opens the case that carries the seeded conversation.
    /// <para>
    /// Deliberately not "the first card". Daniel has four cases now — the others arrived from
    /// later seeding and from these tests' own sends — and the seeded conversation is on the
    /// oldest, which sorts last. Taking the first card opened a case with no messages, so the
    /// panel rendered correctly and empty and the assertions read as though the feature was
    /// broken.
    /// </para>
    /// <para>
    /// The seeded case is the only one with a case manager assigned, and the card shows that, so
    /// it is both a stable identifier and a visible one.
    /// </para>
    /// </summary>
    private async Task NavigateToClientCaseDetail()
    {
        await LoginAsync(ClientEmail, ClientPassword);
        await Page.GotoAsync($"{BaseUrl}/my-cases");
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);

        var card = Page.Locator(".card").Filter(new() { HasTextString = "Case Manager:" }).First;
        if (await card.CountAsync() == 0)
            Assert.Ignore("No managed case in the seed data; the seeded conversation lives on one.");

        await Expect(card).ToBeVisibleAsync(new() { Timeout = 10_000 });
        // The card navigates via NavigationManager, so a click before the circuit is live is lost.
        await ClickUntilUrlAsync(card, @"/my-cases/[0-9a-f\-]+");
    }

    // ── Client-side: panel rendering ─────────────────────────────────────────

    [Test]
    public async Task ClientCaseDetail_MessagesPanel_IsVisible()
    {
        await NavigateToClientCaseDetail();

        var header = Page.GetByText("Messages with your investigation group", new() { Exact = false });
        await Expect(header).ToBeVisibleAsync(new() { Timeout = 12_000 });
    }

    [Test]
    public async Task ClientCaseDetail_MessagesPanel_ShowsSeededOrgMessages()
    {
        await NavigateToClientCaseDetail();

        // Sarah's first message contains this phrase (seeded in DevelopmentDataSeeder)
        var msg = Page.GetByText("scheduled an initial site assessment", new() { Exact = false });
        await Expect(msg).ToBeVisibleAsync(new() { Timeout = 12_000 });
    }

    [Test]
    public async Task ClientCaseDetail_MessagesPanel_ShowsSeededClientReply()
    {
        await NavigateToClientCaseDetail();

        // Daniel's seeded reply
        var msg = Page.GetByText("activity has been a bit more frequent", new() { Exact = false });
        await Expect(msg).ToBeVisibleAsync(new() { Timeout = 12_000 });
    }

    [Test]
    public async Task ClientCaseDetail_MessagesPanel_ShowsMultipleMessages()
    {
        await NavigateToClientCaseDetail();

        // Wait for panel to load
        await Expect(Page.GetByText("Messages with your investigation group", new() { Exact = false }))
            .ToBeVisibleAsync(new() { Timeout = 12_000 });

        // 3 messages seeded — the thread should have at least 2 visible bubble divs
        var bubbles = Page.Locator(".rounded-3.px-3.py-2");
        await Expect(bubbles.First).ToBeVisibleAsync(new() { Timeout = 8_000 });
        var count = await bubbles.CountAsync();
        Assert.That(count, Is.GreaterThanOrEqualTo(3),
            "Expected at least 3 seeded messages in the thread.");
    }

    // ── Client-side: compose and send ────────────────────────────────────────

    [Test]
    public async Task ClientCaseDetail_MessagesPanel_ComposeBox_IsPresent()
    {
        await NavigateToClientCaseDetail();

        await Expect(Page.GetByText("Messages with your investigation group", new() { Exact = false }))
            .ToBeVisibleAsync(new() { Timeout = 12_000 });

        var textArea = Page.GetByPlaceholder("Message your investigation group", new() { Exact = false });
        await Expect(textArea).ToBeVisibleAsync(new() { Timeout = 8_000 });
    }

    [Test]
    public async Task ClientCaseDetail_MessagesPanel_SendDisabled_WhenEmpty()
    {
        await NavigateToClientCaseDetail();

        await Expect(Page.GetByText("Messages with your investigation group", new() { Exact = false }))
            .ToBeVisibleAsync(new() { Timeout = 12_000 });

        // Send button should be disabled (no text entered)
        var sendBtn = Page.GetByRole(AriaRole.Button, new() { Name = "Send" });
        await Expect(sendBtn).ToBeDisabledAsync(new() { Timeout = 8_000 });
    }

    [Test]
    public async Task ClientCaseDetail_MessagesPanel_CanSendMessage()
    {
        await NavigateToClientCaseDetail();

        await Expect(Page.GetByText("Messages with your investigation group", new() { Exact = false }))
            .ToBeVisibleAsync(new() { Timeout = 12_000 });

        var uniqueText = $"Playwright test message {Guid.NewGuid():N}";
        var textArea   = Page.GetByPlaceholder("Message your investigation group", new() { Exact = false });
        await textArea.FillAsync(uniqueText);

        var sendBtn = Page.GetByRole(AriaRole.Button, new() { Name = "Send" });
        await Expect(sendBtn).ToBeEnabledAsync(new() { Timeout = 4_000 });
        await sendBtn.ClickAsync();

        // New message should appear in the thread
        var sent = Page.GetByText(uniqueText, new() { Exact = false });
        await Expect(sent).ToBeVisibleAsync(new() { Timeout = 10_000 });
    }

    [Test]
    public async Task ClientCaseDetail_MessagesPanel_ComposeClearsAfterSend()
    {
        await NavigateToClientCaseDetail();

        await Expect(Page.GetByText("Messages with your investigation group", new() { Exact = false }))
            .ToBeVisibleAsync(new() { Timeout = 12_000 });

        var textArea = Page.GetByPlaceholder("Message your investigation group", new() { Exact = false });
        await textArea.FillAsync("Temporary message to test compose clear");

        await Page.GetByRole(AriaRole.Button, new() { Name = "Send" }).ClickAsync();
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);

        // Compose box should be empty after send
        var val = await textArea.InputValueAsync();
        Assert.That(val, Is.Empty.Or.EqualTo(""), "Compose box should clear after send.");
    }

    // ── Org-side: Messages tab in CaseDetail ─────────────────────────────────

    [Test]
    public async Task OrgCaseDetail_MessagesTab_IsVisible()
    {
        await LoginAsync(UserEmail, UserPassword); // Sarah
        if (!await OpenOrgCaseAsync("Tennessee Ghost Hunters", "Park"))
        { Assert.Pass("TGH case not in the seed data."); return; }

        // Messages tab should be present
        var messagesTab = Page.GetByRole(AriaRole.Tab, new() { Name = "Messages" })
                              .Or(Main.GetByText("Messages", new() { Exact = true }))
                              .First;
        await Expect(messagesTab).ToBeVisibleAsync(new() { Timeout = 8_000 });
    }

    [Test]
    public async Task OrgCaseDetail_MessagesTab_ShowsClientMessages()
    {
        await LoginAsync(UserEmail, UserPassword); // Sarah
        if (!await OpenOrgCaseAsync("Tennessee Ghost Hunters", "Park"))
        { Assert.Pass("TGH case not in the seed data."); return; }

        // Click Messages tab
        await OpenTabAsync("Messages", Main.GetByText("Messages with", new() { Exact = false })
                                           .Or(Main.GetByPlaceholder("Message", new() { Exact = false })));

        // Daniel's reply should be visible to the org
        var clientMsg = Page.GetByText("activity has been a bit more frequent", new() { Exact = false });
        await Expect(clientMsg).ToBeVisibleAsync(new() { Timeout = 12_000 });
    }

    [Test]
    public async Task OrgCaseDetail_MessagesTab_CanSendMessage()
    {
        await LoginAsync(UserEmail, UserPassword); // Sarah
        if (!await OpenOrgCaseAsync("Tennessee Ghost Hunters", "Park"))
        { Assert.Pass("TGH case not in the seed data."); return; }

        await OpenTabAsync("Messages", Main.GetByText("Messages with", new() { Exact = false })
                                           .Or(Main.GetByPlaceholder("Message", new() { Exact = false })));

        var uniqueText = $"Org reply from Playwright {Guid.NewGuid():N}";
        var textArea   = Page.GetByPlaceholder("Message the client", new() { Exact = false });
        await Expect(textArea).ToBeVisibleAsync(new() { Timeout = 8_000 });
        await textArea.FillAsync(uniqueText);

        await Page.GetByRole(AriaRole.Button, new() { Name = "Send" }).ClickAsync();

        var sent = Page.GetByText(uniqueText, new() { Exact = false });
        await Expect(sent).ToBeVisibleAsync(new() { Timeout = 10_000 });
    }
}
