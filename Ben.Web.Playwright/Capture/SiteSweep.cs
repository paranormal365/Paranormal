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

    /// <summary>
    /// How many screens were actually opened.
    /// </summary>
    /// <remarks>
    /// Because "0 findings" and "visited nothing" produce the same report, and this repository has
    /// a long history of the second being read as the first. The report leads with this number and
    /// the run fails outright when it is zero.
    /// </remarks>
    private int _visits;

    /// <summary>How many addresses it set out to open, written to the report before it starts.</summary>
    private int _urlCount;
    private readonly List<string> _consoleErrors = [];
    private readonly List<string> _failedCalls = [];

    [OneTimeSetUp]
    public void SkipUnlessAsked()
    {
        if (Environment.GetEnvironmentVariable("BEN_SWEEP") != "1")
            Assert.Ignore("Set BEN_SWEEP=1 to sweep the whole site as every seat. It is slow.");

        var script = Path.Combine(TestContext.CurrentContext.TestDirectory, "Capture", "visual-audit.js");
        Assert.That(File.Exists(script), Is.True, $"the visual auditor is missing: {script}");
        _auditor = File.ReadAllText(script);
    }

    // ── The visual audit, as every seat (Ben, 2026-09-21: "audit the web app as every type of user") ──

    /// <summary>The same auditor <see cref="VisualAuditWalk"/> runs, read once.</summary>
    /// <remarks>
    /// <para>Run here rather than only there because THIS is the instrument that opens every route
    /// as every seat. The audit's own fixture walks a hand-kept list of forty routes as three seats;
    /// the sweep resolves ids and opens ~750 screens as six. The shapes the auditor knows —
    /// contrast, clipped content, text on a card's edge, broken images — are exactly the shapes
    /// that differ by seat, because a member's page has controls a visitor's does not.</para>
    ///
    /// <para><b>It reports, capped, and hard-fails on two.</b> Contrast is a judgement and a badge
    /// at 3.35:1 may be deliberate; those go in the table, four per kind per screen so one bad
    /// stylesheet does not bury everything else. A card's text on its own border and a box hiding
    /// the content somebody asked for are never a choice, and those fail the run — the same rule
    /// the standalone audit applies.</para>
    /// </remarks>
    private string _auditor = "";

    /// <summary>The two that are never a design choice.</summary>
    private readonly List<string> _hard = [];

    private sealed record VisualFinding(string Kind, string El, string Detail);

    private async Task AuditVisualsAsync(string who, string url)
    {
        List<VisualFinding> found;
        try
        {
            var json = await Page.EvaluateAsync<string>(
                _auditor + "\n JSON.stringify(window.__benVisualAudit())");
            found = JsonSerializer.Deserialize<List<VisualFinding>>(json,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? [];
        }
        catch (Exception ex)
        {
            _found.Add(new(who, url, "visual audit could not run", ex.Message.Split('\n')[0].Trim()));
            return;
        }

        // "page scrolls sideways" is already the sweep's own finding, measured the same way; two
        // rows for one fact is how a report gets skimmed.
        foreach (var group in found.Where(f => f.Kind != "page scrolls sideways").GroupBy(f => f.Kind))
        {
            foreach (var f in group.Take(4))
                _found.Add(new(who, url, "visual: " + f.Kind, $"`{f.El}` — {f.Detail}"));
            if (group.Count() > 4)
                _found.Add(new(who, url, "visual: " + group.Key, $"…and {group.Count() - 4} more"));

            if (group.Key is "text on the card edge" or "content clipped")
                foreach (var f in group)
                    _hard.Add($"{who} {url}: {f.Kind} — {f.El} — {f.Detail}");
        }

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

        _visits++;

        try
        {
            var response = await Page.GotoAsync($"{BaseUrl}{url}", new() { Timeout = 25_000 });
            await WaitForTheCircuitAsync();

            // The result was computed and thrown away in the first version of this, so a page that
            // sat on a spinner for twenty seconds produced no finding at all — the same shape as
            // the wait that only looked for the word "Loading". A discarded answer is not a check.
            if (await SettleAsync() is string stuck)
                _found.Add(new(who, url, "never finished loading", stuck));

            if (response is not null && response.Status >= 400)
                _found.Add(new(who, url, "the page itself answered", response.Status.ToString()));
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

            // Only a page that opened and drew something is worth auditing; a blank or broken one
            // has already been reported as such, and its "contrast" would be noise.
            if (!state.GetProperty("unhandled").GetBoolean()
                && !state.GetProperty("circuitDown").GetBoolean()
                && state.GetProperty("content").GetInt32() >= 40)
            {
                await AuditVisualsAsync(who, url);
            }
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

    /// <summary>
    /// Waits for the page to stop being busy, read from the markup rather than the prose.
    /// </summary>
    /// <remarks>
    /// <para><b>Never by searching for the word.</b> The first version looked for "Loading"
    /// anywhere in the page and duly reported <c>/changes</c> as hung on every one of six seats:
    /// the changelog renders the line "…no longer says there are no accounts while it is still
    /// loading", and a page is allowed to talk about loading without doing it. That is the third
    /// time a check in this repository has read a page's own words as evidence about the page —
    /// twice before for "An unhandled error has occurred" — so this one reads structure instead.
    /// </para>
    ///
    /// <para><c>BenLoaderOverlay</c> is the site's one loading marker and always renders a
    /// <c>.spinner-border</c> inside a <c>role="status"</c>, so a spinner is the signal and text
    /// never is.</para>
    /// </remarks>
    private async Task<string?> SettleAsync(int timeoutMs = 20_000)
    {
        var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
        while (DateTime.UtcNow < deadline)
        {
            try
            {
                if (await Spinners.CountAsync() == 0) return null;
            }
            catch (Exception) { return null; }
            await Task.Delay(200);
        }
        return $"still loading after {timeoutMs / 1000}s";
    }

    /// <summary>
    /// Proves the sweep can see before believing that it saw nothing.
    /// </summary>
    /// <remarks>
    /// <para>The first two runs of this fixture opened 644 screens as six different people and
    /// reported a perfectly clean site. That is not a result, it is the shape of a check that
    /// cannot fail — and this repository has produced that shape often enough to have a test named
    /// after it. A sweep whose console and network watchers are silently attached to nothing
    /// reports exactly what a flawless site reports.</para>
    ///
    /// <para>So: make the site emit one console error and one failed request on purpose, and
    /// refuse to run if the watchers did not notice. A clean report is then worth something,
    /// because the instrument was shown working immediately before it produced one.</para>
    /// </remarks>
    private async Task ProveItCanSeeAsync()
    {
        await Page.GotoAsync($"{BaseUrl}/");
        await WaitForTheCircuitAsync();

        _consoleErrors.Clear();
        _failedCalls.Clear();

        await Page.EvaluateAsync("() => console.error('sweep self-test')");
        await Page.EvaluateAsync(
            "async () => { try { await fetch('/__sweep-self-test__.json'); } catch (e) { } }");

        // The events are raised on the browser's own schedule, so give them a moment rather than
        // reading an empty list and concluding the instrument is broken.
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (DateTime.UtcNow < deadline && (_consoleErrors.Count == 0 || _failedCalls.Count == 0))
            await Task.Delay(200);

        Assert.That(_consoleErrors, Is.Not.Empty,
            "the console watcher saw nothing when the page was made to log an error — every "
          + "'no console errors' this fixture reports would be meaningless.");
        Assert.That(_failedCalls, Is.Not.Empty,
            "the network watcher saw nothing when the page was made to request a missing file — "
          + "every 'no failed requests' this fixture reports would be meaningless.");

        _consoleErrors.Clear();
        _failedCalls.Clear();
    }

    [Test]
    [Description("Every screen, as every kind of person, watching the console and the network.")]
    public async Task Every_screen_as_everybody()
    {
        Watch();
        await ProveItCanSeeAsync();

        // Ids come from the admin seat once, so the URLs are the same for everybody — the point is
        // what each person is shown at the SAME address, not a different tour per seat.
        var urls = await UrlsAsync();
        _urlCount = urls.Count;
        Write();   // the header, before a single page is opened, so "what did it look at" is never a guess
        Assert.That(urls, Is.Not.Empty, "no routes were discovered — the sweep would report a clean site having looked at nothing");

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

            // Written after every seat rather than at the end. A sweep that takes half an hour and
            // shows nothing until it finishes is a sweep nobody runs twice — and if it dies on the
            // fifth seat, the first four were still worth having.
            Write();
        }

        // The phone, once, as a visitor — every public screen, because that is where most people
        // meet this site and no fixture has ever opened them at that width.
        await LogoutAsync();
        await Page.SetViewportSizeAsync(375, 812);
        foreach (var url in urls.Where(IsPublic))
            await VisitAsync("a visitor on a phone", url);
        await Page.SetViewportSizeAsync(1280, 720);

        Write();
        TestContext.Out.WriteLine($"{_visits} visits, {_found.Count} findings");
        Assert.That(_visits, Is.GreaterThan(0), "the sweep opened nothing at all");

        // After the report is written, so a failing run still leaves the full table to read.
        Assert.That(_hard, Is.Empty,
            "these are not design choices — a card's text on its own border, or a box hiding its own "
          + "content:\n  " + string.Join("\n  ", _hard.Take(40))
          + (_hard.Count > 40 ? $"\n  …and {_hard.Count - 40} more" : ""));
    }

    private static bool IsPublic(string url)
        // /tonight added 2026-09-21: the one screen a guest opens on a phone in a field, and the
        // phone pass had been skipping it.
        => url is "/" or "/pricing" or "/events" or "/find" or "/feed" or "/signup" or "/login" or "/tonight"
        || url.StartsWith("/o/", StringComparison.Ordinal)
        || url.StartsWith("/places", StringComparison.Ordinal)
        || url.StartsWith("/help", StringComparison.Ordinal);

    /// <summary>Every plain route, plus every parameterised route that a real id can fill.</summary>
    /// <remarks>
    /// The first version resolved only <c>{OrgId}</c>, which skipped every case, place, event,
    /// person and help screen on the site — the pages where the product actually lives. Each id
    /// below is best-effort and independent, so one endpoint that answers nothing costs its own
    /// routes rather than all of them.
    /// </remarks>
    private async Task<List<string>> UrlsAsync()
    {
        var urls = new List<string>(RouteCrawlHelper.PlainRoutes(["/logout"]));
        var ids = new Dictionary<string, string>(StringComparer.Ordinal);

        try
        {
            var token = await AdminTokenAsync();

            var orgs = await ApiArrayAsync("/api/organizations", token);
            if (First(orgs, "id") is { } orgId)
            {
                ids["OrgId"] = orgId;

                if (First(await ApiArrayAsync($"/api/organizations/{orgId}/cases", token), "id") is { } caseId)
                    ids["CaseId"] = caseId;
            }
            if (First(orgs, "urlName") is { } orgSlug) ids["UrlName"] = orgSlug;

            if (First(await ApiArrayAsync("/api/public/places", token), "id") is { } placeId)
                ids["PlaceId"] = placeId;

            if (First(await ApiArrayAsync("/api/public/hosted-events", token), "id") is { } eventId)
                ids["EventId"] = eventId;

            if (First(await ApiArrayAsync("/api/admin/app-users", token), "id") is { } userId)
            {
                ids["UserId"] = userId;
                ids["AppUserId"] = userId;
            }

            // Help topics are embedded in the app rather than served by the API, so the slug comes
            // from the index page's own links — the route a reader would follow.
            await Page.GotoAsync($"{BaseUrl}/help");
            var firstHelp = Page.Locator("a[href^='/help/']").First;
            try
            {
                await firstHelp.WaitForAsync(new() { State = WaitForSelectorState.Attached, Timeout = 10_000 });
                if (await firstHelp.GetAttributeAsync("href") is { Length: > 6 } href)
                    ids["Slug"] = href["/help/".Length..];
            }
            catch (Exception) { /* no topics visible is a legitimate state; those routes are skipped */ }
        }
        catch (Exception ex)
        {
            _found.Add(new("the sweep itself", "(resolving ids)", "could not resolve ids",
                ex.Message.Split('\n')[0].Trim()));
        }

        _resolved = string.Join(", ", ids.Keys.OrderBy(k => k));

        foreach (var route in RouteCrawlHelper.ParameterisedRoutes())
        {
            if (route.Contains("{Token}") || route.Contains("{AccessToken")) continue;
            if (RouteCrawlHelper.Fill(route, ids) is { } filled) urls.Add(filled);
        }

        return urls;
    }

    /// <summary>The ids that could be resolved, named in the report so a thin sweep is visible.</summary>
    private string _resolved = "";

    private async Task<string> AdminTokenAsync()
    {
        var login = await Page.APIRequest.PostAsync($"{ApiUrl}/login",
            new() { DataObject = new { email = SuperAdminEmail, password = SuperAdminPassword } });
        Assert.That(login.Ok, Is.True, "could not sign in to the API to resolve ids");
        return (await login.JsonAsync())?.GetProperty("accessToken").GetString() ?? "";
    }

    private async Task<JsonElement?> ApiArrayAsync(string path, string token)
    {
        try
        {
            var response = await Page.APIRequest.GetAsync($"{ApiUrl}{path}",
                new() { Headers = new Dictionary<string, string> { ["Authorization"] = $"Bearer {token}" } });
            if (!response.Ok) return null;
            return await response.JsonAsync();
        }
        catch (Exception) { return null; }
    }

    /// <summary>The first non-empty string value of that property in an array answer.</summary>
    private static string? First(JsonElement? json, string name)
    {
        // Some endpoints answer with a bare array and some with { items: [...] }.
        var array = json;
        if (json is { ValueKind: JsonValueKind.Object } obj
            && obj.TryGetProperty("items", out var items)) array = items;

        if (array is not { ValueKind: JsonValueKind.Array } list) return null;

        foreach (var item in list.EnumerateArray())
        {
            if (item.TryGetProperty(name, out var v)
                && v.ValueKind == JsonValueKind.String
                && v.GetString() is { Length: > 0 } s)
            {
                return s;
            }
        }
        return null;
    }

    private void Write()
    {
        Directory.CreateDirectory(OutRoot);

        var report = new StringBuilder();
        report.AppendLine($"# The whole site, as everybody — {ThemeName} theme");
        report.AppendLine();
        report.AppendLine($"{_urlCount} address(es) to walk, {_visits} screen(s) opened, {_found.Count} finding(s).");
        report.AppendLine();
        report.AppendLine($"Ids resolved: {(_resolved.Length == 0 ? "none" : _resolved)}");
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

        // Named for the theme it measured. Both themes used to write "report.md", so sweeping light
        // and then dark left one file that claimed to be the whole story and was half of it — and
        // the half that survived was whichever ran last. VisualAuditWalk already did this; this did
        // not (2026-09-22).
        var path = Path.Combine(OutRoot, $"report-{ThemeName}.md");
        File.WriteAllText(path, report.ToString());
        TestContext.Out.WriteLine($"{_found.Count} finding(s) → {path}");
    }
}
