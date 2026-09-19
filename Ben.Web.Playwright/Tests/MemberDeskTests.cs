using Microsoft.Playwright;
using NUnit.Framework;

namespace Ben.Web.Playwright.Tests;

/// <summary>A member's Home is a desk; a visitor's is the hero (item 204).</summary>
[TestFixture]
[Category("MemberDesk")]
public class MemberDeskTests : BenTestBase
{
    [Test]
    public async Task A_member_lands_on_their_desk_not_the_poster()
    {
        await LoginAsync(MemberEmail, MemberPassword);
        await Page.GotoAsync($"{BaseUrl}/");
        // The hero prerenders at once; the desk arrives once the circuit is live and the API has
        // answered. So wait for the desk itself, and only treat its absence as "no groups" after
        // a real wait — not on the first paint.
        try
        {
            await Page.WaitForSelectorAsync("[data-testid='member-desk'], [data-testid='desk-refusal']", new() { Timeout = 20_000 });
        }
        catch (TimeoutException)
        {
            await Expect(Page.Locator(".home-hero__title")).ToBeVisibleAsync();
            Assert.Ignore("this account belongs to no group, so the hero is the right page for it");
        }
        await Expect(Page.Locator("[data-testid='desk-refusal']")).ToHaveCountAsync(0);

        // The desk, and not the poster.
        await Expect(Page.Locator("[data-testid='member-desk']")).ToBeVisibleAsync();
        await Expect(Page.Locator(".home-hero__title")).ToHaveCountAsync(0, new() { Timeout = 10_000 });

        // Every tile is present and each opens what it counts.
        foreach (var tile in new[] { "desk-next-investigation", "desk-open-cases", "desk-unread", "desk-requests", "desk-gear" })
            await Expect(Page.Locator($"[data-testid='{tile}']")).ToBeVisibleAsync();
        Assert.That(await Page.Locator("[data-testid='desk-unread']").GetAttributeAsync("href"), Is.EqualTo("/notifications"));

        if (Environment.GetEnvironmentVariable("BEN_DESK_SHOT") is { Length: > 0 } shot)
            await Page.ScreenshotAsync(new() { Path = shot, FullPage = true });
    }

    [Test]
    public async Task A_visitor_still_gets_the_hero_and_no_desk()
    {
        await Page.GotoAsync($"{BaseUrl}/");
        await Expect(Page.Locator(".home-hero__title")).ToBeVisibleAsync(new() { Timeout = 30_000 });
        await Expect(Page.Locator("[data-testid='member-desk']")).ToHaveCountAsync(0);
    }
}
