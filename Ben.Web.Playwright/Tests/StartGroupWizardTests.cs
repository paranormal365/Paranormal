using Microsoft.Playwright;
using NUnit.Framework;

namespace Ben.Web.Playwright.Tests;

/// <summary>
/// Item 166 W1: the founder's wizard — a real group founded through all four steps, the hub
/// landing with its tour affordance, and the draft surviving a closed tab. The created group
/// is deleted through the API in finally (shared database), and the localStorage draft is
/// cleared the same way.
/// </summary>
[TestFixture]
[NonParallelizable]
[Category("StartGroupWizard")]
public class StartGroupWizardTests : BenTestBase
{
    [Test]
    public async Task The_wizard_founds_a_group_and_the_hub_offers_the_tour()
    {
        await LoginAsync(SuperAdminEmail, SuperAdminPassword);

        var suffix = Guid.NewGuid().ToString("N")[..8];
        string? orgId = null;

        try
        {
            await Page.GotoAsync($"{BaseUrl}/organizations/new");
            await WaitUntilLoadedAsync();

            // Step 1 — identity. The slug suggests itself from the name.
            var name = Main.Locator("#newgroup-name");
            await Expect(name).ToBeVisibleAsync(new() { Timeout = 45_000 });
            await name.FillAsync($"E2E Wizard Group {suffix}");
            await ClickUntilAsync(
                Main.GetByRole(AriaRole.Button, new() { Name = "Next", Exact = true }),
                Main.Locator("#newgroup-city"));

            // Step 2 — address, deliberately skipped (it is optional by design).
            await Main.GetByRole(AriaRole.Button, new() { Name = "Next", Exact = true }).ClickAsync();

            // Step 3 — defaults are fine.
            await Expect(Main.Locator("#newgroup-applications")).ToBeVisibleAsync(new() { Timeout = 45_000 });
            await Main.GetByRole(AriaRole.Button, new() { Name = "Next", Exact = true }).ClickAsync();

            // Step 4 — review shows the name, then create.
            await Expect(Main.Locator("#newgroup-review")).ToContainTextAsync($"E2E Wizard Group {suffix}");
            await Main.GetByRole(AriaRole.Button, new() { Name = "Create the group", Exact = true }).ClickAsync();

            await Page.WaitForURLAsync(url => url.Contains("/organizations/") && url.Contains("welcome=1"),
                new() { Timeout = 45_000 });
            orgId = new Uri(Page.Url).AbsolutePath.Split('/')[^1];

            // The hub is theirs, and the tour affordance is there. The tour is launched by
            // hand here rather than through the welcome auto-launch, because dismissal is a
            // per-user row and this shared seat may have dismissed it in an earlier run —
            // the manual path is the one that must ALWAYS work.
            await WaitUntilLoadedAsync();
            var launcher = Main.Locator("#org-tour-launch");
            await Expect(launcher).ToBeVisibleAsync(new() { Timeout = 45_000 });
            await ClickUntilAsync(launcher, Page.Locator(".ben-tour-card"));

            await Expect(Page.Locator(".ben-tour-card")).ToContainTextAsync("Invite your people");
            await Page.Locator(".ben-tour-card").GetByRole(AriaRole.Button, new() { Name = "Next" }).ClickAsync();
            await Expect(Page.Locator(".ben-tour-card")).ToContainTextAsync("Cases live here");
            await Page.Locator(".ben-tour-card").GetByText("Skip tour").ClickAsync();
            await Expect(Page.Locator(".ben-tour-card")).ToHaveCountAsync(0);
        }
        finally
        {
            if (orgId is not null)
            {
                var login = await Page.APIRequest.PostAsync("http://localhost:5252/login",
                    new() { DataObject = new { email = SuperAdminEmail, password = SuperAdminPassword } });
                if (login.Ok)
                {
                    var token = (await login.JsonAsync())!.Value.GetProperty("accessToken").GetString();
                    await Page.APIRequest.DeleteAsync($"http://localhost:5252/api/organizations/{orgId}",
                        new() { Headers = new Dictionary<string, string> { ["Authorization"] = $"Bearer {token}" } });
                }
            }
            await Page.EvaluateAsync("localStorage.removeItem('wizard:start-group')");
        }
    }

    [Test]
    public async Task A_closed_tab_resumes_the_wizard_where_it_left_off()
    {
        await LoginAsync(SuperAdminEmail, SuperAdminPassword);

        try
        {
            await Page.GotoAsync($"{BaseUrl}/organizations/new");
            await WaitUntilLoadedAsync();

            var name = Main.Locator("#newgroup-name");
            await Expect(name).ToBeVisibleAsync(new() { Timeout = 45_000 });
            await name.FillAsync("Draft Survivor Group");
            await ClickUntilAsync(
                Main.GetByRole(AriaRole.Button, new() { Name = "Next", Exact = true }),
                Main.Locator("#newgroup-city"));

            // The tab "closes" — a reload is the same death and the same resurrection.
            await Page.ReloadAsync();
            await WaitUntilLoadedAsync();

            // Resumed on step 2, with step 1's answers intact behind Back.
            await Expect(Main.Locator("#newgroup-city")).ToBeVisibleAsync(new() { Timeout = 45_000 });
            await Main.GetByRole(AriaRole.Button, new() { Name = "Back", Exact = true }).ClickAsync();
            await Expect(Main.Locator("#newgroup-name")).ToHaveValueAsync("Draft Survivor Group",
                new() { Timeout = 45_000 });
        }
        finally
        {
            await Page.EvaluateAsync("localStorage.removeItem('wizard:start-group')");
        }
    }
}
