using Microsoft.Playwright;
using NUnit.Framework;

namespace Ben.Web.Playwright.Tests;

/// <summary>
/// The header's two menus can be used on a phone (2026-09-23).
/// </summary>
/// <remarks>
/// <para><b>What was wrong.</b> On a phone the template gives every header item after the first
/// <c>scale: 1</c> for its tap animation, and any <c>scale</c> but <c>none</c> makes a stacking
/// context. The open menu's z-index was then scored inside its own wrapper, which sat beneath the
/// full-screen click-away layer — so every tap on the account menu or the bell landed on that layer
/// and closed the menu. "Sign Out" signed nobody out. At desktop width it all worked, which is the
/// only width anything had tried it at.</para>
///
/// <para><b>Real clicks, deliberately.</b> Playwright refuses a click whose target is covered by
/// something else, and reports what covers it — which is how the visual audit's first 375-wide pass
/// found this. A forced click would pass against the broken page.</para>
/// </remarks>
[TestFixture]
public class HeaderMenusWorkOnAPhoneTests : BenTestBase
{
    private async Task OnAPhoneAsync()
    {
        await LoginAsync(UserEmail, UserPassword);
        await Page.SetViewportSizeAsync(375, 812);
        await Page.GotoAsync($"{BaseUrl}/");
        await WaitUntilLoadedAsync();
    }

    [Test]
    [Description("On a phone, the bell's menu takes a tap: View all opens the notifications.")]
    public async Task The_bell_menu_takes_a_tap_on_a_phone()
    {
        await OnAPhoneAsync();

        await ClickUntilAsync(Page.Locator(".notification-bell > button").First,
                              Page.Locator(".notification-bell .dropdown-menu.show"));
        await Page.Locator(".notification-bell .dropdown-menu.show")
                  .GetByRole(AriaRole.Link, new() { Name = "View all" })
                  .ClickAsync(new() { Timeout = 10_000 });

        await Page.WaitForURLAsync(url => url.Contains("/notifications"), new() { Timeout = 15_000 });
    }

    [Test]
    [Description("On a phone, Sign Out in the account menu signs you out.")]
    public async Task Sign_out_signs_you_out_on_a_phone()
    {
        await OnAPhoneAsync();

        await ClickUntilAsync(Page.Locator(".user-menu > button").First,
                              Page.GetByRole(AriaRole.Button, new() { Name = "Sign Out" }));
        await Page.GetByRole(AriaRole.Button, new() { Name = "Sign Out" }).ClickAsync(new() { Timeout = 10_000 });

        // Signed out: the account menu is gone from the header.
        await Expect(Page.Locator(".user-menu")).ToHaveCountAsync(0, new() { Timeout = 15_000 });
    }
}
