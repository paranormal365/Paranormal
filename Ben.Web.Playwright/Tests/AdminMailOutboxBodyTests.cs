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

        // The list opens on "given up on"; a fresh letter is waiting, not failed.
        var all = Page.GetByRole(AriaRole.Button, new() { Name = "All", Exact = true });
        if (await all.CountAsync() > 0) await all.First.ClickAsync();

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
            await Expect(Page.Locator("[data-testid=letter-scrubbed]")).ToBeVisibleAsync();
            Assert.Pass("The letter's words had been cleared, and the screen said so.");
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
    [Description("Somebody who is not a SuperAdmin cannot reach the mail screen at all.")]
    public async Task An_ordinary_member_cannot_reach_the_letters()
    {
        await LoginAsync(MemberEmail, MemberPassword);
        await Page.GotoAsync($"{BaseUrl}/admin/mail");
        await WaitForTheCircuitAsync();

        await Expect(Page.Locator("[data-testid=outbox-view]")).ToHaveCountAsync(0);
    }
}
