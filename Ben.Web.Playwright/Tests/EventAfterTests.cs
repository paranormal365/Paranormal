using Microsoft.Playwright;
using NUnit.Framework;

namespace Ben.Web.Playwright.Tests;

/// <summary>
/// After the event: the thank-you settings, what guests said, and where a guest leaves a review
/// (item 235 phase 12).
/// </summary>
/// <remarks>
/// Leaving a review needs an event that is over and a confirmed place at it, which the seeded events
/// are not; that rule is proved in <c>HostedEventAfterTests</c>. These walk the pages around it.
/// </remarks>
[TestFixture]
[Category("HostedEvents")]
public class EventAfterTests : BenTestBase
{
    private const string RoomsEventId = "40000002-0000-0000-0000-000000000002";

    private string _orgId = string.Empty;

    [SetUp]
    public async Task Find() => _orgId = await OrgIdBySlugAsync("paranormal365");

    [TestCase(1280, 800)]
    [TestCase(375, 812)]
    public async Task The_organizer_writes_the_thank_you_and_it_is_still_there_after_a_reload(int width, int height)
    {
        await LoginAsync(SuperAdminEmail, SuperAdminPassword);
        await Page.SetViewportSizeAsync(width, height);

        var url = $"{BaseUrl}/organizations/{_orgId}/events/{RoomsEventId}/after";
        await Page.GotoAsync(url);
        await WaitUntilLoadedAsync();
        await Expect(Page.Locator("#after-thank-you")).ToBeVisibleAsync(new() { Timeout = 30_000 });

        var words = $"Thank you for coming. {Guid.NewGuid():N}"[..40];
        await Page.Locator("#after-thank-you-note").FillAsync(words);
        await ClickUntilAsync(Page.Locator("#after-save"), Page.Locator("#after-note"));

        await Page.GotoAsync(url);
        await WaitUntilLoadedAsync();
        await Expect(Page.Locator("#after-thank-you-note")).ToHaveValueAsync(words, new() { Timeout = 30_000 });
        await Expect(Page.Locator("#after-reviews")).ToBeVisibleAsync();

        var wide = await Page.EvaluateAsync<bool>("document.documentElement.scrollWidth > window.innerWidth + 1");
        Assert.That(wide, Is.False, "the page scrolled sideways");
    }

    [Test]
    public async Task A_guests_review_page_says_reviews_open_once_the_event_is_over()
    {
        await LoginAsync(ClientEmail, ClientPassword);
        await Page.GotoAsync($"{BaseUrl}/my-events/{RoomsEventId}/review");
        await WaitUntilLoadedAsync();

        await Expect(Page.Locator("#review-not-yet")).ToBeVisibleAsync(new() { Timeout = 30_000 });
        Assert.That(await Page.Locator("#event-review-form").CountAsync(), Is.Zero);
    }
}
