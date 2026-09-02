using Microsoft.Playwright;
using NUnit.Framework;

namespace Ben.Web.Playwright.Tests;

/// <summary>
/// Changing a password has to finish visibly.
/// </summary>
/// <remarks>
/// Found 2026-09-02 on the live site: the button stuck on "Saving…" for ever. The endpoint answers
/// <c>NoContent</c>, the client's <c>SendExpectingReasonAsync</c> called <c>ReadFromJsonAsync</c>
/// on the empty body and threw, and because nothing caught it the panel's busy flag was never
/// cleared — while the password had in fact been changed. This walks the real screen and asserts
/// the button comes back, which is the only way that bug was ever going to be seen.
/// <para>Writes: it changes the password twice and puts the original back, so the seat is usable
/// afterwards. Runs only when <c>BEN_PASSWORD_PANEL</c> is set, because a half-finished run leaves
/// the account on the interim password.</para>
/// </remarks>
[TestFixture]
[Category("PasswordPanel")]
public class PasswordPanelTests : BenTestBase
{
    [Test]
    public async Task Changing_a_password_finishes_and_the_button_comes_back()
    {
        if (Environment.GetEnvironmentVariable("BEN_PASSWORD_PANEL") != "1")
            Assert.Ignore("Set BEN_PASSWORD_PANEL=1 — this test changes a real account's password.");

        var original = MemberPassword;
        var interim  = original + "!Tmp1";

        await LoginAsync(MemberEmail, original);
        await Page.GotoAsync($"{BaseUrl}/profile");
        // The panel lives behind the Security tab; the page renders long before the circuit is
        // live, so wait for the tab itself rather than for the network to go quiet.
        var securityTab = Page.GetByText("Security", new() { Exact = true }).First;
        await securityTab.WaitForAsync(new() { Timeout = 30_000 });
        await securityTab.ClickAsync();
        await Page.WaitForSelectorAsync("input[type=password]", new() { Timeout = 30_000 });

        await ChangeAsync(original, interim);
        await ChangeAsync(interim, original);   // put the seat back the way it was
    }

    private async Task ChangeAsync(string current, string next)
    {
        var boxes = Page.Locator("input[type=password]");
        Assert.That(await boxes.CountAsync(), Is.GreaterThanOrEqualTo(3),
                    "current, new and confirm");

        await boxes.Nth(0).FillAsync(current);
        await boxes.Nth(1).FillAsync(next);
        await boxes.Nth(2).FillAsync(next);

        var button = Page.GetByRole(AriaRole.Button,
            new() { NameRegex = new System.Text.RegularExpressions.Regex("Change password|Add password", System.Text.RegularExpressions.RegexOptions.IgnoreCase) });
        await button.ClickAsync();

        // The whole bug: this never stopped saying "Saving…".
        await Expect(Page.GetByText("Saving…")).Not.ToBeVisibleAsync(new() { Timeout = 20_000 });

        // Losing the busy state is not enough on its own — a refusal also clears it, and an
        // assertion that only checks "Saving… is gone" passes on a change that never happened.
        // The panel says so explicitly when it worked, so require that sentence.
        await Expect(Page.GetByText("Password changed.")).ToBeVisibleAsync(new() { Timeout = 10_000 });
        Assert.That(await Page.Locator(".alert-danger").CountAsync(), Is.Zero,
                    "a successful change shows no refusal");
    }
}
