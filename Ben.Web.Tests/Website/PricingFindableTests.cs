using System.Text.RegularExpressions;
using Ben.Data.WebApi.Services;
using Ben.Service.Models;
using Ben.Service.Models.Admin;
using Ben.Service.Models.Entities;
using Ben.Service.Models.Support;
using Ben.Web.Services;
using Ben.Web.Services.WebApi;
using Ben.Web.Website.Components.Layout;
using Ben.Web.Website.Library.Kit;
using Ben.Web.Website.Library.Shared;
using Ben.Web.Website.Library.SuperAdmin;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.JSInterop;
using Moq;
using Xunit;

namespace Ben.Web.Tests.Website;

/// <summary>
/// Pricing can be found (Ben, 10/22/2026): "There is no direct menu link to prices on the home
/// page. The way I am finding it is going to Community then Pricing" and "I don't see, as an
/// administrator, how to configure event settings like pricing."
/// </summary>
/// <remarks>
/// <para>Rendered, because the claim is what a person sees: the top bar, the drawer, the home page
/// and the footer each link to /pricing, signed out and signed in, and none of them does when
/// plans and event credits are both off. Administration has a Pricing entry, and /admin/pricing
/// refuses anybody who is not a SuperAdmin before it asks the API for anything.</para>
///
/// <para>Each was run against the code before the change and failed: the bar had no Pricing entry
/// (signed out it had none at all; signed in it was only inside Community), the home page and the
/// footer had no link, and /admin/pricing did not exist.</para>
/// </remarks>
public sealed class PricingFindableTests
{
    private sealed class FakeNav : NavigationManager
    {
        public string? WentTo { get; private set; }
        public FakeNav(string path = "") => Initialize("https://unit.test/", "https://unit.test/" + path);
        protected override void NavigateToCore(string uri, NavigationOptions options) => WentTo = uri;
    }

    // ── The features the site reports ───────────────────────────────────────

    private static async Task<SiteFeaturesProvider> FeaturesAsync(bool plans, bool events)
    {
        var client = new Mock<IBenAdminClient>();
        client.Setup(c => c.GetSiteFeaturesAsync(It.IsAny<CancellationToken>()))
              .ReturnsAsync(new SiteFeaturesInfo(
                  new Dictionary<string, bool>(SiteFeaturesProvider.Defaults), PlanPurchasesEnabled: plans, EventCreditsEnabled: events));
        var services = new ServiceCollection();
        services.AddScoped(_ => client.Object);
        var provider = new SiteFeaturesProvider(
            services.BuildServiceProvider().GetRequiredService<IServiceScopeFactory>(),
            NullLogger<SiteFeaturesProvider>.Instance);
        await provider.PrimeAsync();
        return provider;
    }

    private static async Task<string> RenderStaticAsync<T>(SiteFeaturesProvider features, bool signedIn,
        Dictionary<string, object?>? parameters = null, bool superAdmin = false) where T : IComponent
    {
        var userState = new Mock<IBenUserState>();
        userState.SetupGet(u => u.IsAuthenticated).Returns(signedIn);
        userState.SetupGet(u => u.IsSuperAdmin).Returns(superAdmin);
        userState.SetupGet(u => u.AuthReady).Returns(Task.CompletedTask);
        var tokens = new Mock<IWebApiTokenStore>();
        tokens.SetupGet(t => t.IsAuthenticated).Returns(signedIn);
        tokens.SetupGet(t => t.IsSuperAdmin).Returns(superAdmin);
        var client = new Mock<IBenAdminClient>();

        var services = new ServiceCollection();
        var nav = new FakeNav();
        services.AddSingleton<NavigationManager>(nav);
        services.AddSingleton(userState.Object);
        services.AddSingleton(tokens.Object);
        services.AddSingleton(new Mock<IWebApiAuthService>().Object);
        services.AddSingleton(client.Object);
        services.AddSingleton(new NotificationState(client.Object, userState.Object, nav));
        services.AddSingleton(features);
        services.AddSingleton(new RailMenuState());
        services.AddSingleton(new Ben.Web.Website.Services.NavGroupsMemory(new Microsoft.Extensions.Caching.Memory.MemoryCache(new Microsoft.Extensions.Caching.Memory.MemoryCacheOptions())));
        services.AddSingleton(new Mock<IJSRuntime>().Object);
        services.AddSingleton<IOptions<Ben.Data.Common.SiteIdentity>>(Options.Create(new Ben.Data.Common.SiteIdentity()));
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());

