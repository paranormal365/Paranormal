using Microsoft.Playwright;
using NUnit.Framework;

namespace Ben.Web.Playwright.Tests;

/// <summary>
/// A group's internal mail: the folder rail, the message rows, and the direct message that could
/// not be sent before this.
/// </summary>
/// <remarks>
/// <para>Every test here signs in as <b>James</b> rather than the usual test account, and that is
/// the point of them. Sarah, the account the rest of the suite uses, is BenCo's owner; James is an
/// ordinary member. Three separate faults in this feature were invisible to an owner and total to
/// everyone else — the organisation page refusing members outright, the recipient list being
/// fetched from an org-admin-only endpoint, and the fetch never being triggered at all. A suite
/// that only ever signs in as the owner cannot see any of them.</para>
/// </remarks>
[TestFixture]
[Category("Messaging")]
public class MessagingTests : BenTestBase
{
    private const string OrgName = "BenCo";

    /// <summary>An ordinary member — deliberately not the owner. See the remarks above.</summary>
    private static string MemberEmail    => Environment.GetEnvironmentVariable("BEN_USER2_EMAIL")    ?? "james.thornton@benco.dev";
    private static string MemberPassword => Environment.GetEnvironmentVariable("BEN_USER2_PASSWORD") ?? "J@mes!Thornton26";

    private ILocator Compose => Main.GetByRole(AriaRole.Button, new() { Name = "Compose" });

    private async Task<bool> OpenMessagesAsync()
    {
        if (!await OpenOrganizationAsync(OrgName)) return false;
        await OpenTabAsync("Messages", Compose);
        await WaitUntilLoadedAsync();
        return true;
    }

    /// <summary>
    /// An ordinary member can open their own group at all.
    /// </summary>
    /// <remarks>
    /// <c>GET /api/organizations/{id}</c> is the organisation hub's first call, and it used to
    /// require Read access through the org security service — which returns true for Owners and
    /// Administrators and then falls through to explicit grants. A plain member had none, so the
    /// hub told three of BenCo's four members "Organization not found or you do not have access"
    /// about a group they belong to. Nothing else on the hub could be reached to fail.
    /// </remarks>
    [Test]
    public async Task An_ordinary_member_can_open_their_own_group()
    {
        await LoginAsync(MemberEmail, MemberPassword);
        if (!await OpenOrganizationAsync(OrgName)) Assert.Ignore($"No organisation named {OrgName} in this database.");

        await Expect(Main.GetByText("do not have access", new() { Exact = false }))
            .ToHaveCountAsync(0);
        await Expect(Main.GetByRole(AriaRole.Tab, new() { Name = "Messages", Exact = true }))
            .ToBeVisibleAsync(new() { Timeout = 10_000 });
    }

    [Test]
    public async Task The_folder_rail_offers_every_channel()
    {
        await LoginAsync(MemberEmail, MemberPassword);
        if (!await OpenMessagesAsync()) Assert.Ignore($"No organisation named {OrgName} in this database.");

        foreach (var folder in new[] { "Inbox", "Sent", "Broadcasts", "Direct", "Case teams", "Public" })
        {
            await Expect(Main.GetByRole(AriaRole.Button, new() { Name = folder, Exact = false }).First)
                .ToBeVisibleAsync(new() { Timeout = 10_000 });
        }
    }

    /// <summary>
    /// The rows carry the template's mail classes, which is what makes unread state visible.
    /// </summary>
    /// <remarks>
    /// The bold weight itself comes from the stylesheet — <c>.unread .mail-sender { font-weight:
    /// 600 }</c> — so what has to hold is that the <c>unread</c> class lands on the row's
    /// <c>li</c>, and stops being there once the message is read. Asserting on the computed weight
    /// would be asserting on the template's own CSS, which is not ours to test.
    /// </remarks>
    [Test]
    public async Task An_unread_message_is_marked_unread_and_stops_being_so_once_read()
    {
        // Seeded rather than assumed: this used to skip whenever the inbox happened to be clear,
        // which meant it could go a long time proving nothing.
        var subject = $"Unread test {Guid.NewGuid():N}"[..24];
        await BroadcastToOrgAsync(subject);

        await LoginAsync(MemberEmail, MemberPassword);
        if (!await OpenMessagesAsync()) Assert.Ignore($"No organisation named {OrgName} in this database.");

        var unread = Main.Locator("li.unread .mail-row", new() { HasTextString = subject }).First;
        await Expect(unread).ToBeVisibleAsync(new() { Timeout = 15_000 });
        await unread.ClickAsync();

        // The reading pane opens below the list, showing the message that was clicked.
        await Expect(Main.GetByRole(AriaRole.Heading, new() { Name = subject }))
            .ToBeVisibleAsync(new() { Timeout = 10_000 });

        // And that row is no longer unread, because opening a message marks it read.
        await Expect(Main.Locator("li.unread .mail-subject", new() { HasTextString = subject }))
            .ToHaveCountAsync(0, new() { Timeout = 10_000 });
    }

