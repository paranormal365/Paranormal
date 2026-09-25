using System.Text.RegularExpressions;
using Microsoft.Playwright;
using NUnit.Framework;

namespace Ben.Web.Playwright.Tests;

/// <summary>
/// Buyer reviews (storefront S6.3): reading them, writing one, seeing it wait for a moderator,
/// "Helpful", and who may not review.
/// </summary>
/// <remarks>
/// Reads StoreDemoSeeder's K-II reviews (S6.4): James's five stars and Emma's three with the
/// store's reply, both published; Sarah's still waiting. Sarah bought the field bag, which is the
/// product she reviews here — each test puts back what it changed, so a rerun starts the same.
/// </remarks>
[TestFixture]
[Category("Store")]
public class StoreReviewTests : BenTestBase
{
    private ILocator Reviews => Page.Locator("[data-testid=review]");

    private async Task OpenAsync(string path)
    {
        await Page.GotoAsync($"{BaseUrl}{path}");
        await WaitForTheCircuitAsync();
    }

    [Test]
    [Description("A product's published reviews show with the average, the store's reply and a sign-in offer for a guest.")]
    public async Task Reviews_are_listed_with_the_average_and_the_reply()
    {
        await OpenAsync("/store/p/k-ii-emf-meter");

        await Expect(Reviews.First).ToBeVisibleAsync(new() { Timeout = 30_000 });
        await Expect(Page.Locator("#reviews .ben-stars").First).ToHaveAttributeAsync("aria-label", new Regex(@"^4 out of 5 stars from 2 reviews$"));
        await Expect(Page.Locator("[data-testid=review-reply]")).ToContainTextAsync("airplane mode");
        await Expect(Reviews.Filter(new() { HasText = "Our go-to first sweep" })).ToHaveCountAsync(0);   // Sarah's is still waiting
        await Expect(Page.Locator("[data-testid=review-sign-in] a")).ToHaveAttributeAsync("href", new Regex(@"/login\?returnUrl="));

        await Page.Locator("#reviews-sort").SelectOptionAsync("lowest");
        await Expect(Reviews.First).ToContainTextAsync("too twitchy near phones", new() { Timeout = 15_000 });
    }

    [Test]
    [Description("A buyer's own review shows to them as 'Waiting for approval' while nobody else can see it.")]
    public async Task A_buyer_sees_their_waiting_review()
    {
        await LoginAsync(UserEmail, UserPassword);
        await OpenAsync("/store/p/k-ii-emf-meter");

        var mine = Page.Locator("[data-testid=my-review]");
        await Expect(mine).ToBeVisibleAsync(new() { Timeout = 30_000 });
        await Expect(mine.Locator("[data-testid=my-review-pending]")).ToHaveTextAsync("Waiting for approval");
        await Expect(Page.Locator("[data-testid=write-review]")).ToHaveCountAsync(0);
    }

    [Test]
    [Description("A buyer writes a review: an empty one is refused in words, a whole one waits for approval, and it can be deleted.")]
    public async Task A_buyer_writes_a_review_and_it_waits()
    {
        await LoginAsync(UserEmail, UserPassword);
        await OpenAsync("/store/p/investigators-field-bag");
        await Expect(Page.Locator("[data-testid=write-review], [data-testid=my-review]").First).ToBeVisibleAsync(new() { Timeout = 30_000 });
        await DeleteMineIfAnyAsync();   // a run that stopped half way left one

        await Page.Locator("[data-testid=write-review]").ClickAsync();
        await Page.Locator("#store-submit-review").ClickAsync();
        await Expect(Page.Locator("#review-refusal")).ToHaveTextAsync("Pick between one and five stars.");

        await Page.Locator("[data-testid=review-star-4]").ClickAsync();
        await Page.Locator("#review-title").FillAsync("Holds everything");
        await Page.Locator("#review-body").FillAsync("Two meters, a recorder and spare batteries, with room left over.");
        await Page.Locator("#store-submit-review").ClickAsync();

        var mine = Page.Locator("[data-testid=my-review]");
        await Expect(mine.Locator("[data-testid=my-review-pending]")).ToBeVisibleAsync(new() { Timeout = 15_000 });
        await Expect(mine).ToContainTextAsync("Holds everything");
        await Expect(Reviews.Filter(new() { HasText = "Holds everything" })).ToHaveCountAsync(0);   // not published

        await DeleteMineIfAnyAsync();
        await Expect(Page.Locator("[data-testid=write-review]")).ToBeVisibleAsync();
    }

    [Test]
    [Description("A member marks somebody's review helpful and takes it back; their own review has no Helpful button.")]
    public async Task A_member_marks_a_review_helpful()
    {
        await LoginAsync(MemberEmail, MemberPassword);   // James: wrote the five-star one, voted for Emma's
        await OpenAsync("/store/p/k-ii-emf-meter");

        var his = Reviews.Filter(new() { HasText = "Does one thing and does it fast" });
        await Expect(his).ToContainTextAsync("Your review", new() { Timeout = 30_000 });
        await Expect(his.Locator("[data-testid=review-helpful]")).ToHaveCountAsync(0);

        var emmas = Reviews.Filter(new() { HasText = "too twitchy near phones" });
        var helpful = emmas.Locator("[data-testid=review-helpful]");
        await Expect(helpful).ToHaveAttributeAsync("aria-pressed", "true");
        await helpful.ClickAsync();
        await Expect(helpful).ToHaveAttributeAsync("aria-pressed", "false", new() { Timeout = 15_000 });
        await Expect(emmas.Locator("[data-testid=review-helpful-count]")).ToHaveCountAsync(0);

        await helpful.ClickAsync();   // put it back
        await Expect(helpful).ToHaveAttributeAsync("aria-pressed", "true", new() { Timeout = 15_000 });
        await Expect(emmas.Locator("[data-testid=review-helpful-count]")).ToHaveTextAsync("1 person found this helpful");
    }

    [Test]
    [Description("Somebody who never bought a product is told why they can't review it.")]
    public async Task Somebody_who_never_bought_it_cannot_review()
    {
        await LoginAsync(MemberEmail, MemberPassword);
        await OpenAsync("/store/p/rem-pod");

        await Expect(Page.Locator("[data-testid=review-cannot]")).ToHaveTextAsync("Only somebody who has bought this can review it.", new() { Timeout = 30_000 });
        await Expect(Page.Locator("[data-testid=write-review]")).ToHaveCountAsync(0);
    }

    private async Task DeleteMineIfAnyAsync()
    {
        if (await Page.Locator("[data-testid=my-review]").CountAsync() == 0) return;
        await Page.Locator("[data-testid=my-review-delete]").ClickAsync();
        await Page.GetByRole(AriaRole.Button, new() { Name = "Delete review" }).ClickAsync();
        await Expect(Page.Locator("[data-testid=my-review]")).ToHaveCountAsync(0, new() { Timeout = 15_000 });
    }
}