        await using var provider = services.BuildServiceProvider();
        await using var renderer = new HtmlRenderer(provider, NullLoggerFactory.Instance);
        return await renderer.Dispatcher.InvokeAsync(async () =>
        {
            var output = await renderer.RenderComponentAsync<T>(ParameterView.FromDictionary(parameters ?? []));
            return System.Net.WebUtility.HtmlDecode(output.ToHtmlString());
        });
    }

    private static Task<string> TopBarAsync(SiteFeaturesProvider features, bool signedIn)
        => RenderStaticAsync<BenNav>(features, signedIn, new()
        {
            [nameof(BenNav.TopBar)] = true,
            [nameof(BenNav.IsAuthenticated)] = signedIn,
        });

    private static Task<string> DrawerAsync(SiteFeaturesProvider features, bool signedIn, bool superAdmin = false)
        => RenderStaticAsync<BenNav>(features, signedIn, new()
        {
            [nameof(BenNav.IsAuthenticated)] = signedIn,
            [nameof(BenNav.IsSuperAdmin)] = superAdmin,
        }, superAdmin);

    /// <summary>The bar's own Pricing entry: a topnav link to /pricing that says Pricing.</summary>
    private static readonly Regex BarPricing =
        new(@"<a class=""topnav-link"" href=""/pricing""[^>]*>.*?<span>Pricing</span>", RegexOptions.Singleline);

    private static int LinksTo(string html, string href)
        => Regex.Matches(html, $@"href=""{Regex.Escape(href)}""").Count;

    // ── The top bar ────────────────────────────────────────────────────────

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task The_top_bar_shows_Pricing_signed_out_and_signed_in(bool signedIn)
    {
        var html = await TopBarAsync(await FeaturesAsync(plans: true, events: true), signedIn);

        Assert.Matches(BarPricing, html);
    }

    [Theory]
    [InlineData(false, true)]
    [InlineData(true, false)]
    public async Task Either_selling_switch_alone_keeps_Pricing_on_the_bar(bool plans, bool events)
    {
        var features = await FeaturesAsync(plans, events);

        Assert.Matches(BarPricing, await TopBarAsync(features, signedIn: false));
        Assert.Matches(BarPricing, await TopBarAsync(features, signedIn: true));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task With_plans_and_event_credits_both_off_no_menu_links_to_Pricing(bool signedIn)
    {
        var features = await FeaturesAsync(plans: false, events: false);

        Assert.Equal(0, LinksTo(await TopBarAsync(features, signedIn), "/pricing"));
        Assert.Equal(0, LinksTo(await DrawerAsync(features, signedIn), "/pricing"));
    }

    /// <summary>
    /// One place, not two: the same address under Community too would light both entries as the
    /// current page, and a reader would still go looking for it in Community.
    /// </summary>
    [Fact]
    public async Task Signed_in_the_drawer_lists_Pricing_once_and_not_under_Community()
    {
        var html = await DrawerAsync(await FeaturesAsync(plans: true, events: true), signedIn: true);

        Assert.Equal(1, LinksTo(html, "/pricing"));
        var community = html.IndexOf("<span class=\"nav-link-text\">Community</span>", StringComparison.Ordinal);
        var pricing = html.IndexOf("href=\"/pricing\"", StringComparison.Ordinal);
        var communityEnd = html.IndexOf("</ul>", community, StringComparison.Ordinal);
        Assert.True(community >= 0, "Community is in the drawer.");
        Assert.False(pricing > community && pricing < communityEnd, "Pricing is not inside Community any more.");
    }

    [Fact]
    public async Task Signed_out_the_drawer_lists_Pricing()
    {
        var html = await DrawerAsync(await FeaturesAsync(plans: true, events: true), signedIn: false);

        Assert.Equal(1, LinksTo(html, "/pricing"));
    }

    // ── Administration ─────────────────────────────────────────────────────

    [Fact]
    public async Task Administration_has_a_Pricing_entry_for_a_SuperAdmin_only()
    {
        var features = await FeaturesAsync(plans: true, events: true);

        var admin = await DrawerAsync(features, signedIn: true, superAdmin: true);
        Assert.Matches(new Regex(@"href=""/admin/pricing""[^>]*>.*?Pricing", RegexOptions.Singleline), admin);

        var member = await DrawerAsync(features, signedIn: true, superAdmin: false);
        Assert.Equal(0, LinksTo(member, "/admin/pricing"));
    }

    // ── The home page and the footer ───────────────────────────────────────

    [Fact]
    public async Task The_home_page_has_See_pricing_under_the_three_ways_in()
    {
        var html = await RenderStaticAsync<HomePitch>(await FeaturesAsync(plans: true, events: true), signedIn: false);

        var threeWays = html.IndexOf("Three ways in. All of them start free.", StringComparison.Ordinal);
        var button = html.IndexOf("data-testid=\"home-see-pricing\"", StringComparison.Ordinal);
        var next = html.IndexOf("Everything it does", StringComparison.Ordinal);
        Assert.True(threeWays >= 0 && button > threeWays && button < next,
            "See pricing sits under the three ways in, before the next section.");
        Assert.Matches(new Regex(@"href=""/pricing"" data-testid=""home-see-pricing""[^>]*>.*?See pricing", RegexOptions.Singleline), html);
    }

    [Fact]
    public async Task The_footer_links_to_Pricing()
    {
        var html = await RenderStaticAsync<BenFooter>(await FeaturesAsync(plans: false, events: true), signedIn: false);

        Assert.Contains("href=\"/pricing\" class=\"app-footer-link ms-3\" data-testid=\"footer-pricing\">Pricing</a>", html);
    }

    [Fact]
    public async Task With_nothing_on_sale_the_home_page_and_the_footer_do_not_link_to_Pricing()
    {
        var features = await FeaturesAsync(plans: false, events: false);

        Assert.DoesNotContain("home-see-pricing", await RenderStaticAsync<HomePitch>(features, signedIn: false));
        Assert.DoesNotContain("footer-pricing", await RenderStaticAsync<BenFooter>(features, signedIn: false));
    }

    // ── The features endpoint carries the second switch ─────────────────────

    [Fact]
    public async Task The_provider_reads_the_event_credit_switch_and_shows_Pricing_while_either_is_on()
    {
        Assert.True((await FeaturesAsync(true, true)).PricingIsShown);
        Assert.True((await FeaturesAsync(false, true)).PricingIsShown);
        Assert.True((await FeaturesAsync(true, false)).PricingIsShown);

        var off = await FeaturesAsync(false, false);
        Assert.False(off.EventCreditsEnabled);
        Assert.False(off.PricingIsShown);
    }

    // ── /admin/pricing ─────────────────────────────────────────────────────

    private sealed record AdminPage(InteractiveTestRenderer Renderer, int Id, FakeNav Nav, Mock<IBenAdminClient> Client)
        : IAsyncDisposable
    {
        public string Html => Renderer.Html(Id);
#pragma warning disable BL0006 // disposing the test renderer, which is a Renderer
        public async ValueTask DisposeAsync() => await Renderer.DisposeAsync();
#pragma warning restore BL0006
    }

    private static SiteSettingRecord Setting(string key, string label, string? value = null, bool boolean = false,
                                             bool defaultOn = false, bool multiLine = false)
        => new(key, label, value, $"What {label} does.", new DateTime(2026, 10, 22, 0, 0, 0, DateTimeKind.Utc),
               multiLine, boolean, defaultOn, "Events", "");

    private static IReadOnlyList<SiteSettingRecord> PriceSettings(string? price = null) =>
    [
        Setting(SiteSettingKeys.PlanPurchasesEnabled, "Sell plans and seats", boolean: true, defaultOn: true),
        Setting(SiteSettingKeys.EventCreditsEnabled, "Sell event credits", boolean: true, defaultOn: true),
        Setting(SiteSettingKeys.EventCreditPriceUsd, "Event credit price ($)", price),
        Setting(SiteSettingKeys.EventCreditSubscriberPercent, "Paid-subscriber price (% of the full price)"),
        Setting(SiteSettingKeys.EventCreditBundles, "Event credit bundles", "3 = 249", multiLine: true),
        Setting(SiteSettingKeys.EventRunMonthPercent, "Extra month for a show's run (% of a credit)"),
        Setting(SiteSettingKeys.EventRunMonthsPerShow, "Most extra months for one show"),
        Setting(SiteSettingKeys.GhostWalkEnabled, "Sell the Ghost Walk Tour subscription", "true", boolean: true),
        Setting(SiteSettingKeys.GhostWalkMonthlyPriceUsd, "Ghost Walk Tour price ($ a month per tour route)"),
    ];

    private static SubscriptionTierAdminRecord Tier(string name, int min, int? max, decimal monthly, decimal yearly, bool active = true)
        => new(Guid.NewGuid(), name, min, max, min, active,
               [new(Ben.Data.Common.Enums.BillingInterval.Monthly, monthly, true, null),
                new(Ben.Data.Common.Enums.BillingInterval.Yearly, yearly, true, 16)],
               [], 3);

    private static PublicPricingExtras Extras(decimal unit = 99m) => new(
        500,
        new PublicEventOffer(true, unit, 365, 20, 48, 2000, 90, 20m, Math.Round(unit * .2m, 2),
            [new EventCreditBundleOffer(3, 249m, 249m, 49.80m)]),
        new PublicGhostWalkOffer(29m));

    private static async Task<AdminPage> OpenAdminPricingAsync(bool superAdmin, bool signedIn = true, bool impersonating = false,
        LoadResult<SiteSettingRecord>? settings = null)
    {
        var userState = new Mock<IBenUserState>();
        userState.SetupGet(u => u.IsAuthenticated).Returns(signedIn);
        userState.SetupGet(u => u.IsSuperAdmin).Returns(superAdmin);
        userState.SetupGet(u => u.IsImpersonating).Returns(impersonating);
        userState.SetupGet(u => u.AuthReady).Returns(Task.CompletedTask);

        var client = new Mock<IBenAdminClient>();
        client.Setup(c => c.GetSiteSettingsAsync(It.IsAny<CancellationToken>()))
              .ReturnsAsync(settings ?? LoadResult<SiteSettingRecord>.Ok(PriceSettings()));
        client.Setup(c => c.GetSubscriptionTiersAsync(It.IsAny<CancellationToken>()))
              .ReturnsAsync(LoadResult<SubscriptionTierAdminRecord>.Ok(
              [
                  Tier("Starter", 1, 5, 19.99m, 199.99m),
                  Tier("Crew", 6, null, 99.99m, 999.99m),
                  Tier("Retired", 1, 3, 9.99m, 99.99m, active: false),
              ]));
        client.Setup(c => c.GetPublicPricingExtrasAsync(It.IsAny<CancellationToken>())).ReturnsAsync(Extras());
        client.Setup(c => c.GetSiteFeaturesAsync(It.IsAny<CancellationToken>()))
              .ReturnsAsync(new SiteFeaturesInfo(new Dictionary<string, bool>(SiteFeaturesProvider.Defaults)));

        var services = new ServiceCollection();
        var nav = new FakeNav("admin/pricing");
        services.AddSingleton<NavigationManager>(nav);
        services.AddSingleton(userState.Object);
        services.AddScoped(_ => client.Object);
        services.AddSingleton(new Mock<IJSRuntime>().Object);
        services.AddSingleton<SiteFeaturesProvider>();
        services.AddLogging();

        var renderer = new InteractiveTestRenderer(services.BuildServiceProvider());
        var id = await renderer.RenderAsync<AdminPricing>();
        return new AdminPage(renderer, id, nav, client);
    }

    [Theory]
    [InlineData(true, false)]    // a signed-in member
    [InlineData(false, false)]   // nobody signed in
    [InlineData(true, true)]     // a SuperAdmin standing in for somebody else
    public async Task Anybody_but_a_SuperAdmin_is_refused_before_anything_is_asked(bool signedIn, bool impersonating)
    {
        await using var page = await OpenAdminPricingAsync(superAdmin: impersonating, signedIn: signedIn, impersonating: impersonating);

        Assert.NotNull(page.Nav.WentTo);
        Assert.Equal(signedIn ? "/" : "/login?returnUrl=%2Fadmin%2Fpricing", page.Nav.WentTo);
        page.Client.Verify(c => c.GetSiteSettingsAsync(It.IsAny<CancellationToken>()), Times.Never);
        page.Client.Verify(c => c.GetSubscriptionTiersAsync(It.IsAny<CancellationToken>()), Times.Never);
        Assert.DoesNotContain("admin-pricing-events", page.Html);
    }

    [Fact]
    public async Task A_SuperAdmin_sees_plans_events_and_the_Ghost_Walk_Tour_with_a_sentence_each()
    {
        await using var page = await OpenAdminPricingAsync(superAdmin: true);
        var html = Regex.Replace(page.Html, @"\s+", " ");

        Assert.Null(page.Nav.WentTo);
        Assert.Contains("Groups can buy 2 plans, from $19.99 to $99.99 a month.", html);
        Assert.Contains("A group with a paid plan pays $19.80 per event (20% of the full price).", html);
        Assert.Contains("Bundles: 3 for $249.00 ($49.80 with a paid plan).", html);
        Assert.Contains("A tour group pays $29.00 a month for each tour route", html);

        // The bands, live ones only, and the way to the editor and to what visitors see.
        Assert.Contains("Starter", html);
        Assert.DoesNotContain(">Retired<", html);
        Assert.Contains("href=\"/admin/subscription-tiers\"", html);
        Assert.Contains("href=\"/pricing\"", html);
        Assert.Contains("See what visitors see", html);

        // Every price setting is editable here.
        foreach (var key in AdminPricing.Keys)
            Assert.Contains($"data-key=\"{key}\"", html);
    }

    [Fact]
    public async Task A_refused_price_shows_the_servers_sentence()
    {
        const string refusal = "Write the event credit price as a number above zero, without the dollar sign — for example 99.";
        await using var page = await OpenAdminPricingAsync(superAdmin: true);
        page.Client.Setup(c => c.SetSiteSettingWithReasonAsync(SiteSettingKeys.EventCreditPriceUsd,
                It.IsAny<SetSiteSettingRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(((SiteSettingRecord?)null, refusal));

        var within = ("data-key", SiteSettingKeys.EventCreditPriceUsd);
        await page.Renderer.FireAsync(page.Id, "oninput", e => e.Name == "input",
            new ChangeEventArgs { Value = "ninety-nine" }, within.Item1, within.Item2);
        await page.Renderer.FireAsync(page.Id, "onclick", e => e.Name == "button" && e.Text == "Save",
            EventArgs.Empty, within.Item1, within.Item2);

        Assert.Contains(refusal, page.Html);
        page.Client.Verify(c => c.SetSiteSettingWithReasonAsync(SiteSettingKeys.EventCreditPriceUsd,
            It.Is<SetSiteSettingRequest>(r => r.Value == "ninety-nine"), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task A_saved_price_refreshes_the_sentence_from_what_the_site_now_charges()
    {
        await using var page = await OpenAdminPricingAsync(superAdmin: true);
        page.Client.Setup(c => c.SetSiteSettingWithReasonAsync(SiteSettingKeys.EventCreditPriceUsd,
                It.IsAny<SetSiteSettingRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Setting(SiteSettingKeys.EventCreditPriceUsd, "Event credit price ($)", "120"), (string?)null));
        page.Client.Setup(c => c.GetPublicPricingExtrasAsync(It.IsAny<CancellationToken>())).ReturnsAsync(Extras(120m));

        await page.Renderer.FireAsync(page.Id, "oninput", e => e.Name == "input",
            new ChangeEventArgs { Value = "120" }, "data-key", SiteSettingKeys.EventCreditPriceUsd);
        await page.Renderer.FireAsync(page.Id, "onclick", e => e.Name == "button" && e.Text == "Save",
            EventArgs.Empty, "data-key", SiteSettingKeys.EventCreditPriceUsd);

        var html = Regex.Replace(page.Html, @"\s+", " ");
        Assert.Contains("Saved.", html);
        Assert.Contains("One event costs $120.00. A group with a paid plan pays $24.00 per event", html);
    }

    [Fact]
    public async Task Settings_that_could_not_load_say_why_instead_of_drawing_empty_cards()
    {
        await using var page = await OpenAdminPricingAsync(superAdmin: true,
            settings: LoadResult<SiteSettingRecord>.Failure("The server refused: you are not allowed to read site settings."));

        Assert.Contains("The server refused: you are not allowed to read site settings.", page.Html);
        Assert.DoesNotContain("admin-pricing-events", page.Html);
    }

    /// <summary>The page names the API's keys in its own constants; a renamed key must fail here.</summary>
    [Fact]
    public void The_pages_keys_are_the_APIs_price_settings()
    {
        Assert.Equal(SiteSettingKeys.PlanPurchasesEnabled, AdminPricing.PlanPurchasesKey);
        Assert.Equal(SiteSettingKeys.EventCreditsEnabled, AdminPricing.EventCreditsKey);
        Assert.Equal(SiteSettingKeys.EventCreditPriceUsd, AdminPricing.EventCreditPriceKey);
        Assert.Equal(SiteSettingKeys.EventCreditSubscriberPercent, AdminPricing.EventCreditPercentKey);
        Assert.Equal(SiteSettingKeys.EventCreditBundles, AdminPricing.EventCreditBundlesKey);
        Assert.Equal(SiteSettingKeys.EventRunMonthPercent, AdminPricing.RunMonthPercentKey);
        Assert.Equal(SiteSettingKeys.EventRunMonthsPerShow, AdminPricing.RunMonthsPerShowKey);
        Assert.Equal(SiteSettingKeys.GhostWalkEnabled, AdminPricing.GhostWalkKey);
        Assert.Equal(SiteSettingKeys.GhostWalkMonthlyPriceUsd, AdminPricing.GhostWalkPriceKey);

        // Everything in Site Settings' Events and Ghost Walk Tour tabs is on this page too.
        foreach (var name in new[] { "Events", "Ghost Walk Tour subscription", "Selling plans" })
            foreach (var key in SiteSettingKeys.Groups.Single(g => g.Name == name).Keys)
                Assert.Contains(key, AdminPricing.Keys);
    }

    [Fact]
    public void Site_Settings_says_the_price_tabs_are_also_on_Pricing()
    {
        Assert.True(AdminSiteSettings.IsAlsoOnPricing("Events"));
        Assert.True(AdminSiteSettings.IsAlsoOnPricing("Ghost Walk Tour subscription"));
        Assert.True(AdminSiteSettings.IsAlsoOnPricing("Selling plans"));
        Assert.False(AdminSiteSettings.IsAlsoOnPricing("Limits"));

        // Named the way the API names the sections, or the line would never show.
        foreach (var name in new[] { "Events", "Ghost Walk Tour subscription", "Selling plans" })
            Assert.Contains(SiteSettingKeys.Groups, g => g.Name == name);
    }

    // ── The sentences ──────────────────────────────────────────────────────

    [Fact]
    public void The_sentences_say_what_happens_when_things_are_off()
    {
        Assert.Equal("Event credits aren't on sale. Credits people already have still work.",
            PricingPreview.Events(Extras().Events with { OnSale = false }));
        Assert.Equal("It's off, so tour groups aren't offered it and the site doesn't mention it.",
            PricingPreview.GhostWalk(null, switchedOn: false));
        Assert.StartsWith("Plans aren't on sale.", PricingPreview.Plans([Tier("Starter", 1, 5, 19.99m, 199.99m)], purchasesOn: false));
        Assert.StartsWith("Nothing is on sale", PricingPreview.Menus(false, false));
    }
}
