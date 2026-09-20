using System.Text;
using System.Text.Json;
using Microsoft.Playwright;
using NUnit.Framework;

namespace Ben.Web.Playwright.Capture;

/// <summary>
/// Every screen, as every kind of person, watching the console and the network (2026-09-20).
/// </summary>
/// <remarks>
/// <para><b>What this covers that nothing else does.</b> The two route crawls open every route,
/// which is real coverage — but both sign in as the <b>SuperAdmin</b> and both look only at what
/// the page drew. So two whole classes of fault have never had anywhere to show up:</para>
///
/// <list type="bullet">
///   <item>Anything that is only wrong for somebody else. An ordinary member, a viewer, a client,
///   a person with no account at all. The site's own memory already records three total failures
///   that were invisible from the admin seat.</item>
///   <item>Anything that goes wrong <b>behind</b> a page that looks fine. A component that throws
///   in the browser console, a fetch that comes back 500 and is swallowed, an asset that 404s. A
///   page can render perfectly and be broken underneath, and no test in this repository has ever
///   looked.</item>
/// </list>
///
/// <para><b>It reports; it does not fail.</b> Some of what it finds is correct behaviour — a
/// client opening a group's admin screen SHOULD be refused — and a sweep that failed on those
/// would be turned off within a week. It writes a report to read, and the findings worth holding
/// forever become ordinary tests with names.</para>
///
/// <para><b>Opt in:</b> <c>BEN_SWEEP=1</c>, output under <c>BEN_SWEEP_OUT</c>. It signs in and out
/// repeatedly and visits several hundred URLs, so it is slow on purpose and not part of a run
/// somebody is waiting on.</para>
/// </remarks>
[TestFixture]
[Category("Capture")]
[NonParallelizable]
public sealed class SiteSweep : BenTestBase
{
    private static string OutRoot => Environment.GetEnvironmentVariable("BEN_SWEEP_OUT") ?? "site-sweep";

    /// <summary>Who walks the site. The seat is the most load-bearing decision a check makes.</summary>
    private sealed record Seat(string Who, string? Email, string? Password);

    private static IEnumerable<Seat> Seats() =>
    [
        new("a visitor with no account", null, null),
        new("a client",      ClientEmail,   ClientPassword),
        new("a viewer",      ViewerEmail,   ViewerPassword),
        new("an ordinary member", MemberEmail, MemberPassword),
        new("a group's owner",    UserEmail,   UserPassword),
        new("the site admin", SuperAdminEmail, SuperAdminPassword),
    ];

    /// <summary>What went wrong on one screen for one person.</summary>
    private sealed record Finding(string Who, string Url, string What, string Detail);

    private readonly List<Finding> _found = [];
    private readonly List<string> _consoleErrors = [];
    private readonly List<string> _failedCalls = [];

    [OneTimeSetUp]
    public void SkipUnlessAsked()
    {
        if (Environment.GetEnvironmentVariable("BEN_SWEEP") != "1")
            Assert.Ignore("Set BEN_SWEEP=1 to sweep the whole site as every seat. It is slow.");
    }

    /// <summary>
    /// Console noise that is not this site's fault, and would drown everything that is.
    /// </summary>
    /// <remarks>
    /// Deliberately short. Every entry here is a line nobody will ever read again, so a pattern
    /// earns its place by being demonstrably not ours — a browser's own deprecation warning, a
    /// third-party map library — never by being merely frequent.
    /// </remarks>
    private static readonly string[] NotOurs =
    [
        "Failed to load resource: net::ERR_INTERNET_DISCONNECTED",
        "mapkit",           // Apple's library logs its own configuration state
        "Download the React DevTools",
    ];

