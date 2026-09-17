using System;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading.Tasks;
using Microsoft.Playwright;
using NUnit.Framework;

namespace Ben.Web.Playwright.Tests;

/// <summary>
/// Somebody in no group at all, doing the thing the free lane exists for.
/// </summary>
/// <remarks>
/// <para>Ben, 2026-09-17: "everything a solo person submits is going to be public by default…
/// if paid, they can make their work private." Wren belongs to nothing — no group, no case, not
/// anybody's client — which no other seat in the suite manages: Daniel is group-less but is a
/// client, and a client passes the feed's belonging rule. Until this fixture existed there was no
/// way to drive the door a person like her comes through, and the endpoint that mints her a space
/// had no caller on the site at all.</para>
///
/// <para><b>The fixture writes.</b> It mints Wren a personal organization, which is idempotent at
/// the server, so a second run reuses the one she already has rather than failing — and the tests
/// are written to pass either way, because a suite that only works on a fresh database is a suite
/// nobody runs twice.</para>
/// </remarks>
[TestFixture]
[Category("Feed")]
[NonParallelizable]
// ORDERED, and it has to be. Wren's group-less state is a consumable: the door test mints her a
// space, and anything needing "belongs to nothing" is impossible afterwards. Left unordered, NUnit
// runs them alphabetically and the scheduling test spent the seat before the door test could use
// it — two tests ignoring themselves on a brand-new database, which is the worst of both worlds.
// So the tests that need the seat run first, and the ones that work either way follow.
public class SoloInvestigatorTests : BenTestBase
{
    private const string SeededPlace = "40000001-0000-0000-0000-000000000001";
    private const string SeededPlaceName = "Bell Witch Cave";
    private const string FeedFlag = "features.public-feed";

    private static bool _wasAlreadyOn;

    /// <summary>Place posts are feed posts, so the box only exists when the feed is on.</summary>
    [OneTimeSetUp]
    public async Task TurnTheFeedOn()
    {
        var token = await AdminTokenAsync();
        if (token is null) return;

        using (var http = new HttpClient { BaseAddress = new Uri(ApiUrl) })
        {
            http.DefaultRequestHeaders.Authorization =
                new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);

            var settings = await http.GetFromJsonAsync<System.Text.Json.JsonElement>("/api/admin/site-settings");
            foreach (var setting in settings.EnumerateArray())
            {
                if (setting.GetProperty("key").GetString() != FeedFlag) continue;
                _wasAlreadyOn = setting.TryGetProperty("value", out var value)
                    && string.Equals(value.GetString(), "true", StringComparison.OrdinalIgnoreCase);
            }
        }

