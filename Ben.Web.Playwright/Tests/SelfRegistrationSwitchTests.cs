using Microsoft.Playwright;
using NUnit.Framework;

namespace Ben.Web.Playwright.Tests;

/// <summary>
/// "Allow groups to self-register" must actually close the door. It was declared, shown as a
/// switch, described as "when off, only a SuperAdmin can create one" — and read by nothing, so an
/// administrator could switch it off and every signed-in visitor kept founding groups (item 152).
/// </summary>
/// <remarks>
/// The switch is restored in a finally: this database is shared with the public site, and leaving
/// self-registration off would quietly close the front door for real visitors.
/// </remarks>
[TestFixture]
[Category("SelfRegistrationSwitch")]
public class SelfRegistrationSwitchTests : BenTestBase
{
    private const string SettingLabel = "Allow groups to self-register";

    private ILocator SettingCard =>
        Page.Locator(".card", new() { HasText = SettingLabel }).First;

    private async Task SetSwitchAsync(bool on)
    {
        await Page.GotoAsync($"{BaseUrl}/admin/site-settings");
        await Expect(SettingCard).ToBeVisibleAsync(new() { Timeout = 20_000 });

        var toggle = SettingCard.Locator("input[type=checkbox]");
        if (await toggle.IsCheckedAsync() == on) return;

        // Saving happens on the toggle; the label is what says it landed.
        await ClickUntilAsync(toggle, SettingCard.GetByText(on ? "On" : "Off", new() { Exact = true }));
    }

    [Test]
    public async Task Switching_self_registration_off_removes_the_door_for_ordinary_members()
    {
        await LoginAsync(SuperAdminEmail, SuperAdminPassword);

        try
        {
            await SetSwitchAsync(false);

            // The SuperAdmin is exempt, so their own button must stay.
            await Page.GotoAsync($"{BaseUrl}/organizations");
            await WaitUntilLoadedAsync();
            await Expect(Page.GetByRole(AriaRole.Button, new() { Name = "Start a Group" }))
                .ToBeVisibleAsync(new() { Timeout = 20_000 });

            await LogoutAsync();
            await LoginAsync(MemberEmail, MemberPassword);

            // The button is gone …
            await Page.GotoAsync($"{BaseUrl}/organizations");
            await WaitUntilLoadedAsync();
            await Expect(Page.GetByRole(AriaRole.Button, new() { Name = "Start a Group" }))
                .ToHaveCountAsync(0, new() { Timeout = 20_000 });

            // … and so is the address behind it, with a reason rather than a dead form.
            await Page.GotoAsync($"{BaseUrl}/organizations/new");
            await Expect(Page.Locator("#newgroup-closed")).ToBeVisibleAsync(new() { Timeout = 20_000 });
            await Expect(Page.Locator("#newgroup-create")).ToHaveCountAsync(0);

            // Back on, the member can found a group again.
            await LogoutAsync();
            await LoginAsync(SuperAdminEmail, SuperAdminPassword);
            await SetSwitchAsync(true);

            await LogoutAsync();
            await LoginAsync(MemberEmail, MemberPassword);
            await Page.GotoAsync($"{BaseUrl}/organizations/new");
            await Expect(Page.Locator("#newgroup-create")).ToBeVisibleAsync(new() { Timeout = 20_000 });
        }
        finally
        {
            await LogoutAsync();
            await LoginAsync(SuperAdminEmail, SuperAdminPassword);
            await SetSwitchAsync(true);
        }
    }
}
