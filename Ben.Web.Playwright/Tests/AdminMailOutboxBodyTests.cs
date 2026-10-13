using Microsoft.Playwright;
using NUnit.Framework;

namespace Ben.Web.Playwright.Tests;

/// <summary>
/// Reading a letter the site sent, from the administration mail screen (item 245).
/// </summary>
/// <remarks>
/// <para><b>The letter is drawn in a sealed frame.</b> Mail carries whatever somebody typed into a
/// form, so an administration page that executed it would be a worse problem than the one it was
/// opened to diagnose. That is asserted here rather than only in the markup, because a sandbox
/// attribute is the kind of thing a later edit drops without noticing.</para>
///
/// <para><b>A letter is made, not found.</b> Asking for a password reset puts a real letter in the
/// outbox, so this does not depend on a seeded row that a database rebuild could take away.</para>
/// </remarks>
[TestFixture]
public class AdminMailOutboxBodyTests : BenTestBase
{
    /// <summary>Puts one real letter in the queue, through the door a person would use.</summary>
    private async Task EnqueueALetterAsync()
    {
        await Page.GotoAsync($"{BaseUrl}/forgot-password");
        await Expect(Page.Locator("#forgot-email")).ToBeVisibleAsync(new() { Timeout = 15_000 });
        await FillAndConfirmAsync("#forgot-email", UserEmail);
        await ClickUntilAsync(
            Page.GetByRole(AriaRole.Button, new() { Name = "Send reset link", Exact = false }),
            Page.GetByText("reset link is on its way", new() { Exact = false }));
    }

    private async Task OpenTheLettersAsync()
    {
        await Page.GotoAsync($"{BaseUrl}/admin/mail");
        await WaitForTheCircuitAsync();

        // The list opens on "Given up", and a letter queued a moment ago is WAITING — so it was
        // never in the list this test was reading. It used to press a button called "All", which
        // this screen has never had: the three filters are Given up, Waiting and Accepted, so the
        // click found nothing, did nothing, and the failure pointed at the missing Read-it button
        // several steps later.
        var waiting = Page.GetByRole(AriaRole.Button, new() { Name = "Waiting", Exact = true });
        await Expect(waiting).ToBeVisibleAsync(new() { Timeout = 20_000 });
        await ClickUntilAsync(waiting, Page.Locator("[data-testid=outbox-view]").First);

        await Expect(Page.Locator("[data-testid=outbox-view]").First)
            .ToBeVisibleAsync(new() { Timeout = 30_000 });
    }

    [Test]
    [Description("A letter can be read, and it is drawn in a frame that can run nothing.")]
    public async Task A_letter_opens_in_a_sealed_frame()
    {
        await EnqueueALetterAsync();
        await LoginAsync(SuperAdminEmail, SuperAdminPassword);
        await OpenTheLettersAsync();

        await ClickUntilAsync(
            Page.Locator("[data-testid=outbox-view]").First,
            Page.Locator("[data-testid=letter-body], [data-testid=letter-scrubbed]").First);

        var frame = Page.Locator("[data-testid=letter-body]");
        if (await frame.CountAsync() == 0)
        {
            // The only other honest outcome: its words were already cleared, which the panel says.
            // IGNORE rather than PASS — this test covers the sealed frame, and a run that never
            // saw one has not tested it. Ending green here would report a regression in the
            // sandbox as a pass (PlaywrightTestsCanFailTests, which caught exactly that).
            await Expect(Page.Locator("[data-testid=letter-scrubbed]")).ToBeVisibleAsync();
            Assert.Ignore("The newest letter's words had already been cleared, so there was no "
                        + "body to draw. Nothing about the frame was exercised.");
            return;
        }

        // sandbox="" — present AND empty. An empty value is the whole point: sandbox with
        // allow-scripts would be the attribute present and the protection gone.
        var sandbox = await frame.First.GetAttributeAsync("sandbox");
        Assert.That(sandbox, Is.EqualTo(string.Empty),
            "The letter frame must be sandboxed with nothing allowed.");

        // And the letter actually drew something.
        //
        // Read through Playwright's frame locator, NOT through page script: sandbox="" gives the
        // frame an opaque origin, so `el.contentDocument` from the page is null however well the
        // letter rendered. Asserting that way passes only when the sandbox is broken, which is the
        // exact opposite of what this test is for — it failed here first for that reason.
        var inside = Page.FrameLocator("[data-testid=letter-body]").Locator("body");
        await Expect(inside).ToBeVisibleAsync(new() { Timeout = 15_000 });

        var text = await inside.InnerTextAsync();
        Assert.That(text.Trim(), Is.Not.Empty, "The letter frame drew nothing.");
    }

    [Test]
    [Description("A test send of a picked letter appears in Recent letters, marked as a test.")]
    public async Task A_test_send_is_listed_and_says_it_is_a_test()
    {
        // An address of its own, so the row found is this run's whatever else is in the list.
        var to = $"mail-test-{Guid.NewGuid():N}@example.test";

        await LoginAsync(SuperAdminEmail, SuperAdminPassword);
        await Page.GotoAsync($"{BaseUrl}/admin/mail");
        await WaitForTheCircuitAsync();

        await Expect(Page.Locator("[data-testid=mail-test-to]")).ToBeVisibleAsync(new() { Timeout = 20_000 });
        await FillAndConfirmAsync("[data-testid=mail-test-to]", to);

        var picker = Page.Locator("[data-testid=mail-test-kind]");
        await Expect(picker.Locator("option[value='confirm-your-address']")).ToHaveCountAsync(1, new() { Timeout = 20_000 });
        await picker.SelectOptionAsync("confirm-your-address");
        await Expect(Page.Locator("[data-testid=mail-test-kind-note]")).ToBeVisibleAsync();

        await Page.Locator("[data-testid=mail-test-send]").ClickAsync();
        await Expect(Page.Locator("[data-testid=mail-test-result]")).ToBeVisibleAsync(new() { Timeout = 60_000 });

        // Whether this machine can send or not, the test is written down — accepted or given up.
        var row = Page.Locator("[data-testid=outbox-table] tr").Filter(new() { HasText = to });
        await Expect(row).ToHaveCountAsync(1, new() { Timeout = 20_000 });
        await Expect(row).ToContainTextAsync("[Test] Confirm your email");

        await ClickUntilAsync(row.Locator("[data-testid=outbox-view]"), Page.Locator("[data-testid=letter-body]"));
        var inside = Page.FrameLocator("[data-testid=letter-body]").Locator("body");
        await Expect(inside).ToContainTextAsync("This is a test.", new() { Timeout = 15_000 });
        await Expect(inside).ToContainTextAsync("Marguerite");
    }

    [Test]
    [Description("Somebody who is not a SuperAdmin cannot reach the mail screen at all.")]
    public async Task An_ordinary_member_cannot_reach_the_letters()
    {
        await LoginAsync(MemberEmail, MemberPassword);
        await Page.GotoAsync($"{BaseUrl}/admin/mail");
        await WaitForTheCircuitAsync();

        await Expect(Page.Locator("[data-testid=outbox-view]")).ToHaveCountAsync(0);
    }
}