        await SetFeedAsync(token, on: true);
    }

    [OneTimeTearDown]
    public async Task PutTheFeedBack()
    {
        var token = await AdminTokenAsync();
        if (token is not null) await SetFeedAsync(token, on: _wasAlreadyOn);
    }

    private static async Task<string?> AdminTokenAsync()
    {
        using var http = new HttpClient { BaseAddress = new Uri(ApiUrl), Timeout = TimeSpan.FromSeconds(30) };
        try
        {
            using var response = await http.PostAsJsonAsync("/login",
                new { email = SuperAdminEmail, password = SuperAdminPassword });
            if (!response.IsSuccessStatusCode) return null;

            var json = await response.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>();
            return json.GetProperty("accessToken").GetString();
        }
        catch (HttpRequestException) { return null; }
    }

    private static async Task SetFeedAsync(string token, bool on)
    {
        using var http = new HttpClient { BaseAddress = new Uri(ApiUrl), Timeout = TimeSpan.FromSeconds(30) };
        http.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
        using var _ = await http.PutAsJsonAsync(
            $"/api/admin/site-settings/{FeedFlag}", new { value = on ? "true" : "false" });
    }

    private async Task<bool> OpenTheLandmarkAsync()
    {
        await LoginAsync(SoloEmail, SoloPassword);
        await Page.GotoAsync($"{BaseUrl}/places/{SeededPlace}");
        await WaitUntilLoadedAsync();
        return await Main.GetByText(SeededPlaceName, new() { Exact = false }).CountAsync() > 0;
    }

    /// <summary>
    /// The door: no group, and one button that explains itself and then works.
    /// </summary>
    /// <remarks>
    /// Written to tolerate Wren already having a space from an earlier run. When she does, the
    /// offer is gone and the per-group <c>Investigate here</c> is there instead — which is the
    /// correct page for somebody who now belongs somewhere, and is asserted as such rather than
    /// skipped over.
    /// </remarks>
    [Test, Order(2)]
    public async Task Somebody_with_no_group_can_start_investigating_a_public_place()
    {
        if (!await OpenTheLandmarkAsync())
            Assert.Ignore("the seeded landmark this walks is not on this database");

        var offer = Main.Locator(".place-investigate-solo");
        var perGroup = Main.Locator(".place-investigate");

        if (await offer.CountAsync() == 0)
        {
            // She already has a space, from an earlier run of this very test — so the seat this
            // needs is gone and the door cannot be driven. A MISSING PRECONDITION, reported as one:
            // ending as passed here would make a regression in the door report green, which is
            // what PlaywrightTestsCanFailTests exists to stop. The page must still offer the
            // ordinary route, so that much is checked before standing down.
            await Expect(perGroup.First).ToBeVisibleAsync(new() { Timeout = 20_000 });
            Assert.Ignore(
                "Wren already has a space of her own, so this database cannot exercise the door. "
                + "Run with a fresh BEN_E2E_DB to drive it.");
        }

        await offer.First.ClickAsync();

        // The sentence before the act. Both halves of the bargain are named, because agreeing to
        // one without the other is not agreeing.
        var dialog = Page.Locator(".modal, [role='dialog']").Filter(
            new() { HasTextString = "Investigating on your own" }).First;
        await Expect(dialog).ToBeVisibleAsync(new() { Timeout = 15_000 });
        await Expect(dialog).ToContainTextAsync("public");
        await Expect(dialog).ToContainTextAsync(SeededPlaceName);

        await Page.GetByTestId("solo-confirm").ClickAsync();

        // Straight on to scheduling, with the place already settled — she pressed a button that
        // said "start investigating", not "make me an account".
        var window = Page.Locator(".modal, [role='dialog']").Filter(
            new() { HasTextString = "Who can see the findings" }).First;
        await Expect(window).ToBeVisibleAsync(new() { Timeout = 30_000 });
        await Expect(window).ToContainTextAsync(SeededPlaceName);
    }

    /// <summary>
    /// The free lane's rule, seen on the page rather than in a unit test: her visit to a public
    /// location may only be shared with everyone, and the narrower choices are not offered.
    /// </summary>
    /// <remarks>
    /// This is the browser half of phase 1, which had to wait for a seat that pays nothing — both
    /// seeded groups are on active plans, so until Wren there was nothing to drive it with.
    /// </remarks>
    [Test, Order(3)]
    public async Task Her_visit_to_a_public_place_can_only_be_public()
    {
        if (!await OpenTheLandmarkAsync())
            Assert.Ignore("the seeded landmark this walks is not on this database");

        // Get her into the scheduling window, whichever door this run needs.
        var offer = Main.Locator(".place-investigate-solo");
        if (await offer.CountAsync() > 0)
        {
            await offer.First.ClickAsync();
            await Page.GetByTestId("solo-confirm").ClickAsync();
        }
        else
        {
            var perGroup = Main.Locator(".place-investigate");
            await Expect(perGroup.First).ToBeVisibleAsync(new() { Timeout = 20_000 });
            await perGroup.First.ClickAsync();
        }

        var scope = Page.Locator("#newinvestigationwindow-who-can-see-the-fea9");
        await Expect(scope).ToBeVisibleAsync(new() { Timeout = 30_000 });

        // Only "Anyone". The narrower scopes are withheld rather than offered and refused.
        await Expect(scope.Locator("option")).ToHaveCountAsync(1);
        await Expect(scope).ToContainTextAsync("Anyone");

        // And the page says why, rather than leaving a one-item dropdown to be puzzled over.
        await Expect(Page.GetByTestId("investigation-scope-locked"))
            .ToBeVisibleAsync(new() { Timeout = 10_000 });
    }

    /// <summary>
    /// She may post about a public place, which the feed's own front page would refuse her. The
    /// seam from phase 3, driven by the only seat that can actually show both sides of it.
    /// </summary>
    [Test, Order(4)]
    public async Task She_may_post_about_a_place_but_not_on_the_feed()
    {
        if (!await OpenTheLandmarkAsync())
            Assert.Ignore("the seeded landmark this walks is not on this database");

        var composer = Main.GetByTestId("place-composer");
        if (await composer.CountAsync() == 0)
            Assert.Ignore("the feed is switched off on this database, so a place takes no posts");

        var said = $"First visit for me {Guid.NewGuid():N}"[..36];
        await composer.Locator("textarea").First.FillAsync(said);
        await composer.GetByTestId("feed-composer-post").ClickAsync();

        await Expect(Main.GetByTestId("place-posts").GetByText(said, new() { Exact = false }))
            .ToBeVisibleAsync(new() { Timeout = 20_000 });
    }

    /// <summary>
    /// The other half of the seam: the feed's own front page is not hers, while she belongs to
    /// nothing. Its own test, because it is the half with a precondition.
    /// </summary>
    /// <remarks>
    /// Read on the PLACE page, where ".place-investigate" exists at all — asking for it on the feed
    /// would always say no and this would assert the wrong thing, which it did once.
    /// </remarks>
    [Test, Order(1)]
    public async Task While_she_belongs_to_nothing_the_feeds_front_page_is_not_hers()
    {
        if (!await OpenTheLandmarkAsync())
            Assert.Ignore("the seeded landmark this walks is not on this database");

        if (await Main.Locator(".place-investigate").CountAsync() > 0)
        {
            Assert.Ignore(
                "Wren has a space of her own on this database, so the feed is legitimately hers "
                + "too. Run with a fresh BEN_E2E_DB to exercise the refusal.");
        }

        await Page.GotoAsync($"{BaseUrl}/feed");
        await WaitUntilLoadedAsync();

        // No composer on the front page for somebody who belongs to nothing, and the page invites
        // rather than simply omitting it.
        await Expect(Main.Locator("#feed-composer")).ToHaveCountAsync(0);
    }
}