    /// <summary>
    /// The recipient picker offers the group's members to a member.
    /// </summary>
    /// <remarks>
    /// Two faults met here. The list was fetched from the membership-administration endpoint,
    /// which refuses anyone who is not an org admin; and the fetch was hung off a handler the
    /// channel dropdown never called, so it stayed on "Loading members…" indefinitely. Both
    /// failed quietly — one behind a catch that reported "no other active members", the other
    /// behind a placeholder that never resolved — which is why this asserts on names being
    /// present rather than on the absence of an error.
    /// </remarks>
    [Test]
    public async Task Choosing_direct_message_offers_the_groups_members_as_recipients()
    {
        await LoginAsync(MemberEmail, MemberPassword);
        if (!await OpenMessagesAsync()) Assert.Ignore($"No organisation named {OrgName} in this database.");

        await Compose.First.ClickAsync();
        await SelectChannelAsync("Direct Message");

        var recipients = Main.Locator("input[id^='rcpt-']");
        await Expect(recipients.First).ToBeVisibleAsync(new() { Timeout = 15_000 });

        Assert.That(await recipients.CountAsync(), Is.GreaterThan(0),
            "The recipient picker was empty for an ordinary member — the person most likely to be "
            + "sending a direct message.");

        // The sender is not offered as a recipient of their own message.
        var names = await Main.Locator("label[for^='rcpt-']").AllInnerTextsAsync();
        Assert.That(names.Select(n => n.Trim()), Has.None.EqualTo("James Thornton"));
    }

    /// <summary>
    /// A direct message reaches the person it was addressed to.
    /// </summary>
    /// <remarks>
    /// The compose form offered "Direct Message" while sending <c>RecipientUserIds: []</c>, so the
    /// message went to nobody and looked sent. The only test that can catch that is one that goes
    /// and reads it as the recipient, which is what the second half of this does.
    /// </remarks>
    [Test]
    public async Task A_direct_message_reaches_the_person_it_was_addressed_to()
    {
        var subject = $"DM test {Guid.NewGuid():N}"[..20];

        await LoginAsync(MemberEmail, MemberPassword);
        if (!await OpenMessagesAsync()) Assert.Ignore($"No organisation named {OrgName} in this database.");

        await Compose.First.ClickAsync();
        await SelectChannelAsync("Direct Message");

        var recipient = Main.Locator("label[for^='rcpt-']", new() { HasTextString = "Sarah" }).First;
        await Expect(recipient).ToBeVisibleAsync(new() { Timeout = 15_000 });
        await recipient.ClickAsync();   // the label, because the checkbox is a form-check input

        await Main.Locator("input[id^='orgmessages-subject']").FillAsync(subject);
        await TypeIntoEditorAsync("Sent by an automated test.");

        await Main.GetByRole(AriaRole.Button, new() { Name = "Send" }).First.ClickAsync();
        await Expect(Main.GetByRole(AriaRole.Button, new() { Name = "Send" }))
            .ToHaveCountAsync(0, new() { Timeout = 20_000 });

        // Now read it as the person it was addressed to. Before the fix this inbox stayed empty:
        // the message existed, addressed to nobody.
        await LogoutAsync();
        await LoginAsync(UserEmail, UserPassword);
        if (!await OpenMessagesAsync()) Assert.Ignore($"No organisation named {OrgName} in this database.");

        await Expect(Main.GetByText(subject).First).ToBeVisibleAsync(new() { Timeout = 20_000 });
    }

