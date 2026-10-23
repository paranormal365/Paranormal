using System.Globalization;
using System.Text.Json;
using Ben.Web.Playwright.Tests.Oct16;
using Microsoft.Playwright;
using NUnit.Framework;

namespace Ben.Web.Playwright.Tests;

/// <summary>
/// Event credits and the Ghost Walk Tour subscription on the running site (Ben, 10/21/2026): the
/// prices the SuperAdmin sets reach every page that shows them, no page says a plan includes events,
/// the buy flows reach Stripe, and every new piece fits from 320 to 1920 pixels.
/// </summary>
/// <remarks>
/// <para><b>Settings are put back.</b> Every test that changes a site setting reads its value first
/// and restores it in <c>TearDown</c>, so a run leaves the site priced as it found it.</para>
///
/// <para><b>Stripe is reached, never paid.</b> A buy flow is proved as far as the session Stripe hands
/// back, which exercises permission, price and the metadata the webhook later reads. Completing a
/// payment and the webhook are covered by the unit tests, which can replay a delivery. When online
/// payment isn't configured the API answers 503 and the test says so rather than failing.</para>
///
/// <para>Written for <c>scripts/run-e2e.sh --filter TestCategory=EventCreditsAndTours</c>; run against
/// a fresh <c>BEN_E2E_DB</c>.</para>
/// </remarks>
[TestFixture]
[Category("EventCreditsAndTours")]
[NonParallelizable]
public class EventCreditsAndToursTests : Oct16TestBase
{
    private const string PriceKey = "events.credit-price-usd";
    private const string PercentKey = "events.credit-subscriber-percent";
    private const string BundlesKey = "events.credit-bundles";
    private const string WalkOnKey = "tours.ghost-walk-enabled";
    private const string WalkPriceKey = "tours.ghost-walk-monthly-price-usd";

    private IAPIRequestContext _admin = null!;
    private IAPIRequestContext _sarah = null!;
    private readonly Dictionary<string, string?> _restore = [];
    private readonly List<string> _groups = [];

    [SetUp]
    public async Task SignIn()
    {
        _admin = await ApiAsAsync(SuperAdminEmail, SuperAdminPassword);
        _sarah = await ApiAsAsync(UserEmail, UserPassword);
    }

    [TearDown]
    public async Task PutTheSiteBack()
    {
        foreach (var (key, value) in _restore)
            await _admin.PutAsync($"/api/admin/site-settings/{key}", new() { DataObject = new { value } });
        _restore.Clear();

        foreach (var orgId in _groups)
            await _admin.DeleteAsync($"/api/organizations/{orgId}");
        _groups.Clear();
    }

    // ── helpers ──────────────────────────────────────────────────────────────────────────────────

