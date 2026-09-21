using Microsoft.Playwright;
using NUnit.Framework;

namespace Ben.Web.Playwright.Tests;

/// <summary>
/// The way back from a public event page.
/// </summary>
/// <remarks>
/// <para>Ben, 2026-09-21, from the home page: he searched "What's Near You" for Franklin TN,
/// pressed <i>See this event</i>, and the only arrow on the page he landed on pointed at
/// <b>Apple-Beta</b> — a group he had not asked about. There was no way back to the search he had
/// just run.</para>
///
/// <para>The page offered one link and it went onward rather than back. It now carries both: the
/// way back to wherever the visitor came from, when the link that sent them said so, and the
/// group beside it.</para>
/// </remarks>
[TestFixture]
public class EventBackLinkTests : BenTestBase
{
    /// <summary>A published event's public address, from the site's own public list.</summary>
    private async Task<string?> AnEventAddressAsync()
    {
        await Page.GotoAsync($"{BaseUrl}/events");
        await WaitForTheCircuitAsync();
        await WaitUntilLoadedAsync();

        var link = Main.Locator(".ev-row__title a").First;
        if (await link.CountAsync() == 0) return null;
        return await link.GetAttributeAsync("href");
    }

    [Test]
    [Description("An event opened from What's On offers the way back to What's On.")]
    public async Task The_way_back_is_to_where_the_visitor_came_from()
    {
        await LogoutAsync();

        var href = await AnEventAddressAsync();
        if (href is null) Assert.Ignore("no published event on the public list to open");

        // Followed as a person would: the list's own link, whatever it carries.
        await Main.Locator(".ev-row__title a").First.ClickAsync();
        await WaitForTheCircuitAsync();
        await WaitUntilLoadedAsync();

        await Expect(Main.GetByRole(AriaRole.Link, new() { Name = "Back to what's on", Exact = false }).First)
            .ToBeVisibleAsync(new() { Timeout = 20_000 });
    }

    /// <summary>
    /// Arriving with no origin — a shared link, a search result — still names the group.
    /// </summary>
    /// <remarks>
    /// Because the group link is worth having and was never the problem; what was wrong is that it
    /// was the ONLY link and was dressed as the way back.
    /// </remarks>
    [Test]
    [Description("An event opened cold still offers the group.")]
    public async Task An_event_opened_cold_still_names_its_group()
    {
        await LogoutAsync();

        var href = await AnEventAddressAsync();
        if (href is null) Assert.Ignore("no published event on the public list to open");

        // Stripped of any origin, the way a link pasted into a message arrives.
        var bare = href.Split('?')[0];
        await Page.GotoAsync($"{BaseUrl}{bare}");
        await WaitForTheCircuitAsync();
        await WaitUntilLoadedAsync();

        await Expect(Main.GetByRole(AriaRole.Link).Filter(new() { HasTextString = "Paranormal365" }).First)
            .ToBeVisibleAsync(new() { Timeout = 20_000 });

        // And no back-link pretending somebody came from somewhere they did not.
        await Expect(Main.GetByRole(AriaRole.Link, new() { Name = "Back to", Exact = false }))
            .ToHaveCountAsync(0);
    }
}
