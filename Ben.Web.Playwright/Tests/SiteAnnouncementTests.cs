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

        // Wait for "Saved.", which shows once the save and the site's re-read of its settings have both
        // finished. The card's Set / Not set badge is no proof: it follows the text as it is typed, so it
        // was already right before Save was pressed, and the test moved on mid-save (backlog 255).
        await ClickUntilAsync(
            AnnouncementCard.GetByRole(AriaRole.Button, new() { Name = "Save", Exact = true }),
            Page.Locator(".alert-success", new() { HasText = "Saved." }));
    }

    [Test]
    public async Task The_announcement_banner_appears_everywhere_and_leaves_when_cleared()
    {
        await LoginAsync(SuperAdminEmail, SuperAdminPassword);
        var original = await ReadCurrentAsync();

        try
        {
            await SaveAnnouncementAsync(TestNotice);

            // The provider refreshes on a 30s snapshot, but saving re-reads it before "Saved." shows,
            // so the next page sees it immediately.
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
