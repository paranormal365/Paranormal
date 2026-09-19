using Microsoft.Playwright;
using NUnit.Framework;

namespace Ben.Web.Playwright.Tests;

/// <summary>
/// The site-wide announcement (Administration → Site Settings) must show up as a banner on every
/// page — it was the seventh write-only feature (2026-08-22): saved, stored, and read by nothing.
/// The test restores whatever announcement was set before it ran, because this database is shared
/// and a leftover test banner would be shown to real visitors.
/// </summary>
[TestFixture]
[Category("SiteAnnouncement")]
public class SiteAnnouncementTests : BenTestBase
{
    private const string TestNotice = "E2E notice: the site is fine, this banner is a test.";

    private ILocator AnnouncementCard =>
        Page.Locator(".card", new() { HasText = "Site-wide announcement" }).First;

    private ILocator Banner => Page.Locator("#site-announcement");

    private async Task<string> ReadCurrentAsync()
    {
        await Page.GotoAsync($"{BaseUrl}/admin/site-settings");
        await Expect(AnnouncementCard).ToBeVisibleAsync(new() { Timeout = 20_000 });
        return await AnnouncementCard.Locator("textarea").InputValueAsync();
    }

    private async Task SaveAnnouncementAsync(string value)
    {
        await Page.GotoAsync($"{BaseUrl}/admin/site-settings");
        await Expect(AnnouncementCard).ToBeVisibleAsync(new() { Timeout = 20_000 });

        var box = AnnouncementCard.Locator("textarea");
        await box.ClickAsync();
        await box.FillAsync(value);

        // Saved state is announced by the card's badge; waiting on it beats trusting the click.
        await ClickUntilAsync(
            AnnouncementCard.GetByRole(AriaRole.Button, new() { Name = "Save", Exact = true }),
            AnnouncementCard.Locator(".badge", new() { HasText = value.Length == 0 ? "Not set" : "Set" }));
    }

    [Test]
    public async Task The_announcement_banner_appears_everywhere_and_leaves_when_cleared()
    {
        await LoginAsync(SuperAdminEmail, SuperAdminPassword);
        var original = await ReadCurrentAsync();

        try
        {
            await SaveAnnouncementAsync(TestNotice);

            // The provider refreshes on a 30s snapshot, but saving calls Invalidate, so this
            // circuit's next page sees it immediately.
            await Page.GotoAsync($"{BaseUrl}/");
            await Expect(Banner).ToBeVisibleAsync(new() { Timeout = 20_000 });
            await Expect(Banner).ToContainTextAsync(TestNotice);

            // Site-wide means any page, not the front door.
            await Page.GotoAsync($"{BaseUrl}/my-investigations");
            await Expect(Banner).ToBeVisibleAsync(new() { Timeout = 20_000 });

            await SaveAnnouncementAsync(string.Empty);
            await Page.GotoAsync($"{BaseUrl}/");
            await Expect(Banner).ToHaveCountAsync(0, new() { Timeout = 20_000 });
        }
        finally
        {
            // Put back whatever was there before — this database is shared with the public site.
            await SaveAnnouncementAsync(original);
        }
    }
}