    /// <summary>Sets a site setting as the SuperAdmin, remembering what it was for TearDown.</summary>
    private async Task SetAsync(string key, string? value)
    {
        if (!_restore.ContainsKey(key))
        {
            var all = await OkJsonAsync(await _admin.GetAsync("/api/admin/site-settings"), "reading site settings");
            var row = all.EnumerateArray().First(s => s.GetProperty("key").GetString() == key);
            _restore[key] = row.TryGetProperty("value", out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
        }
        await OkJsonAsync(await _admin.PutAsync($"/api/admin/site-settings/{key}", new() { DataObject = new { value } }),
            $"setting {key}");
    }

    /// <summary>A fresh group with Sarah as owner and no plan, deleted in TearDown.</summary>
    private async Task<string> NewGroupAsync()
    {
        var slug = $"pw-ect-{Guid.NewGuid():N}"[..20];
        var made = await OkJsonAsync(await _sarah.PostAsync("/api/security/organizations/register", new()
        {
            DataObject = new { name = "Playwright Credits House", urlName = slug },
        }), "registering a group");
        var id = made.GetProperty("organizationId").GetString()!;
        _groups.Add(id);
        return id;
    }

    private static string Money(decimal amount)
        => amount % 1 == 0 ? amount.ToString("C0", CultureInfo.GetCultureInfo("en-US"))
                           : amount.ToString("C2", CultureInfo.GetCultureInfo("en-US"));

    private async Task<JsonElement> ExtrasAsync()
        => await OkJsonAsync(await _admin.GetAsync("/api/public/pricing/extras"), "reading the price list's extras");

    // ── Site Settings ────────────────────────────────────────────────────────────────────────────

    [TestCase("dark")]
    [TestCase("light")]
    public async Task The_event_and_ghost_walk_settings_are_on_the_settings_page_on_every_screen(string theme)
    {
        await UseThemeAsync(theme);
        await LoginAsync(SuperAdminEmail, SuperAdminPassword);

        var problems = new List<string>();
        foreach (var (width, height) in Widths)
        {
            await Page.SetViewportSizeAsync(width, height);
            await Page.GotoAsync($"{BaseUrl}/admin/site-settings?setting={PercentKey}");
            await WaitForTheCircuitAsync();

            var percent = Page.Locator(".card", new() { HasTextString = PercentKey });
            problems.AddRange(await LookAtAsync(percent, "settings-subscriber-percent", theme));
            await Expect(Page.Locator(".card", new() { HasTextString = BundlesKey })).ToBeVisibleAsync();

            await Page.GotoAsync($"{BaseUrl}/admin/site-settings?setting={WalkPriceKey}");
            await WaitForTheCircuitAsync();
            problems.AddRange(await LookAtAsync(Page.Locator(".card", new() { HasTextString = WalkPriceKey }), "settings-ghost-walk-price", theme));
            await Expect(Page.Locator($"[id='sw-{WalkOnKey}']")).ToBeAttachedAsync();
        }
        NothingWrong(problems);
    }

    [Test]
    public async Task A_percentage_over_100_is_refused_on_the_page_with_the_server_s_sentence()
    {
        await LoginAsync(SuperAdminEmail, SuperAdminPassword);
        await Page.GotoAsync($"{BaseUrl}/admin/site-settings?setting={PercentKey}");
        await WaitForTheCircuitAsync();

        var card = Page.Locator(".card", new() { HasTextString = PercentKey });
        await card.Locator("input").FillAsync("150");
        await card.GetByRole(AriaRole.Button, new() { Name = "Save" }).ClickAsync();

        // The server's own words, not "Couldn't save that setting".
        await Expect(Page.Locator(".alert-danger")).ToContainTextAsync("from 1 to 100", new() { Timeout = 15_000 });

        var all = await OkJsonAsync(await _admin.GetAsync("/api/admin/site-settings"), "reading site settings");
        var stored = all.EnumerateArray().First(s => s.GetProperty("key").GetString() == PercentKey);
        Assert.That(stored.TryGetProperty("value", out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null,
            Is.Not.EqualTo("150"), "a refused value must not be stored");
    }

    // ── The prices on screen come from the settings ──────────────────────────────────────────────

    [TestCase("dark")]
    [TestCase("light")]
    public async Task The_pricing_page_sells_events_at_the_settings_prices_and_never_says_a_plan_includes_them(string theme)
    {
        await SetAsync(PriceKey, "120");
        await SetAsync(PercentKey, "25");
        await SetAsync(BundlesKey, "3 = 300");
        await UseThemeAsync(theme);

        var problems = new List<string>();
        foreach (var (width, height) in Widths)
        {
            await Page.SetViewportSizeAsync(width, height);
            await Page.GotoAsync($"{BaseUrl}/pricing");
            var offer = Page.Locator("[data-testid='pricing-event-offer']");
            await Expect(offer).ToBeVisibleAsync(new() { Timeout = 30_000 });

            await Expect(Page.Locator("[data-testid='pricing-event-price']")).ToHaveTextAsync("$120");
            await Expect(Page.Locator("[data-testid='pricing-event-subscriber-price']")).ToContainTextAsync("$30");
            await Expect(Page.Locator("[data-testid='pricing-event-bundles']")).ToContainTextAsync("3 events for $300");
            await Expect(Page.Locator("body")).Not.ToContainTextAsync("Every plan includes hosted events");
            await Expect(Page.Locator("body")).Not.ToContainTextAsync("Hosted events included");

            problems.AddRange(await LookAtAsync(offer, "pricing-event-offer", theme));
        }
        NothingWrong(problems);
    }

    [Test]
    public async Task The_home_page_ad_reads_the_settings_and_says_nothing_of_ghost_walks_while_they_are_off()
    {
        await SetAsync(PriceKey, "110");
        await SetAsync(WalkOnKey, "false");

        await Page.GotoAsync(BaseUrl);
        var join = Page.Locator("[data-testid='home-join']");
        await Expect(join).ToBeVisibleAsync(new() { Timeout = 30_000 });

        await Expect(Page.Locator("[data-testid='home-join-event-price']")).ToContainTextAsync("$110");
        await Expect(Page.Locator("[data-testid='home-join-ghost-walk']")).ToHaveCountAsync(0);
        await Expect(join).Not.ToContainTextAsync("ghost walk", new() { IgnoreCase = true });
        await Expect(join).Not.ToContainTextAsync("$99");
        await Expect(join).Not.ToContainTextAsync("$29");
    }

    [TestCase("dark")]
    [TestCase("light")]
    public async Task With_the_ghost_walk_subscription_on_its_price_is_on_the_home_page_and_the_pricing_page(string theme)
    {
        await SetAsync(WalkPriceKey, "34");
        await SetAsync(WalkOnKey, "true");
        await UseThemeAsync(theme);

        var problems = new List<string>();
        foreach (var (width, height) in Widths)
        {
            await Page.SetViewportSizeAsync(width, height);

            // The circuit replaces the prerendered page when it connects, so a line found before then
            // can be gone by the time it is scrolled to ("Element is not attached to the DOM").
            await Page.GotoAsync(BaseUrl);
            await WaitForTheCircuitAsync();
            var line = Page.Locator("[data-testid='home-join-ghost-walk']");
            await Expect(line).ToContainTextAsync("$34", new() { Timeout = 30_000 });
            problems.AddRange(await LookAtAsync(line, "home-ghost-walk-line", theme));

            await Page.GotoAsync($"{BaseUrl}/pricing");
            await WaitForTheCircuitAsync();
            var offer = Page.Locator("[data-testid='pricing-ghost-walk-offer']");
            await Expect(Page.Locator("[data-testid='pricing-ghost-walk-price']")).ToHaveTextAsync("$34", new() { Timeout = 30_000 });
            problems.AddRange(await LookAtAsync(offer, "pricing-ghost-walk-offer", theme));
        }
        NothingWrong(problems);
    }

    // ── The Pricing page's link down to them (Ben, 10/23/2026) ─────────────────────────────────

    private const string CreditsOnKey = "events.credits-enabled";
    private ILocator JumpBelow => Page.GetByTestId("pricing-jump-below");

    private async Task OpenPricingAsync()
    {
        await Page.SetViewportSizeAsync(1440, 900);
        await Page.GotoAsync($"{BaseUrl}/pricing");
        await WaitForTheCircuitAsync();
        await Expect(Page.Locator("[data-testid='pricing-band']").First).ToBeVisibleAsync(new() { Timeout = 30_000 });
    }

    [Test]
    public async Task With_events_and_ghost_walks_on_sale_the_top_of_Pricing_takes_you_to_them()
    {
        await SetAsync(CreditsOnKey, "true");
        await SetAsync(PriceKey, "29");
        await SetAsync(WalkPriceKey, "34");
        await SetAsync(WalkOnKey, "true");
        await OpenPricingAsync();

        await Expect(JumpBelow).ToHaveTextAsync("Events and ghost walks");
        await Expect(Page.GetByTestId("pricing-events")).Not.ToBeInViewportAsync();

        await JumpBelow.ClickAsync();
        await Expect(Page.GetByTestId("pricing-events")).ToBeInViewportAsync(new() { Timeout = 10_000 });
        Assert.That(new Uri(Page.Url).AbsolutePath, Is.EqualTo("/pricing"), "the link left the Pricing page");

        // On a phone it, and How plans work beside it, are a finger's size.
        await Page.SetViewportSizeAsync(375, 812);
        await Page.GotoAsync($"{BaseUrl}/pricing");
        await WaitForTheCircuitAsync();
        var problems = await TouchTargetAsync(JumpBelow, "the link to events", "dark");
        problems.AddRange(await TouchTargetAsync(Page.GetByRole(AriaRole.Link, new() { Name = "How plans work" }), "How plans work", "dark"));
        NothingWrong(problems);
    }

    [Test]
    public async Task With_only_ghost_walks_on_sale_the_link_says_so_and_goes_to_them()
    {
        await SetAsync(CreditsOnKey, "false");
        await SetAsync(WalkPriceKey, "34");
        await SetAsync(WalkOnKey, "true");
        await OpenPricingAsync();

        await Expect(JumpBelow).ToHaveTextAsync("Ghost walk tours");
        await JumpBelow.ClickAsync();
        await Expect(Page.GetByTestId("pricing-ghost-walk")).ToBeInViewportAsync(new() { Timeout = 10_000 });
    }

    [Test]
    public async Task With_neither_on_sale_there_is_no_link()
    {
        await SetAsync(CreditsOnKey, "false");
        await SetAsync(WalkOnKey, "false");
        await OpenPricingAsync();

        await Expect(JumpBelow).ToHaveCountAsync(0);
    }

    /// <summary>
    /// The home page's price lines stay drawn when the page comes alive (10/22/2026).
    /// </summary>
    /// <remarks>
    /// The server drew them, then the live page fetched the prices again and drew the list without
    /// them until the answer came, so the ghost walk line vanished and came back on every visit.
    /// PagesDoNotBlinkTests watches for a spinner and could not see it, because nothing spins: a
    /// line is simply missing for a moment. This watches the line itself, from the first paint.
    /// </remarks>
    [Test]
    public async Task With_the_ghost_walk_subscription_on_its_home_page_line_never_vanishes()
    {
        await SetAsync(WalkPriceKey, "34");
        await SetAsync(WalkOnKey, "true");

        await Page.AddInitScriptAsync("""
            window.__line = { drawn: false, vanished: false };
            const look = () => {
                const there = !!document.querySelector("[data-testid='home-join-ghost-walk']");
                if (there) window.__line.drawn = true;
                else if (window.__line.drawn) window.__line.vanished = true;
            };
            document.addEventListener('DOMContentLoaded', () => {
                look();
                new MutationObserver(look).observe(document, { childList: true, subtree: true });
            });
            """);

        await Page.GotoAsync(BaseUrl);
        await WaitForTheCircuitAsync();
        await Page.WaitForTimeoutAsync(2500);   // the live page's first render and whatever it fetches

        Assert.That(await Page.EvaluateAsync<bool>("window.__line.drawn"), Is.True,
            "the ghost walk line was never drawn on the home page");
        Assert.That(await Page.EvaluateAsync<bool>("window.__line.vanished"), Is.False,
            "the ghost walk line was drawn, then disappeared when the page came alive");
    }

    [Test]
    public async Task With_the_ghost_walk_subscription_off_the_pricing_page_and_help_say_nothing_about_it()
    {
        await SetAsync(WalkPriceKey, "34");
        await SetAsync(WalkOnKey, "false");

        var extras = await ExtrasAsync();
        Assert.That(extras.TryGetProperty("ghostWalk", out var gw) && gw.ValueKind != JsonValueKind.Null, Is.False,
            "the price list must not send the offer while it is off");

        await Page.GotoAsync($"{BaseUrl}/pricing");
        await Expect(Page.Locator("[data-testid='pricing-events']")).ToBeVisibleAsync(new() { Timeout = 30_000 });
        await Expect(Page.Locator("[data-testid='pricing-ghost-walk']")).ToHaveCountAsync(0);

        await LoginAsync(UserEmail, UserPassword);
        await Page.GotoAsync($"{BaseUrl}/help/organization-administration");
        await WaitForTheCircuitAsync();
        await Expect(Page.Locator(".help-body")).ToBeVisibleAsync(new() { Timeout = 30_000 });
        await Expect(Page.Locator(".help-body")).Not.ToContainTextAsync("Ghost Walk Tour subscription");
        await Expect(Page.Locator(".help-body")).Not.ToContainTextAsync("{{");
    }

    [Test]
    public async Task The_help_quotes_today_s_event_credit_price()
    {
        await SetAsync(PriceKey, "105");
        await SetAsync(PercentKey, "20");

        await LoginAsync(UserEmail, UserPassword);
        await Page.GotoAsync($"{BaseUrl}/help/organization-administration#what-publishing-does");
        await WaitForTheCircuitAsync();
        var body = Page.Locator(".help-body");
        await Expect(body).ToContainTextAsync("$105", new() { Timeout = 30_000 });
        await Expect(body).ToContainTextAsync("$21");
        await Expect(body).Not.ToContainTextAsync("{{");
    }

    // ── Buying ───────────────────────────────────────────────────────────────────────────────────

    [TestCase("dark")]
    [TestCase("light")]
    public async Task A_group_s_billing_page_offers_credits_and_bundles_and_buying_reaches_stripe(string theme)
    {
        await SetAsync(BundlesKey, "3 = 249");
        var orgId = await NewGroupAsync();
        await UseThemeAsync(theme);
        await LoginAsync(UserEmail, UserPassword);

        var problems = new List<string>();
        foreach (var (width, height) in Widths)
        {
            await Page.SetViewportSizeAsync(width, height);
            await Page.GotoAsync($"{BaseUrl}/organizations/{orgId}/billing");
            await WaitForTheCircuitAsync();

            var card = Page.Locator("[data-testid='event-credits-card']");
            await Expect(card).ToContainTextAsync("no plan includes events", new() { Timeout = 30_000 });
            await Expect(card.Locator("[data-testid='event-credits-bundle']")).ToBeVisibleAsync();
            problems.AddRange(await LookAtAsync(card, "billing-event-credits", theme));
            problems.AddRange(await TouchTargetAsync(Page.Locator("#buy-event-credits"), "Buy credits", theme));
        }
        NothingWrong(problems);

        var start = await _sarah.PostAsync(
            $"/api/organizations/{orgId}/subscription/checkout/event-credits?quantity=1&bundle=3", new() { DataObject = new { } });
        if (start.Status == 503) Assert.Ignore("Online payment is not configured on this deployment.");
        var url = (await OkJsonAsync(start, "starting a bundle checkout")).GetProperty("redirectUrl").GetString();
        Assert.That(url, Does.Contain("stripe.com"));
    }

    [TestCase("dark")]
    [TestCase("light")]
    public async Task A_person_s_own_credits_page_fits_every_screen_and_buying_reaches_stripe(string theme)
    {
        await UseThemeAsync(theme);
        await LoginAsync(UserEmail, UserPassword);

        var problems = new List<string>();
        foreach (var (width, height) in Widths)
        {
            await Page.SetViewportSizeAsync(width, height);
            await Page.GotoAsync($"{BaseUrl}/my-event-credits");
            await WaitForTheCircuitAsync();

            var card = Page.Locator("[data-testid='event-credits-card']");
            await Expect(card).ToBeVisibleAsync(new() { Timeout = 30_000 });
            // Blazor's error bar is always in the page, hidden, with "An unhandled error has occurred"
            // in it, so the body's text always contains the words; whether the bar is SHOWN is the test.
            await Expect(Page.Locator("#blazor-error-ui")).ToBeHiddenAsync();
            await Expect(card).Not.ToContainTextAsync("You holds");
            problems.AddRange(await LookAtAsync(card, "my-event-credits", theme));
        }
        NothingWrong(problems);

        var start = await _sarah.PostAsync("/api/me/event-credits/checkout?quantity=1", new() { DataObject = new { } });
        if (start.Status == 503) Assert.Ignore("Online payment is not configured on this deployment.");
        var url = (await OkJsonAsync(start, "starting a personal checkout")).GetProperty("redirectUrl").GetString();
        Assert.That(url, Does.Contain("stripe.com"));
    }

    [TestCase("dark")]
    [TestCase("light")]
    public async Task With_the_subscription_on_a_group_can_subscribe_and_a_new_tour_waits_for_it(string theme)
    {
        await SetAsync(WalkPriceKey, "29");
        await SetAsync(WalkOnKey, "true");
        var orgId = await NewGroupAsync();
        await UseThemeAsync(theme);
        await LoginAsync(UserEmail, UserPassword);

        var problems = new List<string>();
        foreach (var (width, height) in Widths)
        {
            await Page.SetViewportSizeAsync(width, height);
            await Page.GotoAsync($"{BaseUrl}/organizations/{orgId}/billing");
            await WaitForTheCircuitAsync();

            var card = Page.Locator("[data-testid='ghost-walk-card']");
            await Expect(Page.Locator("[data-testid='ghost-walk-price']")).ToHaveTextAsync("$29", new() { Timeout = 30_000 });
            problems.AddRange(await LookAtAsync(card, "billing-ghost-walk", theme));
            problems.AddRange(await TouchTargetAsync(Page.Locator("[data-testid='ghost-walk-subscribe']"), "Subscribe", theme));
        }
        NothingWrong(problems);

        // A new tour is refused with the price and the way in, and the refusal reaches the person.
        var addresses = await OkJsonAsync(await _sarah.GetAsync($"/api/organizations/{orgId}/addresses"), "reading addresses");
        if (addresses.GetArrayLength() > 0)
        {
            var addressId = addresses[0].GetProperty("id").GetString();
            var tour = await _sarah.PostAsync($"/api/organizations/{orgId}/tours", new()
            {
                DataObject = new { name = "Playwright walk", startOrganizationAddressId = addressId, durationMinutes = 90, defaultCapacity = 20 },
            });
            Assert.That(tour.Status, Is.EqualTo(400), await tour.TextAsync());
            Assert.That(await tour.TextAsync(), Does.Contain("$29.00").And.Contain("Billing"));
        }

        var start = await _sarah.PostAsync($"/api/organizations/{orgId}/subscription/checkout/ghost-walk", new() { DataObject = new { } });
        if (start.Status == 503) Assert.Ignore("Online payment is not configured on this deployment.");
        var url = (await OkJsonAsync(start, "starting the ghost walk checkout")).GetProperty("redirectUrl").GetString();
        Assert.That(url, Does.Contain("stripe.com"));
    }

    [Test]
    public async Task Turned_off_the_ghost_walk_card_is_gone_from_the_billing_page()
    {
        await SetAsync(WalkPriceKey, "29");
        await SetAsync(WalkOnKey, "false");
        var orgId = await NewGroupAsync();
        await LoginAsync(UserEmail, UserPassword);

        await Page.GotoAsync($"{BaseUrl}/organizations/{orgId}/billing");
        await WaitForTheCircuitAsync();
        await Expect(Page.Locator("[data-testid='event-credits-card']")).ToBeVisibleAsync(new() { Timeout = 30_000 });
        await Expect(Page.Locator("[data-testid='ghost-walk-card']")).ToHaveCountAsync(0);
        await Expect(Page.Locator("body")).Not.ToContainTextAsync("Ghost Walk");
    }

    [Test]
    public async Task Guests_are_still_told_the_site_takes_none_of_their_money()
    {
        await Page.GotoAsync($"{BaseUrl}/pricing");
        await Expect(Page.Locator("[data-testid='pricing-event-features']"))
            .ToContainTextAsync("The site never takes their money", new() { Timeout = 30_000 });
    }
}