    /// <summary>Platform messages use the same rows as a group's inbox.</summary>
    [Test]
    public async Task The_notifications_page_renders_messages_as_mail_rows()
    {
        var subject = $"Platform test {Guid.NewGuid():N}"[..26];
        var seeded = await SendPlatformMessageAsync(subject);
        if (!seeded) Assert.Ignore("Could not seed a platform message; the admin API refused.");

        await LoginAsync(UserEmail, UserPassword);
        await Page.GotoAsync($"{BaseUrl}/notifications");
        await WaitUntilLoadedAsync();

        // Unread, and marked as such by the same class the group inbox uses — a freshly delivered
        // platform message has never been read.
        var row = Main.Locator("li.unread .mail-row", new() { HasTextString = subject }).First;
        await Expect(row).ToBeVisibleAsync(new() { Timeout = 15_000 });

        // Opening it shows the body below the list rather than in a dialog.
        await row.ClickAsync();
        await Expect(Main.GetByRole(AriaRole.Heading, new() { Name = subject }))
            .ToBeVisibleAsync(new() { Timeout = 10_000 });
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Signs in against the API and returns a bearer token.
    /// </summary>
    private async Task<string> ApiTokenAsync(string email, string password)
    {
        var api = await Playwright.APIRequest.NewContextAsync(new() { BaseURL = ApiUrl });
        var response = await api.PostAsync("/login", new()
        {
            DataObject = new Dictionary<string, object> { ["email"] = email, ["password"] = password },
        });
        Assert.That(response.Ok, Is.True, $"API sign-in failed for {email}: {response.Status}");

        var json = await response.JsonAsync();
        return json!.Value.GetProperty("accessToken").GetString()!;
    }

    private async Task<IAPIRequestContext> ApiAsAsync(string email, string password)
    {
        var token = await ApiTokenAsync(email, password);
        return await Playwright.APIRequest.NewContextAsync(new()
        {
            BaseURL = ApiUrl,
            ExtraHTTPHeaders = new Dictionary<string, string> { ["Authorization"] = $"Bearer {token}" },
        });
    }

    /// <summary>Posts a broadcast to BenCo as the owner, so a member has something unread.</summary>
    private async Task BroadcastToOrgAsync(string subject)
    {
        var api = await ApiAsAsync(UserEmail, UserPassword);

        var orgs = await (await api.GetAsync("/api/organizations")).JsonAsync();
        var orgId = orgs!.Value.EnumerateArray()
            .First(o => o.GetProperty("name").GetString() == OrgName)
            .GetProperty("id").GetString();

        var sent = await api.PostAsync($"/api/organizations/{orgId}/messages", new()
        {
            DataObject = new Dictionary<string, object?>
            {
                ["channelType"]      = 0,          // Broadcast — reaches every member
                ["subject"]          = subject,
                ["body"]             = "<p>Seeded by an automated test.</p>",
                ["isEncrypted"]      = false,
                ["parentMessageId"]  = null,
                ["caseId"]           = null,
                ["recipientUserIds"] = Array.Empty<string>(),
            },
        });
        Assert.That(sent.Ok, Is.True, $"Could not seed a broadcast: {sent.Status} {await sent.TextAsync()}");
    }

    /// <summary>
    /// Sends a platform message to the test user as SuperAdmin, in two parts — the message and the
    /// row addressing it to someone. Returns false if the admin API is unavailable, so the caller
    /// can skip rather than fail for an unrelated reason.
    /// </summary>
    private async Task<bool> SendPlatformMessageAsync(string subject)
    {
        var api = await ApiAsAsync(SuperAdminEmail, SuperAdminPassword);

        var types = await api.GetAsync("/api/admin/user-message-types");
        if (!types.Ok) return false;
        var typeId = (await types.JsonAsync())!.Value.EnumerateArray().FirstOrDefault()
                      .GetProperty("id").GetString();
        if (typeId is null) return false;

        var users = await api.GetAsync("/api/admin/app-users");
        if (!users.Ok) return false;
        var recipient = (await users.JsonAsync())!.Value.EnumerateArray()
            .FirstOrDefault(u => u.GetProperty("email").GetString() == UserEmail);
        if (recipient.ValueKind == System.Text.Json.JsonValueKind.Undefined) return false;

        var created = await api.PostAsync("/api/admin/user-messages", new()
        {
            DataObject = new Dictionary<string, object?>
            {
                ["userMessageTypeId"] = typeId,
                ["messageSubject"]    = subject,
                ["messageBody"]       = "<p>Seeded by an automated test.</p>",
            },
        });
        if (!created.Ok) return false;

        var messageId = (await created.JsonAsync())!.Value.GetProperty("id").GetString();
        var addressed = await api.PostAsync("/api/admin/user-message-tos", new()
        {
            DataObject = new Dictionary<string, object?>
            {
                ["messageId"]   = messageId,
                ["toAppUserId"] = recipient.GetProperty("id").GetString(),
            },
        });
        return addressed.Ok;
    }

    /// <summary>
    /// Picks a channel. BenSelect renders a native &lt;select&gt;, so this is a real change event
    /// and the component's OnChange handler runs — which is what fetches the recipient list.
    /// </summary>
    private async Task SelectChannelAsync(string label)
    {
        var channel = Main.Locator("select").First;
        await Expect(channel).ToBeVisibleAsync(new() { Timeout = 10_000 });
        await channel.SelectOptionAsync(new SelectOptionValue { Label = label });
    }

    /// <summary>
    /// Types into the rich-text body.
    /// </summary>
    /// <remarks>
    /// Real typing, not a synthetic value assignment: setting an editor's value from script
    /// updates the DOM without ever reaching the bound C# field, so Send would stay disabled and
    /// the test would fail for a reason that has nothing to do with messaging. Clicking and typing
    /// produces the events the binding actually listens for.
    /// </remarks>
    private async Task TypeIntoEditorAsync(string text)
    {
        var frame = Page.FrameLocator(".k-editor iframe");
        var body  = frame.Locator("body");

        await body.ClickAsync();
        await body.PressSequentiallyAsync(text);

        // The binding updates on blur, so move focus off the editor before pressing Send.
        await Main.Locator("input[id^='orgmessages-subject']").ClickAsync();
    }
}
