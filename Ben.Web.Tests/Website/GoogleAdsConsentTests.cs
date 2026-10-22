using Ben.Data.Common;
using Ben.Web.Services;
using Ben.Web.Website.Components.Layout;
using Jint;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Ben.Web.Tests.Website;

/// <summary>
/// Google Ads measurement (10/19/2026): no tag, banner or footer link without a configured tag
/// id; nothing fetched from Google before the visitor accepts; the conversion only with consent;
/// and /subscribed only ever forwards to the visitor's own group.
/// </summary>
public sealed class GoogleAdsConsentTests
{
    private const string Tag = "AW-123456789";

    private static IConfiguration Config(string? tagId, string? label = null) =>
        new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["GoogleAds:TagId"] = tagId,
            ["GoogleAds:SubscribeConversionLabel"] = label,
        }).Build();

    private static async Task<string> RenderAsync<T>(IConfiguration config) where T : IComponent
    {
        var services = new ServiceCollection();
        services.AddSingleton(config);
        services.AddSingleton(Microsoft.Extensions.Options.Options.Create(new SiteIdentity()));
        // The footer asks whether to link to Pricing (10/22/2026); unprimed, it reads the defaults.
        services.AddSingleton<Ben.Web.Services.SiteFeaturesProvider>();
        services.AddScoped(_ => new Moq.Mock<Ben.Web.Services.IBenAdminClient>().Object);
        services.AddLogging();
        await using var provider = services.BuildServiceProvider();
        await using var renderer = new HtmlRenderer(provider, NullLoggerFactory.Instance);
        return await renderer.Dispatcher.InvokeAsync(async () =>
            (await renderer.RenderComponentAsync<T>(ParameterView.Empty)).ToHtmlString());
    }

    // ── settings ─────────────────────────────────────────────────────────────

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void An_empty_tag_id_turns_everything_off(string? tagId)
    {
        var ads = GoogleAdsSettings.From(Config(tagId, "label"));
        Assert.False(ads.Enabled);
        Assert.Null(ads.SubscribeConversionSendTo);
    }

    [Fact]
    public void The_conversion_goes_to_tag_slash_label_and_is_skipped_without_a_label()
    {
        Assert.Equal($"{Tag}/abcDEF", GoogleAdsSettings.From(Config(Tag, "abcDEF")).SubscribeConversionSendTo);
        Assert.Null(GoogleAdsSettings.From(Config(Tag, "")).SubscribeConversionSendTo);
    }

    // ── what the page renders ────────────────────────────────────────────────

    [Fact]
    public async Task No_tag_id_renders_no_tag_and_no_banner()
    {
        var html = await RenderAsync<AdsConsent>(Config(""));
        Assert.DoesNotContain("ads-consent", html);
        Assert.DoesNotContain("google", html, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task No_tag_id_puts_no_cookie_settings_in_the_footer()
    {
        Assert.DoesNotContain("Cookie settings", await RenderAsync<BenFooter>(Config("")));
        Assert.Contains("Cookie settings", await RenderAsync<BenFooter>(Config(Tag)));
    }

    [Fact]
    public async Task With_a_tag_id_the_banner_starts_hidden_and_the_page_itself_loads_nothing_from_google()
    {
        var html = await RenderAsync<AdsConsent>(Config(Tag));
        Assert.Contains("id=\"ads-consent-banner\"", html);
        Assert.Matches("id=\"ads-consent-banner\"[^>]*hidden", html);
        Assert.Contains($"data-tag-id=\"{Tag}\"", html);
        Assert.Contains("src=\"/js/ads-consent.js\"", html);
        Assert.DoesNotContain("googletagmanager", html);
        // Equal weight: Accept and Decline carry the same classes.
        var classes = System.Text.RegularExpressions.Regex.Matches(html,
            "class=\"([^\"]+)\" data-ads-consent=\"(accept|decline)\"");
        Assert.Equal(2, classes.Count);
        Assert.Equal(classes[0].Groups[1].Value, classes[1].Groups[1].Value);
    }

    // ── ads-consent.js, run for real ─────────────────────────────────────────

    private sealed class Browser
    {
        public readonly Engine Js = new();

        public Browser(string tagId, string? stored = null)
        {
            var script = File.ReadAllText(Path.Combine(RepoRoot(), "Ben.Web.Website", "wwwroot", "js", "ads-consent.js"));
            Js.SetValue("TAG", tagId);
            Js.SetValue("STORED", stored);
            Js.Execute("""
                var appended = [], store = {}, attrs = { hidden: '' }, listeners = {};
                if (STORED !== null && STORED !== undefined) store['ih-ads-consent'] = STORED;
                var banner = {
                    removeAttribute: function (n) { delete attrs[n]; },
                    setAttribute: function (n, v) { attrs[n] = v; }
                };
                var document = {
                    currentScript: { getAttribute: function (n) { return n === 'data-tag-id' ? TAG : null; } },
                    readyState: 'complete',
                    head: { appendChild: function (s) { appended.push(s.src); } },
                    createElement: function () { return {}; },
                    getElementById: function () { return banner; },
                    addEventListener: function (t, f) { listeners[t] = f; }
                };
                var window = { document: document, localStorage: {
                    getItem: function (k) { return k in store ? store[k] : null; },
                    setItem: function (k, v) { store[k] = v; } } };
                function fetchedFromGoogle() { return appended.filter(function (s) { return s.indexOf('googletagmanager') >= 0; }).length; }
                function bannerShown() { return !('hidden' in attrs); }
                function layer() { return JSON.stringify((window.dataLayer || []).map(function (a) { return Array.prototype.slice.call(a); })); }
                """);
            Js.Execute(script);
        }

        public int FetchedFromGoogle => (int)Js.Evaluate("fetchedFromGoogle()").AsNumber();
        public bool BannerShown => Js.Evaluate("bannerShown()").AsBoolean();
        public string DataLayer => Js.Evaluate("layer()").AsString();
        public void Run(string code) => Js.Execute(code);
        public bool Convert(string sendTo) => Js.Invoke(Js.Evaluate("window.benAdsConsent.conversion"), sendTo).AsBoolean();
    }

    [Fact]
    public void Before_any_choice_nothing_is_fetched_consent_is_denied_and_the_banner_shows()
    {
        var b = new Browser(Tag);
        Assert.Equal(0, b.FetchedFromGoogle);
        Assert.True(b.BannerShown);
        Assert.Contains("[\"consent\",\"default\",{\"ad_storage\":\"denied\",\"ad_user_data\":\"denied\",\"ad_personalization\":\"denied\",\"analytics_storage\":\"denied\"}]", b.DataLayer);
        Assert.DoesNotContain("granted", b.DataLayer);
        Assert.False(b.Convert($"{Tag}/label"));
        Assert.DoesNotContain("conversion", b.DataLayer);
    }

    [Fact]
    public void Decline_keeps_google_out_and_hides_the_banner()
    {
        var b = new Browser(Tag);
        b.Run("window.benAdsConsent.decline()");
        Assert.Equal(0, b.FetchedFromGoogle);
        Assert.False(b.BannerShown);
        Assert.False(b.Convert($"{Tag}/label"));
        Assert.Equal("denied", b.Js.Evaluate("store['ih-ads-consent']").AsString());
    }

    [Fact]
    public void Accept_loads_the_tag_once_and_the_conversion_carries_only_send_to()
    {
        var b = new Browser(Tag);
        b.Run("window.benAdsConsent.accept()");
        Assert.Equal(1, b.FetchedFromGoogle);
        Assert.False(b.BannerShown);
        Assert.True(b.Convert($"{Tag}/label"));
        Assert.Equal(1, b.FetchedFromGoogle);
        Assert.Contains($"[\"event\",\"conversion\",{{\"send_to\":\"{Tag}/label\"}}]", b.DataLayer);
    }

    [Fact]
    public void A_remembered_accept_loads_on_the_next_visit_without_asking()
    {
        var b = new Browser(Tag, stored: "granted");
        Assert.Equal(1, b.FetchedFromGoogle);
        Assert.False(b.BannerShown);
    }

    [Fact]
    public void Cookie_settings_reopens_the_banner()
    {
        var b = new Browser(Tag, stored: "denied");
        Assert.False(b.BannerShown);
        b.Run("window.benAdsConsent.open()");
        Assert.True(b.BannerShown);
    }

    [Fact]
    public void Without_a_tag_id_the_script_does_nothing()
    {
        var b = new Browser("", stored: "granted");
        Assert.Equal(0, b.FetchedFromGoogle);
        Assert.True(b.Js.Evaluate("window.benAdsConsent === undefined").AsBoolean());
    }

    // ── /subscribed ──────────────────────────────────────────────────────────

    [Fact]
    public void Subscribed_forwards_to_the_visitors_own_group_billing_with_the_outcome()
    {
        var mine = Guid.NewGuid();
        Assert.Equal($"/organizations/{mine}/billing?checkout=success",
            SubscribedLanding.Destination(mine.ToString(), "paid", [mine]));
        Assert.Equal($"/organizations/{mine}/billing?checkout=free",
            SubscribedLanding.Destination(mine.ToString(), "free", [mine]));
        Assert.Equal($"/organizations/{mine}/billing",
            SubscribedLanding.Destination(mine.ToString(), "weird", [mine]));
    }

    [Theory]
    [InlineData("https://evil.example/")]
    [InlineData("//evil.example")]
    [InlineData("")]
    [InlineData(null)]
    [InlineData("00000000-0000-0000-0000-000000000000")]
    public void Subscribed_refuses_anything_that_is_not_a_group_id(string? org)
    {
        Assert.Equal(SubscribedLanding.Fallback, SubscribedLanding.Destination(org, "paid", [Guid.NewGuid()]));
    }

    [Fact]
    public void Subscribed_refuses_a_group_the_visitor_does_not_belong_to()
    {
        var foreign = Guid.NewGuid();
        Assert.Equal("/organizations", SubscribedLanding.Destination(foreign.ToString(), "paid", [Guid.NewGuid()]));
        Assert.Equal("/organizations", SubscribedLanding.Destination(foreign.ToString(), "paid", []));
    }

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Ben.slnx"))) dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("repo root not found");
    }
}
