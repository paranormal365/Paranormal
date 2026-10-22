using System.Text.Json;
using Ben.Web.Playwright.Tests.Oct16;
using Microsoft.Playwright;
using NUnit.Framework;

namespace Ben.Web.Playwright.Tests;

/// <summary>
/// The Ghost Walk Tour subscription per tour route (Ben, 10/21/2026): "$29 a month per tour route,
/// unlimited walks and guests". The words on every page that sells it, the route count on the billing
/// card, and the ask when every paid route is running — at every width from 320 to 1920.
/// </summary>
/// <remarks>
/// <para><b>Stripe is reached, never paid.</b> A real subscription can't be bought from a browser test,
/// so the tests that need one standing are skipped with a reason when the group has none; the route
/// arithmetic, renewals and the per-tour plan's move are covered by the unit tests.</para>
///
/// <para>Settings are put back in TearDown. Written for
/// <c>scripts/run-e2e.sh --filter TestCategory=EventCreditsAndTours</c>; not yet run.</para>
/// </remarks>
[TestFixture]
[Category("EventCreditsAndTours")]
[NonParallelizable]
public class GhostWalkPerRouteTests : Oct16TestBase
{
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
        foreach (var orgId in _groups) await _admin.DeleteAsync($"/api/organizations/{orgId}");
        _groups.Clear();
    }

    private async Task SetAsync(string key, string? value)
    {
        if (!_restore.ContainsKey(key))
        {
            var all = await OkJsonAsync(await _admin.GetAsync("/api/admin/site-settings"), "reading site settings");
            var row = all.EnumerateArray().First(s => s.GetProperty("key").GetString() == key);
            _restore[key] = row.TryGetProperty("value", out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
        }
        await OkJsonAsync(await _admin.PutAsync($"/api/admin/site-settings/{key}", new() { DataObject = new { value } }), $"setting {key}");
    }

    private async Task<string> NewGroupAsync()
    {
        var made = await OkJsonAsync(await _sarah.PostAsync("/api/security/organizations/register", new()
        {
            DataObject = new { name = "Playwright Route House", urlName = $"pw-route-{Guid.NewGuid():N}"[..20] },
        }), "registering a group");
        var id = made.GetProperty("organizationId").GetString()!;
        _groups.Add(id);
        return id;
    }

    [Test]
    public async Task With_no_price_set_the_subscription_sells_at_29_dollars_a_route()
    {
        await SetAsync(WalkPriceKey, null);
        await SetAsync(WalkOnKey, "true");

        var extras = await OkJsonAsync(await _admin.GetAsync("/api/public/pricing/extras"), "reading the price list");
        Assert.That(extras.GetProperty("ghostWalk").GetProperty("monthlyPrice").GetDecimal(), Is.EqualTo(29m));
    }

    [TestCase("dark")]
    [TestCase("light")]
    public async Task Every_page_that_sells_it_says_29_dollars_a_month_per_tour_route_on_every_screen(string theme)
    {
        await SetAsync(WalkPriceKey, null);
        await SetAsync(WalkOnKey, "true");
        await UseThemeAsync(theme);

        var problems = new List<string>();
        foreach (var (width, height) in Widths)
        {
            await Page.SetViewportSizeAsync(width, height);

            // The prerendered page is replaced when the circuit starts; looking at it before then
            // finds an element that is gone a moment later ("not attached to the DOM").
            await Page.GotoAsync(BaseUrl);
            await WaitForTheCircuitAsync();
            var line = Page.Locator("[data-testid='home-join-ghost-walk']");
            await Expect(line).ToContainTextAsync("$29", new() { Timeout = 30_000 });
            await Expect(line).ToContainTextAsync("a month per tour route");
            await Expect(line).ToContainTextAsync("unlimited walks and guests");
            problems.AddRange(await LookAtAsync(line, "home-ghost-walk-per-route", theme));

            await Page.GotoAsync($"{BaseUrl}/pricing");
            await WaitForTheCircuitAsync();
            var offer = Page.Locator("[data-testid='pricing-ghost-walk-offer']");
            await Expect(offer).ToContainTextAsync("a month per tour route", new() { Timeout = 30_000 });
            await Expect(Page.Locator("[data-testid='pricing-ghost-walk']")).ToContainTextAsync("Unlimited walks");
            problems.AddRange(await LookAtAsync(offer, "pricing-ghost-walk-per-route", theme));
        }
        NothingWrong(problems);
    }

    [TestCase("dark")]
    [TestCase("light")]
    public async Task The_billing_card_prices_the_routes_the_group_runs_on_every_screen(string theme)
    {
        await SetAsync(WalkPriceKey, null);
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
            await Expect(card).ToContainTextAsync("a month for each tour route", new() { Timeout = 30_000 });
            // A new group runs no routes yet, and still pays for the one it is about to define.
            await Expect(Page.Locator("[data-testid='ghost-walk-month-total']")).ToHaveTextAsync("$29");
            problems.AddRange(await LookAtAsync(card, "billing-ghost-walk-per-route", theme));
            problems.AddRange(await TouchTargetAsync(Page.Locator("[data-testid='ghost-walk-subscribe']"), "Subscribe", theme));
        }
        NothingWrong(problems);
    }

    [Test]
    public async Task Subscribing_pays_for_at_least_the_routes_the_group_runs()
    {
        await SetAsync(WalkPriceKey, null);
        await SetAsync(WalkOnKey, "true");
        var orgId = await NewGroupAsync();

        var tooFew = await _sarah.PostAsync($"/api/organizations/{orgId}/subscription/checkout/ghost-walk?routes=0", new() { DataObject = new { } });
        Assert.That(tooFew.Status, Is.EqualTo(400), await tooFew.TextAsync());

        var start = await _sarah.PostAsync($"/api/organizations/{orgId}/subscription/checkout/ghost-walk?routes=2", new() { DataObject = new { } });
        if (start.Status == 503) Assert.Ignore("Online payment is not configured on this deployment.");
        var url = (await OkJsonAsync(start, "starting a two-route checkout")).GetProperty("redirectUrl").GetString();
        Assert.That(url, Does.Contain("stripe.com"));
    }

    [TestCase("dark")]
    [TestCase("light")]
    public async Task With_every_paid_route_running_the_tours_page_asks_before_adding_one(string theme)
    {
        await SetAsync(WalkPriceKey, null);
        await SetAsync(WalkOnKey, "true");
        var orgId = await OrgIdBySlugAsync(GroupSlug);

        var plan = await OkJsonAsync(await _sarah.GetAsync($"/api/organizations/{orgId}/tours/plan"), "reading the tour plan");
        if (!plan.GetProperty("ghostWalkStanding").GetBoolean())
            Assert.Ignore("The seeded group holds no Ghost Walk Tour subscription; a browser test can't buy one.");
        if (plan.GetProperty("activeTours").GetInt32() < plan.GetProperty("routesPaid").GetInt32())
            Assert.Ignore("The seeded group has a paid route free, so nothing is asked.");

        await UseThemeAsync(theme);
        await LoginAsync(UserEmail, UserPassword);
        var problems = new List<string>();
        foreach (var (width, height) in Widths)
        {
            await Page.SetViewportSizeAsync(width, height);
            await Page.GotoAsync($"{BaseUrl}/organizations/{orgId}/tours");
            await WaitForTheCircuitAsync();

            var ask = Page.Locator("label[for='tour-add-route']");
            await Expect(ask).ToContainTextAsync("Add a route to my Ghost Walk Tour subscription", new() { Timeout = 30_000 });
            await Expect(ask).ToContainTextAsync("on your next bill");
            await Expect(Page.Locator("#tour-add")).ToBeDisabledAsync();
            problems.AddRange(await LookAtAsync(ask, "tours-add-route-ask", theme));
        }
        NothingWrong(problems);
    }

    [Test]
    public async Task The_help_says_what_the_customer_sees_on_the_bill()
    {
        await SetAsync(WalkPriceKey, null);
        await SetAsync(WalkOnKey, "true");
        await LoginAsync(UserEmail, UserPassword);

        await Page.GotoAsync($"{BaseUrl}/help/organization-administration#adding-a-tour-and-what-it-costs");
        await WaitForTheCircuitAsync();
        var body = Page.Locator(".help-body");
        await Expect(body).ToContainTextAsync("$29 a month per tour route", new() { Timeout = 30_000 });
        await Expect(body).ToContainTextAsync("go on your next monthly bill");
        await Expect(body).Not.ToContainTextAsync("On the per-tour business plan");
        await Expect(body).Not.ToContainTextAsync("{{");
    }
}
