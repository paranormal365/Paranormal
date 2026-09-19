using Microsoft.Playwright;
using NUnit.Framework;

namespace Ben.Web.Playwright.Tests;

/// <summary>
/// Item 166 W2: a cold signup meets onboarding exactly once — the gate sends them there,
/// finishing routes them where their answer points and stamps them, and no later visit ever
/// shows it again. An existing seed account never sees it at all (the migration stamped
/// everyone who predates the column).
/// </summary>
[TestFixture]
[NonParallelizable]
[Category("OnboardingJourney")]
public class OnboardingJourneyTests : BenTestBase
{
    private static string? ApiLogPath => Environment.GetEnvironmentVariable("BEN_API_LOGE") ?? Environment.GetEnvironmentVariable("BEN_API_LOG");

    [Test]
    public async Task A_cold_signup_meets_onboarding_once_and_an_old_account_never_does()
    {
        if (ApiLogPath is null || !File.Exists(ApiLogPath))
            Assert.Ignore("BEN_API_LOG not set — signup needs the API log for the confirmation link.");

        var tag = Guid.NewGuid().ToString("N")[..8];
        var email = $"onboard{tag}@example.com";
        var password = NewTestPassword();

        // ── sign up + confirm, the way the journey fixture does ───────────────
        await Page.GotoAsync($"{BaseUrl}/signup");
        await Expect(Page.Locator("#signup-handle")).ToBeVisibleAsync(new() { Timeout = 15_000 });
        await TypeHandleAsync($"onboard{tag}");
        await Expect(Page.GetByText("is free.")).ToBeVisibleAsync(new() { Timeout = 15_000 });
        await FillAndConfirmAsync("#signup-first", "Onboard");
        await FillAndConfirmAsync("#signup-last", $"User{tag}");
        await FillAndConfirmAsync("#signup-name", $"Onboard {tag}");
        await FillAndConfirmAsync("#signup-email", email);
        await FillAndConfirmAsync("#signup-password", password);
        await Page.GetByRole(AriaRole.Button, new() { Name = "Create account" }).ClickAsync();
        await Expect(Page.GetByText("Check your email").First).ToBeVisibleAsync(new() { Timeout = 20_000 });

        string? link = null;
        for (var attempt = 0; attempt < 20 && link is null; attempt++)
        {
            var text = await File.ReadAllTextAsync(ApiLogPath!);
            link = text.Split('\n')
                .Where(l => l.Contains("/confirm-email?userId="))
                .Select(l => l[l.IndexOf("/confirm-email?userId=", StringComparison.Ordinal)..].Trim())
                .LastOrDefault();
            if (link is null) await Task.Delay(500);
        }
        Assert.That(link, Is.Not.Null, "No confirmation link reached the API log.");
        await Page.GotoAsync($"{BaseUrl}{link}");
        await Page.GetByRole(AriaRole.Button, new() { Name = "Confirm my email" }).ClickAsync(new() { Timeout = 15_000 });
        await Expect(Page.GetByText("confirmed", new() { Exact = false }).First).ToBeVisibleAsync(new() { Timeout = 15_000 });

        // ── first sign-in: the gate delivers them to onboarding ───────────────
        await LoginAsync(email, password);
        await Page.WaitForURLAsync(url => url.Contains("/onboarding"), new() { Timeout = 45_000 });
        await Expect(Main.Locator("#onboard-name")).ToBeVisibleAsync(new() { Timeout = 45_000 });

        // Step 1 is pre-filled from the signup's display name; forward.
        await ClickUntilAsync(
            Main.GetByRole(AriaRole.Button, new() { Name = "Next", Exact = true }),
            Main.Locator("#onboard-intents"));

        // "I want to investigate with a group" routes to the finder.
        await Main.Locator("#onboard-intents").GetByText("I want to investigate with a group").ClickAsync();
        await Main.GetByRole(AriaRole.Button, new() { Name = "Next", Exact = true }).ClickAsync();

        await Expect(Main.Locator("#onboard-tour-launch")).ToBeVisibleAsync(new() { Timeout = 45_000 });
        await Main.GetByRole(AriaRole.Button, new() { Name = "Let's go", Exact = true }).ClickAsync();
        await Page.WaitForURLAsync(url => url.Contains("/find"), new() { Timeout = 45_000 });

        // ── never again: straight to the home page, no detour ─────────────────
        await Page.GotoAsync(BaseUrl);
        await WaitUntilLoadedAsync();
        await Task.Delay(1500);   // give a wrong gate its chance to misfire
        Assert.That(Page.Url, Does.Not.Contain("/onboarding"),
            "The gate re-offered onboarding to someone already stamped.");

        // ── an account that predates the column is already onboard ────────────
        await LoginAsync(MemberEmail, MemberPassword);
        await Page.GotoAsync(BaseUrl);
        await WaitUntilLoadedAsync();
        await Task.Delay(1500);
        Assert.That(Page.Url, Does.Not.Contain("/onboarding"),
            "The migration's backfill failed — an existing member was sent to onboarding.");
    }
}
