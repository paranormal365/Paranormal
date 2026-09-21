using Microsoft.Playwright;
using NUnit.Framework;

namespace Ben.Web.Playwright.Tests;

/// <summary>
/// The invitation card and the page it leads to, opened in a browser.
/// </summary>
/// <remarks>
/// <para>Both screens are new, and this repository's own rule is that a Razor page which compiles
/// is not a page that renders — <c>HelpLink Page=</c> compiled fine and threw at render, and only
/// opening it found that. Two new screens shipped without being opened would be the same bet
/// taken twice.</para>
///
/// <para>It also holds the shape of the feature: the card is the way a group adds its own people,
/// and the page it leads to must be readable by somebody with no account, because that is who is
/// holding the link.</para>
/// </remarks>
[TestFixture]
public class JoinLinkScreensTests : BenTestBase
{
    [Test]
    [Description("A group's owner can make an invitation link and read it off the screen.")]
    public async Task An_owner_can_make_an_invitation_link()
    {
        await LoginAsync(UserEmail, UserPassword);

        var orgId = await OrgIdBySlugAsync("paranormal365");
        await Page.GotoAsync($"{BaseUrl}/organizations/{orgId}/members");
        await WaitForTheCircuitAsync();
        await WaitUntilLoadedAsync();

        await Expect(Page.Locator("#invite-card")).ToBeVisibleAsync(new() { Timeout = 20_000 });

        // Either state is legitimate — the group may already hold a link from an earlier run — so
        // the test asks for the one that is offered rather than assuming a fresh group.
        var make = Page.Locator("#invite-make");
        if (await make.CountAsync() > 0)
            await ClickUntilAsync(make, Page.Locator("#invite-link"));

        var box = Page.Locator("#invite-link");
        await Expect(box).ToBeVisibleAsync(new() { Timeout = 20_000 });

        var link = await box.InputValueAsync();
        Assert.That(link, Does.Contain("/join/"),
            "the box a person is meant to paste into a message must hold the whole address");
        Assert.That(link, Does.StartWith("http"),
            "a link that arrives as \"/join/abc\" is not a link");
    }

    [Test]
    [Description("Somebody with no account can read the invitation and is offered a way in.")]
    public async Task A_stranger_holding_the_link_is_told_whose_group_it_is()
    {
        await LoginAsync(UserEmail, UserPassword);

        var orgId = await OrgIdBySlugAsync("paranormal365");
        await Page.GotoAsync($"{BaseUrl}/organizations/{orgId}/members");
        await WaitForTheCircuitAsync();
        await WaitUntilLoadedAsync();

        var make = Page.Locator("#invite-make");
        if (await make.CountAsync() > 0)
            await ClickUntilAsync(make, Page.Locator("#invite-link"));

        var link = await Page.Locator("#invite-link").InputValueAsync();
        Assert.That(link, Does.Contain("/join/"), "no invitation link to follow");

        // The person holding this has no account. That is the whole point of the screen.
        await LogoutAsync();
        await Page.GotoAsync(link);
        await WaitForTheCircuitAsync();
        await WaitUntilLoadedAsync();

        // Named, so the reader knows whose group they are being asked into before deciding.
        await Expect(Page.Locator("#join-asking")).ToBeVisibleAsync(new() { Timeout = 20_000 });
        await Expect(Page.GetByText("Paranormal365", new() { Exact = false }).First).ToBeVisibleAsync();

        // And offered a way in rather than a dead end — a link always shows.
        await Expect(Page.Locator("#join-signin")).ToBeVisibleAsync();

        var href = await Page.Locator("#join-signin").GetAttributeAsync("href");
        Assert.That(href, Does.Contain("returnUrl"),
            "signing in must come back to the invitation, or the link is lost on the way");
    }

    [Test]
    [Description("A link that is not a real invitation says so instead of failing.")]
    public async Task A_made_up_link_says_it_is_not_working()
    {
        await LogoutAsync();
        await Page.GotoAsync($"{BaseUrl}/join/this-is-not-a-real-invitation");
        await WaitForTheCircuitAsync();
        await WaitUntilLoadedAsync();

        await Expect(Page.Locator("#join-gone")).ToBeVisibleAsync(new() { Timeout = 20_000 });
    }
}
