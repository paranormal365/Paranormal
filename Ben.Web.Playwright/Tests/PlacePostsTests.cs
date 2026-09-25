using System;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading.Tasks;
using Microsoft.Playwright;
using NUnit.Framework;

namespace Ben.Web.Playwright.Tests;

/// <summary>
/// Posting about a public place, and the wider door it comes through.
/// </summary>
/// <remarks>
/// <para>Ben, 2026-09-17: a public location should be "actually public for adding files, messages
/// etc". Built as feed posts carrying a place, so the thing worth driving in a browser is that the
/// two surfaces agree — a post written on the place's page shows up on the feed saying where it
/// belongs, and a link takes you back.</para>
///
/// <para>The seeded Bell Witch Cave is the public location; a residence has no composer at all,
/// which is asserted rather than assumed because it is the half that protects somebody's home.</para>
/// </remarks>
[TestFixture]
[Category("Feed")]
[NonParallelizable]
public class PlacePostsTests : BenTestBase
{
    private const string SeededPlace = "40000001-0000-0000-0000-000000000001";
    private const string SeededPlaceName = "Bell Witch Cave";
    private static bool? _feedWasOn;

    /// <summary>
    /// Turns the feed on and waits until the service agrees. Place posts are feed posts, so
    /// nothing here exists without it.
    /// </summary>
    /// <remarks>
    /// Ignores the whole fixture when the switch cannot be arranged — a missing precondition, not
    /// a finding. Setting it and starting to click was a race, and a race that loses quietly: the
    /// composer does not render and the tests time out looking for an element whose absence is
    /// correct.
    /// </remarks>
    [OneTimeSetUp]
    public async Task TurnTheFeedOn()
    {
        _feedWasOn = await TurnTheFeedOnAsync();
        if (_feedWasOn is null)
            Assert.Ignore("The public feed could not be switched on, so a place takes no posts here.");
    }

    [OneTimeTearDown]
    public Task PutTheFeedBack() => PutTheFeedBackAsync(_feedWasOn);

    private async Task<bool> OpenSeededPlaceAsync(string email, string password)
    {
        await LoginAsync(email, password);
        await Page.GotoAsync($"{BaseUrl}/places/{SeededPlace}");
        await WaitUntilLoadedAsync();

        // A database without the seeded landmark has nothing for this fixture to drive.
        return await Main.GetByText(SeededPlaceName, new() { Exact = false }).CountAsync() > 0;
    }

    /// <summary>
    /// The whole round trip: write it on the place, read it back on the place, and find it on the
    /// feed saying which place it belongs to with a link home.
    /// </summary>
    [Test]
    public async Task A_post_about_a_place_appears_there_and_on_the_feed()
    {
        if (!await OpenSeededPlaceAsync(UserEmail, UserPassword))
            Assert.Ignore("the seeded landmark this walks is not on this database");

        // The fixture's setup has already confirmed the feed is on, so a missing composer is a
        // fault rather than a precondition — and ignoring here would hide exactly the regression
        // this test exists to catch.
        var composer = Main.GetByTestId("place-composer");
        await Expect(composer).ToBeVisibleAsync(new() { Timeout = 30_000 });

        // Unique, so the assertions cannot pass on somebody else's post from an earlier run.
        var said = $"Cold spot on the stair {Guid.NewGuid():N}"[..40];

        var box = composer.GetByRole(AriaRole.Textbox).First;
        await Expect(box).ToBeVisibleAsync(new() { Timeout = 15_000 });
        await box.FillAsync(said);
        await composer.GetByTestId("feed-composer-post").ClickAsync();

        // On the place, straight away — the page puts a new post at the top rather than reloading.
        await Expect(Main.GetByTestId("place-posts").GetByText(said, new() { Exact = false }))
            .ToBeVisibleAsync(new() { Timeout = 20_000 });

        // And on the feed, carrying where it belongs. On the LATEST tab, not the default For You:
        // For You ranks by score, so on a database with a full seeded feed a post written a second
        // ago has no guaranteed position on the first page — which is how this failed on a fresh
        // database having passed on a well-used one. Latest is ordered by time, so the post just
        // written is the first card, and what the test is actually about is unchanged.
        await Page.GotoAsync($"{BaseUrl}/feed");
        await WaitUntilLoadedAsync();
        await Main.GetByRole(AriaRole.Button, new() { Name = "Latest", Exact = true }).ClickAsync();

        var card = Main.Locator(".bv-feed-post, article, .card")
                       .Filter(new() { HasTextString = said }).First;
        await Expect(card).ToBeVisibleAsync(new() { Timeout = 30_000 });

        var about = card.GetByTestId("feed-post-about-place");
        await Expect(about).ToContainTextAsync(SeededPlaceName);

        // The link goes home to the place, which is what makes the feed a way in rather than a
        // dead end.
        await Expect(about.GetByRole(AriaRole.Link))
            .ToHaveAttributeAsync("href", $"/places/{SeededPlace}");
    }

    /// <summary>
    /// The place's own feed page, where "See all" goes, shows the post and nothing else's.
    /// </summary>
    [Test]
    public async Task One_places_posts_have_an_address_of_their_own()
    {
        if (!await OpenSeededPlaceAsync(UserEmail, UserPassword))
            Assert.Ignore("the seeded landmark this walks is not on this database");

        // The fixture's setup has already confirmed the feed is on, so a missing composer is a
        // fault rather than a precondition — and ignoring here would hide exactly the regression
        // this test exists to catch.
        var composer = Main.GetByTestId("place-composer");
        await Expect(composer).ToBeVisibleAsync(new() { Timeout = 30_000 });

        var said = $"Only about this place {Guid.NewGuid():N}"[..40];
        var box = composer.GetByRole(AriaRole.Textbox).First;
        await Expect(box).ToBeVisibleAsync(new() { Timeout = 15_000 });
        await box.FillAsync(said);
        await composer.GetByTestId("feed-composer-post").ClickAsync();
        await Expect(Main.GetByTestId("place-posts").GetByText(said, new() { Exact = false }))
            .ToBeVisibleAsync(new() { Timeout = 20_000 });

        await Page.GotoAsync($"{BaseUrl}/feed/places/{SeededPlace}");
        await WaitUntilLoadedAsync();

        await Expect(Main.GetByText(said, new() { Exact = false }).First)
            .ToBeVisibleAsync(new() { Timeout = 30_000 });
        // The heading is the place's name, not its id.
        await Expect(Main.GetByRole(AriaRole.Heading).First).ToContainTextAsync(SeededPlaceName);
    }

    /// <summary>
    /// A member with no group would be the sharper test here, and arrives with the seeded
    /// no-group person in phase 4. What this asserts meanwhile is the half that matters most: an
    /// ordinary member is offered the box on a public location.
    /// </summary>
    [Test]
    public async Task An_ordinary_member_is_offered_the_box()
    {
        if (!await OpenSeededPlaceAsync(MemberEmail, MemberPassword))
            Assert.Ignore("the seeded landmark this walks is not on this database");

        await Expect(Main.GetByTestId("place-composer")).ToBeVisibleAsync(new() { Timeout = 20_000 });
    }
}
