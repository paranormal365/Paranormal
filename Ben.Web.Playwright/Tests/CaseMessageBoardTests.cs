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
        await WaitUntilLoadedAsync();
    }

    /// <summary>
    /// A case nobody has written to yet opens, and stays open.
    /// </summary>
    /// <remarks>
    /// <para>The thread scrolls itself to its newest message after every load, through an element
    /// reference on the scrolling list. That list is only rendered when there ARE messages — an
    /// empty thread draws "No messages yet" instead — so on an empty one the reference was still
    /// default and the call handed JavaScript an element that had never existed. The resulting
    /// JSException was not among the exceptions the handler caught, so it ended the circuit:
    /// opening the Messages tab of an unwritten case replaced the page with "An unhandled error
    /// has occurred" (Ben, 2026-09-19, with the console open).</para>
    ///
    /// <para><b>It opens a case of its own, and that is the point.</b> Two earlier versions hunted
    /// the seed for an unwritten thread — first among the client's cases, then among the whole
    /// group's — and every single case already carries a conversation, so both ignored themselves
    /// against the unfixed component instead of failing. A regression test that cannot reach the
    /// state it is about is worse than none, so this one makes the state.</para>
    ///
    /// <para>Asserted on the error banner rather than on how the page looks, because the page keeps
    /// showing whatever it had drawn before the circuit died — the tab looked fine, and only the
    /// banner and a page that no longer answered said otherwise.</para>
    /// </remarks>
    [Test]
    public async Task AThreadWithNoMessagesOpensWithoutKillingThePage()
    {
        await LoginAsync(UserEmail, UserPassword);   // Sarah, who may open a case
        if (!await OpenOrganizationAsync("Paranormal365"))
        { Assert.Ignore("That group is not in the seed data."); return; }

        var orgId = System.Text.RegularExpressions.Regex.Match(Page.Url, @"/organizations/([0-9a-f\-]+)").Groups[1].Value;
        if (string.IsNullOrEmpty(orgId)) { Assert.Ignore("Could not read the group from the URL."); return; }

        await Page.GotoAsync($"{BaseUrl}/organizations/{orgId}/cases/new");
        await WaitForTheCircuitAsync();
        await WaitUntilLoadedAsync();

        var title = $"Unwritten thread {Guid.NewGuid():N}";
        await FillAndConfirmAsync("#casecreatepage-case-title-b1b1", title);
        await FillAndConfirmAsync("#casecreatepage-street-address-5b76", "200 Cragfont Rd");
        await FillAndConfirmAsync("#casecreatepage-city-4662", "Castalian Springs");
        await FillAndConfirmAsync("#casecreatepage-state-7b45", "TN");
        await FillAndConfirmAsync("#casecreatepage-zip-code-ba79", "37031");
        // A public location, so nothing here depends on the private-engagement plan gate.
        await Page.CheckAsync("#case-place-kind-public");

        // The title again, LAST.
        //
        // It is the first field on a freshly navigated page, and this page renders long before
        // the circuit connects: a value typed in that window is in the DOM and is then overwritten
        // by the first interactive render with the server's empty one. The keystroke is not
        // ignored, it is erased — the documented Blazor Server race — and Open Case then stays
        // disabled for want of a title, which is what made this test fail in a full run and pass
        // on its own.
        await FillAndConfirmAsync("#casecreatepage-case-title-b1b1", title);

        var open = Main.GetByRole(AriaRole.Button, new() { Name = "Open Case" })
                       .Or(Main.Locator("button.btn-primary")).First;

        // Enabled before pressed, so a form the page has not accepted says so here rather than as
        // a navigation that never happens.
        await Expect(open).ToBeEnabledAsync(new() { Timeout = 20_000 });

        await ClickUntilUrlAsync(open, @"/organizations/[0-9a-f\-]+/cases/[0-9a-f\-]+");
        await WaitUntilLoadedAsync();

        await OpenTabAsync("Messages", Compose);

        // Wait for the thread to SETTLE. It loads after the page does, so "No messages yet" is
        // absent for a moment even on a case that has none.
        var empty  = Page.GetByText("No messages yet.", new() { Exact = false });
        var bubble = Page.Locator("[data-testid=case-message-bubble]");
        for (var wait = 0; wait < 40; wait++)
        {
            if (await empty.CountAsync() > 0 || await bubble.CountAsync() > 0) break;
            await Page.WaitForTimeoutAsync(250);
        }

        await Expect(empty.First).ToBeVisibleAsync(new() { Timeout = 10_000 });

        // The whole point: an empty thread, and a circuit that is still alive.
        await Expect(Page.Locator("#blazor-error-ui")).ToBeHiddenAsync(new() { Timeout = 5_000 });

        // And still answering, which a terminated circuit cannot do.
        await Expect(Compose).ToBeVisibleAsync(new() { Timeout = 10_000 });
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
    // The compose box is a small formatting editor since 2026-09-14 (beta feedback), not a textarea.

    private ILocator Compose => Page.Locator("[data-testid='case-thread-compose'] [contenteditable='true']");
    private ILocator SendButton => Page.Locator("#case-thread-send");

    private async Task TypeMessageAsync(string text)
    {
        await Expect(Compose).ToBeVisibleAsync(new() { Timeout = 12_000 });
        await Compose.ClickAsync();
        await Page.Keyboard.TypeAsync(text);
    }

    [Test]
    public async Task ClientCaseDetail_MessagesPanel_ComposeBox_IsPresent()
    {
        await NavigateToClientCaseDetail();

        await Expect(Page.GetByText("Messages with your investigation group", new() { Exact = false }))
            .ToBeVisibleAsync(new() { Timeout = 12_000 });
        await Expect(Compose).ToBeVisibleAsync(new() { Timeout = 8_000 });
    }

    [Test]
    public async Task ClientCaseDetail_MessagesPanel_SendingNothing_SaysSo()
    {
        await NavigateToClientCaseDetail();
        await Expect(Compose).ToBeVisibleAsync(new() { Timeout = 12_000 });

        await SendButton.ClickAsync();
        await Expect(Page.GetByText("Type a message first.")).ToBeVisibleAsync(new() { Timeout = 8_000 });
    }

    [Test]
    public async Task ClientCaseDetail_MessagesPanel_CanSendMessage()
    {
        await NavigateToClientCaseDetail();

        var uniqueText = $"Playwright test message {Guid.NewGuid():N}";
        await TypeMessageAsync(uniqueText);
        await SendButton.ClickAsync();

        var sent = Page.Locator("[data-testid='case-message-bubble']", new() { HasTextString = uniqueText });
        await Expect(sent).ToBeVisibleAsync(new() { Timeout = 10_000 });
    }

    [Test]
    public async Task ClientCaseDetail_MessagesPanel_ComposeClearsAfterSend()
    {
        await NavigateToClientCaseDetail();

        var text = $"Temporary message to test compose clear {Guid.NewGuid():N}";
        await TypeMessageAsync(text);
        await SendButton.ClickAsync();

        // Sending is a SignalR message on the live circuit; Expect polls until the clear actually happens.
        await Expect(Page.Locator("[data-testid='case-message-bubble']", new() { HasTextString = text }))
            .ToBeVisibleAsync(new() { Timeout = 10_000 });
        await Expect(Compose).ToHaveTextAsync("", new() { Timeout = 10_000 });
    }

    // ── Org-side: Messages tab in CaseDetail ─────────────────────────────────

    [Test]
    public async Task OrgCaseDetail_MessagesTab_IsVisible()
    {
        await LoginAsync(UserEmail, UserPassword); // Sarah
        if (!await OpenOrgCaseAsync("Paranormal365", "Belmont"))
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
        if (!await OpenOrgCaseAsync("Paranormal365", "Belmont"))
        { Assert.Pass("TGH case not in the seed data."); return; }

        // The compose box is the one thing both sides of the thread render.
        await OpenTabAsync("Messages", Compose);

        // Daniel's reply should be visible to the org
        var clientMsg = Page.GetByText("activity has been a bit more frequent", new() { Exact = false });
        await Expect(clientMsg).ToBeVisibleAsync(new() { Timeout = 12_000 });
    }

    [Test]
    public async Task OrgCaseDetail_MessagesTab_CanSendMessage()
    {
        await LoginAsync(UserEmail, UserPassword); // Sarah
        if (!await OpenOrgCaseAsync("Paranormal365", "Belmont"))
        { Assert.Pass("TGH case not in the seed data."); return; }

        await OpenTabAsync("Messages", Compose);
        await WaitUntilLoadedAsync();

        var uniqueText = $"Org reply from Playwright {Guid.NewGuid():N}";
        await TypeMessageAsync(uniqueText);
        await SendButton.ClickAsync();

        var sent = Page.Locator("[data-testid='case-message-bubble']", new() { HasTextString = uniqueText });
        await Expect(sent).ToBeVisibleAsync(new() { Timeout = 10_000 });
    }

    /// <summary>
    /// What the group writes in bold arrives in the client's thread in bold — the formatted copy crosses the API, not
    /// only the plain one.
    /// </summary>
    [Test]
    public async Task Org_bold_reaches_the_client_as_bold()
    {
        await LoginAsync(UserEmail, UserPassword); // Sarah
        Assert.That(await OpenOrgCaseAsync("Paranormal365", "Belmont"), Is.True,
            "The seeded Belmont case, which carries the conversation, could not be opened.");

        await OpenTabAsync("Messages", Compose);
        await WaitUntilLoadedAsync();

        // Type, select what was typed, then press Bold — the way a person formats a word, and independent of
        // whether pressing a toolbar button first leaves the caret where it was.
        var marker = $"bold-{Guid.NewGuid():N}";
        await TypeMessageAsync(marker);
        await Page.Keyboard.PressAsync("ControlOrMeta+a");
        await Page.Locator("[data-testid='case-thread-compose']").GetByRole(AriaRole.Button, new() { Name = "Bold", Exact = true }).ClickAsync();
        await Expect(Compose.Locator("strong", new() { HasTextString = marker })).ToBeVisibleAsync(new() { Timeout = 5_000 });
        await SendButton.ClickAsync();
        await Expect(Page.Locator("[data-testid='case-message-bubble'] strong", new() { HasTextString = marker }))
            .ToBeVisibleAsync(new() { Timeout = 10_000 });

        await LogoutAsync();
        await NavigateToClientCaseDetail();

        await Expect(Page.Locator("[data-testid='case-message-bubble'] strong", new() { HasTextString = marker }))
            .ToBeVisibleAsync(new() { Timeout = 15_000 });
    }
}
