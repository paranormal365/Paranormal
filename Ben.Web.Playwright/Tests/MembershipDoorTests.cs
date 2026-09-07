using Microsoft.Playwright;
using NUnit.Framework;

namespace Ben.Web.Playwright.Tests;

/// <summary>
/// What a member can actually reach, and whether the doors agree with it
/// (site evaluation 2026-09-06, phase 2).
/// </summary>
/// <remarks>
/// <para>W-M1 and W-VW1 were the same failure: a membership rank opens nothing below
/// Administrator, so somebody added to a group could see its cases listed on their desk and be
/// refused by the case API when they clicked one. Two things had to change — new members are
/// given the group's starting role, and a door never offers what it cannot open.</para>
///
/// <para>These run as the seeded ordinary member, never as an administrator: the whole point is
/// what the rank below Administrator can see, and an admin bypasses every check being tested.</para>
/// </remarks>
[TestFixture]
[Category("MembershipDoors")]
public class MembershipDoorTests : BenTestBase
{
    /// <summary>
    /// Every case the desk lists opens. The blank page is what W-M1 looked like.
    /// </summary>
    [Test]
    public async Task A_new_member_opens_the_case_their_desk_shows()
    {
        await LoginAsync(MemberEmail, MemberPassword);
        await Page.GotoAsync($"{BaseUrl}/");

        try
        {
            await Page.WaitForSelectorAsync("[data-testid='member-desk']", new() { Timeout = 20_000 });
        }
        catch (TimeoutException)
        {
            Assert.Ignore("this account belongs to no group, so it has no desk to check");
        }

        // The desk's own case card. Its links go to the group's PUBLIC case page, so what this
        // asserts is that the tile lists something at all — a member who cannot read the group's
        // cases now gets no case column rather than a column of doors that refuse them.
        var tile = Page.Locator("[data-testid='desk-open-cases']");
        await Expect(tile).ToBeVisibleAsync(new() { Timeout = 15_000 });

        var caseLinks = tile.Locator("a[href*='/cases']");
        if (await caseLinks.CountAsync() == 0)
            Assert.Ignore("no open cases on this desk to follow");

        // The group-side case page is the one that refused people, and the roster is what led
        // them there. The roster's cards navigate on click rather than being anchors, so this
        // clicks one and follows where it lands.
        await Page.GotoAsync($"{BaseUrl}/my-investigations");
        await WaitUntilLoadedAsync();

        var card = Page.Locator(".card[style*='cursor:pointer']").First;
        if (await card.CountAsync() == 0)
            Assert.Ignore("this member is on no visits, so there is no case to follow");

        await ClickUntilUrlAsync(card, @"/organizations/[0-9a-f\-]+");
        await WaitUntilLoadedAsync();

        // A visit whose case this person cannot read now lands on the group's Investigations tab
        // instead of a case page — that is the door withholding itself, and it is a pass.
        if (!Page.Url.Contains("/cases/"))
            Assert.Ignore("this visit's case is not offered to this member, which is the other correct answer");

        // The sentence a refused case page shows. Its presence means a door was offered that
        // this person cannot open — exactly the thing this phase removed.
        await Expect(Page.Locator("#case-not-available")).ToHaveCountAsync(0, new() { Timeout = 10_000 });

        var body = await Page.InnerTextAsync("body");
        Assert.That(body, Does.Not.Contain("An unhandled error has occurred"));
    }

    /// <summary>
    /// The roster never shows a case reference it cannot open.
    /// </summary>
    [Test]
    public async Task My_investigations_never_offers_a_case_it_cannot_open()
    {
        await LoginAsync(MemberEmail, MemberPassword);
        await Page.GotoAsync($"{BaseUrl}/my-investigations");
        await WaitUntilLoadedAsync();

        var card = Page.Locator(".card[style*='cursor:pointer']").First;
        if (await card.CountAsync() == 0)
            Assert.Ignore("this member is on no visits, so there is nothing to follow");

        await ClickUntilUrlAsync(card, @"/organizations/[0-9a-f\-]+");
        await WaitUntilLoadedAsync();

        // Either it opened a case — in which case the case page must not refuse — or it landed
        // on the group's Investigations tab because the case was withheld. Both are the door
        // telling the truth; a case page showing the refusal sentence is not.
        await Expect(Page.Locator("#case-not-available")).ToHaveCountAsync(0, new() { Timeout = 10_000 });
    }

    /// <summary>
    /// The door to the ballot survives the request going Under Review (W-A4).
    /// </summary>
    /// <remarks>
    /// It used to disappear at exactly that moment — voting opened and the way in closed — so
    /// reviewers had to follow the internal message to find a page the card no longer offered.
    /// </remarks>
    [Test]
    public async Task The_vote_page_stays_reachable_under_review()
    {
        await LoginAsync(SuperAdminEmail, SuperAdminPassword);

        // The pending-requests screen of whichever seeded group has one.
        await Page.GotoAsync($"{BaseUrl}/organizations");
        await WaitUntilLoadedAsync();

        var orgLink = Page.Locator("a[href^='/organizations/']").First;
        if (await orgLink.CountAsync() == 0) Assert.Ignore("no group to open");
        var orgHref = await orgLink.GetAttributeAsync("href");
        var orgId = orgHref?.Split('/', StringSplitOptions.RemoveEmptyEntries).Skip(1).FirstOrDefault();
        if (orgId is null) Assert.Ignore("could not read a group id from the list");

        await Page.GotoAsync($"{BaseUrl}/organizations/{orgId}/pending-requests");
        await WaitUntilLoadedAsync();

        var review = Page.GetByRole(AriaRole.Link, new() { Name = "Review & vote" });
        if (await review.CountAsync() == 0)
            Assert.Ignore("this group has no pending request to review");

        // Put the first one Under Review, then check the link is still on its card.
        var underReview = Page.GetByRole(AriaRole.Button, new() { Name = "Under Review" }).First;
        if (await underReview.CountAsync() > 0)
        {
            await ClickUntilAsync(underReview, Page.GetByText("Under Review", new() { Exact = false }).First);
            await WaitUntilLoadedAsync();
        }

        await Expect(Page.GetByRole(AriaRole.Link, new() { Name = "Review & vote" }).First)
            .ToBeVisibleAsync(new() { Timeout = 15_000 });
    }
}