    private static bool Ignorable(string text)
        => NotOurs.Any(n => text.Contains(n, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// A response that is a problem rather than a rule being kept.
    /// </summary>
    /// <remarks>
    /// <b>401 and 403 are not failures here.</b> A viewer opening a screen that is not theirs
    /// SHOULD be refused, and the site's rule is that the refusal is shown in words — which is the
    /// page's job, checked by the content read, not this one's. What is always wrong is a 5xx, and
    /// a 404 on something the page asked for itself.
    /// </remarks>
    private static bool WorthReporting(int status)
        => status >= 500 || status == 404;

    private void Watch()
    {
        Page.Console += (_, msg) =>
        {
            if (msg.Type == "error" && !Ignorable(msg.Text))
                _consoleErrors.Add(msg.Text.Split('\n')[0].Trim());
        };

        Page.Response += (_, res) =>
        {
            if (WorthReporting(res.Status) && !Ignorable(res.Url))
                _failedCalls.Add($"{res.Status} {Shorten(res.Url)}");
        };

        Page.PageError += (_, error) =>
            _consoleErrors.Add("uncaught: " + error.Split('\n')[0].Trim());
    }

    private static string Shorten(string url)
    {
        var trimmed = url.Replace(BaseUrl, "").Replace(ApiUrl, "api:");
        return trimmed.Length > 120 ? trimmed[..120] + "…" : trimmed;
    }

    /// <summary>Opens one URL as whoever is currently signed in, and writes down what happened.</summary>
    private async Task VisitAsync(string who, string url)
    {
        _consoleErrors.Clear();
        _failedCalls.Clear();

        try
        {
            await Page.GotoAsync($"{BaseUrl}{url}", new() { Timeout = 25_000 });
            await WaitForTheCircuitAsync();
            await SettleAsync();
        }
        catch (Exception ex)
        {
            _found.Add(new(who, url, "would not open", ex.Message.Split('\n')[0].Trim()));
            return;
        }

        try
        {
            var state = await Page.EvaluateAsync<JsonElement>(@"() => {
                const main = document.querySelector('.app-content, main, .content-wrapper');
                const body = document.body.innerText || '';
                const inner = main ? (main.innerText || '') : '';
                const chrome = inner ? body.split(inner).join(' ') : body;
                const err = document.querySelector('#blazor-error-ui');
                return {
                    content: main ? inner.trim().length : 0,
                    unhandled: /An unhandled error has occurred/i.test(chrome),
                    circuitDown: !!err && getComputedStyle(err).display !== 'none',
                    wide: document.documentElement.scrollWidth > window.innerWidth + 2
                };
            }");

            if (state.GetProperty("unhandled").GetBoolean())
                _found.Add(new(who, url, "unhandled error banner", ""));
            else if (state.GetProperty("circuitDown").GetBoolean())
                _found.Add(new(who, url, "the circuit dropped", ""));
            else if (state.GetProperty("content").GetInt32() < 40)
                _found.Add(new(who, url, "drew almost nothing",
                    $"{state.GetProperty("content").GetInt32()} characters"));

            if (state.GetProperty("wide").GetBoolean())
                _found.Add(new(who, url, "scrolls sideways", "the page is wider than the window"));
        }
        catch (Exception ex)
        {
            _found.Add(new(who, url, "could not be read", ex.Message.Split('\n')[0].Trim()));
        }

        // The two nobody has ever looked at.
        foreach (var e in _consoleErrors.Distinct().Take(4))
            _found.Add(new(who, url, "console error", e));
        foreach (var f in _failedCalls.Distinct().Take(4))
            _found.Add(new(who, url, "request failed", f));
    }

    /// <summary>Waits for the page to stop saying it is loading — spinner as well as the word.</summary>
    private async Task<string?> SettleAsync(int timeoutMs = 20_000)
    {
        var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
        while (DateTime.UtcNow < deadline)
        {
            try
            {
                if (await Main.GetByText("Loading", new() { Exact = false }).CountAsync() == 0
                 && await Spinners.CountAsync() == 0) return null;
            }
            catch (Exception) { return null; }
            await Task.Delay(200);
        }
        return "still loading";
    }

    [Test]
    [Description("Every screen, as every kind of person, watching the console and the network.")]
    public async Task Every_screen_as_everybody()
    {
        Watch();

        // Ids come from the admin seat once, so the URLs are the same for everybody — the point is
        // what each person is shown at the SAME address, not a different tour per seat.
        var urls = await UrlsAsync();
        TestContext.Out.WriteLine($"{urls.Count} urls");

        foreach (var seat in Seats())
        {
            if (seat.Email is null) await LogoutAsync();
            else
            {
                await LogoutAsync();
                await LoginAsync(seat.Email, seat.Password!);
            }

            foreach (var url in urls) await VisitAsync(seat.Who, url);
            TestContext.Out.WriteLine($"{seat.Who}: {_found.Count(f => f.Who == seat.Who)} finding(s)");
        }

        // The phone, once, as a visitor — every public screen, because that is where most people
        // meet this site and no fixture has ever opened them at that width.
        await LogoutAsync();
        await Page.SetViewportSizeAsync(375, 812);
        foreach (var url in urls.Where(IsPublic))
            await VisitAsync("a visitor on a phone", url);
        await Page.SetViewportSizeAsync(1280, 720);

        Write();
    }

    private static bool IsPublic(string url)
        => url is "/" or "/pricing" or "/events" or "/find" or "/feed" or "/signup" or "/login"
        || url.StartsWith("/o/", StringComparison.Ordinal)
        || url.StartsWith("/places", StringComparison.Ordinal)
        || url.StartsWith("/help", StringComparison.Ordinal);

    /// <summary>Every plain route, plus every parameterised route that a real id can fill.</summary>
    private async Task<List<string>> UrlsAsync()
    {
        var urls = new List<string>(RouteCrawlHelper.PlainRoutes(["/logout"]));

        // One real group and one real case, resolved the way the parameterised crawl does, so the
        // sweep follows the seed rather than going stale against it.
        try
        {
            var orgId = await OrgIdBySlugAsync("paranormal365");
            var ids = new Dictionary<string, string> { ["OrgId"] = orgId };

            foreach (var route in RouteCrawlHelper.ParameterisedRoutes())
            {
                if (route.Contains("{Token}") || route.Contains("{AccessToken")) continue;
                if (RouteCrawlHelper.Fill(route, ids) is { } filled) urls.Add(filled);
            }
        }
        catch (Exception ex)
        {
            TestContext.Out.WriteLine("no ids: " + ex.Message.Split('\n')[0]);
        }

        return urls;
    }

    private void Write()
    {
        Directory.CreateDirectory(OutRoot);

        var report = new StringBuilder();
        report.AppendLine("# The whole site, as everybody");
        report.AppendLine();
        report.AppendLine($"{_found.Count} finding(s).");
        report.AppendLine();

        foreach (var group in _found.GroupBy(f => f.Who))
        {
            report.AppendLine($"## {group.Key} — {group.Count()}");
            report.AppendLine();
            report.AppendLine("| Screen | What | Detail |");
            report.AppendLine("|---|---|---|");
            foreach (var f in group.OrderBy(f => f.Url))
                report.AppendLine($"| `{f.Url}` | {f.What} | {f.Detail} |");
            report.AppendLine();
        }

        File.WriteAllText(Path.Combine(OutRoot, "report.md"), report.ToString());
        TestContext.Out.WriteLine($"{_found.Count} finding(s) → {Path.Combine(OutRoot, "report.md")}");
    }
}
