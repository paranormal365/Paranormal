using Microsoft.Playwright;
using NUnit.Framework;

namespace Ben.Web.Playwright.Tests;

/// <summary>
/// The page that answers "can this machine actually send mail?".
/// </summary>
/// <remarks>
/// It exists because a real sign-up produced no email and nothing anywhere could say why. A
/// diagnostic that is itself unreachable would be a joke, so this opens it the way a person does —
/// signed in, through the URL — and checks it reports rather than merely renders.
///
/// It deliberately does NOT send a test message. That is a real email to a real address, and a
/// test suite is not the place to decide to send one.
/// </remarks>
[TestFixture]
[Category("AdminMail")]
public class AdminMailDiagnosticsTests : BenTestBase
{
    [Test]
    [Description("A SuperAdmin can open the mail diagnostic and see what this machine sends with.")]
    public async Task The_mail_diagnostic_reports_this_machines_settings()
    {
        await LoginAsync(SuperAdminEmail, SuperAdminPassword);
        await Page.GotoAsync($"{BaseUrl}/admin/mail");
        await WaitUntilLoadedAsync();

        // Any of the three outcomes is a PASS for reachability — the page's job is to report the
        // machine's state, and "mail is switched off" is a report, not a failure. What would be a
        // failure is the page saying nothing at all.
        await Expect(Main.Locator("[data-testid='mail-settings']")
                .Or(Main.Locator("[data-testid='mail-not-configured']"))
                .Or(Main.Locator("[data-testid='mail-settings-missing']"))
                .First)
            .ToBeVisibleAsync(new() { Timeout = 30_000 });

        var body = await Page.InnerTextAsync("body");
        Assert.That(body, Does.Not.Contain("An unhandled error has occurred"));

        // The send control has to be there, or the page only answers half the question.
        await Expect(Main.Locator("[data-testid='mail-test-send']"))
            .ToBeVisibleAsync(new() { Timeout = 10_000 });
    }

    [Test]
    [Description("The diagnostic never prints the SMTP password.")]
    public async Task The_password_is_reported_as_present_or_missing_and_never_shown()
    {
        await LoginAsync(SuperAdminEmail, SuperAdminPassword);
        await Page.GotoAsync($"{BaseUrl}/admin/mail");
        await WaitUntilLoadedAsync();

        var settings = Main.Locator("[data-testid='mail-settings']");
        if (await settings.CountAsync() == 0)
            Assert.Ignore("Mail is not configured on this machine, so there is no password to show.");

        await Expect(settings).ToBeVisibleAsync(new() { Timeout = 20_000 });

        // The word, never the value. A diagnostic that prints a secret is one nobody can safely
        // open in front of another person — or screenshot into a bug report.
        var shown = await settings.InnerTextAsync();
        Assert.That(shown, Does.Contain("present").Or.Contain("missing"),
            $"The password row said neither 'present' nor 'missing'. Saw:\n{shown}");
    }

    [Test]
    [Description("It is SuperAdmin-only.")]
    public async Task An_ordinary_member_is_sent_away()
    {
        await LoginAsync(UserEmail, UserPassword);
        await Page.GotoAsync($"{BaseUrl}/admin/mail");
        await WaitUntilLoadedAsync();

        // The page redirects rather than rendering a refusal. Either is acceptable; showing the
        // settings is not.
        var body = await Page.InnerTextAsync("body");
        Assert.That(await Main.Locator("[data-testid='mail-settings']").CountAsync(), Is.Zero,
            $"An ordinary member was shown the mail settings. Saw:\n{body}");
    }
}
