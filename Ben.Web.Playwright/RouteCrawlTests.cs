using Microsoft.Playwright;
using NUnit.Framework;
using System.Net.Http.Json;
using System.Text.Json;

namespace Ben.Web.Playwright;

/// <summary>
/// Walks every plain route in the app as a SuperAdmin and reports the ones that do not come up.
/// <para>
/// This is the check a build cannot do. A Blazor page that throws during render, or renders
/// nothing because a guard bailed out, still compiles and still returns 200 — the only way to
/// know is to open it. Six routes were once missing entirely and presented as blank pages, which
/// is what this exists to catch.
/// </para>
/// </summary>
[TestFixture]
[Category("Crawl")]
public class RouteCrawlTests : BenTestBase
{
    /// <summary>Routes that are expected to be unreachable or empty for this account.</summary>
    private static readonly HashSet<string> Excluded = new()
    {
        "/logout",          // ends the session the rest of the crawl needs
        "/not-found",       // reached by the status-code middleware, asserted separately
    };

    /// <summary>
    /// Routes a switched-off feature owns. A dark feature is SUPPOSED to look absent — its
    /// pages render "Page not found" so a visitor cannot tell a switched-off feature from one
    /// that was never built — so walking them while the switch is off reports the feature
    /// working correctly as a broken route. The flags are read live rather than hardcoded,
    /// so the day the public feed launches these routes rejoin the walk on their own.
    /// </summary>
    private static readonly (string Flag, string[] Routes)[] FeatureGatedRoutes =
    [
        ("features.public-feed",  ["/feed"]),
        ("features.publications", ["/publications"]),
        ("features.equipment",    ["/equipment-catalog", "/my-equipment", "/my-checkouts"]),
        ("features.video-editor", ["/video-editor", "/my-videos"]),
        ("features.events",       ["/events"]),
    ];

    private static async Task<HashSet<string>> RoutesBehindOffSwitchesAsync()
    {
        var off = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            using var http = new HttpClient { BaseAddress = new Uri(ApiUrl), Timeout = TimeSpan.FromSeconds(20) };
            var features = await http.GetFromJsonAsync<JsonElement>("/api/public/site-features");
            if (!features.TryGetProperty("features", out var map)) return off;

            foreach (var (flag, routes) in FeatureGatedRoutes)
            {
                if (map.TryGetProperty(flag, out var value)
                    && value.ValueKind == JsonValueKind.False)
                {
                    foreach (var route in routes) off.Add(route);
                }
            }
        }
        catch (HttpRequestException)
        {
            // Cannot ask: walk everything and let a genuine breakage report itself, rather
            // than silently skipping routes because one request failed.
        }
        return off;
    }

    private static List<string> PlainRoutes() => RouteCrawlHelper.PlainRoutes(Excluded);

    [Test]
    public async Task EveryPlainRoute_RendersWithoutErrorOrEmptiness()
    {
        await LoginAsync(SuperAdminEmail, SuperAdminPassword);

        var switchedOff = await RoutesBehindOffSwitchesAsync();
        var broken = new List<string>();
        var routes = PlainRoutes().Where(r => !switchedOff.Contains(r)).ToList();
        Assert.That(routes, Is.Not.Empty, "no routes were discovered — has the layout moved?");

        foreach (var route in routes)
        {
            try
            {
                await Page.GotoAsync($"{BaseUrl}{route}", new() { Timeout = 20_000 });
                await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);
                await WaitUntilLoadedAsync(8_000);

                var state = await Page.EvaluateAsync<JsonElement>(@"() => {
                    const main = document.querySelector('.app-content, main, .content-wrapper');
                    const body = document.body.innerText || '';
                    const err  = document.querySelector('#blazor-error-ui');
                    return {
                        content:  main ? (main.innerText || '').trim().length : 0,
                        unhandled: /An unhandled error has occurred|Sorry, there's nothing at this address/i.test(body),
                        notFound:  /Page not found/i.test(body),
                        circuitDown: !!err && getComputedStyle(err).display !== 'none'
                    };
                }");

                var content = state.GetProperty("content").GetInt32();
                if (state.GetProperty("unhandled").GetBoolean())   broken.Add($"{route} — unhandled error");
                else if (state.GetProperty("notFound").GetBoolean()) broken.Add($"{route} — not routed");
                else if (state.GetProperty("circuitDown").GetBoolean()) broken.Add($"{route} — circuit dropped");
                else if (content < 40) broken.Add($"{route} — rendered {content} chars of content");
            }
            catch (Exception ex)
            {
                broken.Add($"{route} — {ex.GetType().Name}: {ex.Message.Split('\n')[0]}");
            }
        }

        TestContext.Out.WriteLine($"crawled {routes.Count} routes, {broken.Count} problem(s)");
        foreach (var b in broken) TestContext.Out.WriteLine("   " + b);

        Assert.That(broken, Is.Empty,
            $"{broken.Count} of {routes.Count} routes did not come up:\n  " + string.Join("\n  ", broken));
    }
}
