using Microsoft.Playwright;
using NUnit.Framework;
using System.Text.Json;

namespace Ben.Web.Playwright;

/// <summary>
/// The other half of the crawl: routes that need a real id. These are where the app actually
/// lives — an organisation, a case, a public page — and a route only ever opened with a made-up id
/// proves nothing.
/// <para>
/// Ids are resolved from the API at run time rather than hard-coded, so the crawl follows the seed
/// data instead of going stale against it.
/// </para>
/// </summary>
[TestFixture]
[Category("Crawl")]
public class ParameterisedRouteCrawlTests : BenTestBase
{
    private readonly Dictionary<string, string> _ids = new();

    /// <summary>
    /// Values that apply only under a route prefix, because a parameter name does not always mean
    /// the same thing: <c>{UrlName}</c> is an organization on <c>/o/{UrlName}</c> and a
    /// publication on <c>/publications/{UrlName}</c>.
    /// </summary>
    private readonly Dictionary<string, Dictionary<string, string>> _routeIds = new();

    /// <summary>
    /// Routes keyed only by a token that gets emailed to someone — an invite, an attendance link,
    /// an email validation. A made-up token exercises the rejection path rather than the page, so
    /// these are covered by their own tests instead.
    /// </summary>
    private static readonly string[] TokenPlaceholders =
        { "{Token}", "{AccessToken:guid}" };

    /// <summary>Whether this URL only exists when a feature switch is on, and that switch is off.</summary>
    private static async Task<bool> IsBehindAnOffSwitchAsync(string url)
    {
        (string Flag, string Prefix)[] switched =
        [
            ("features.publications",  "/publications"),
            ("features.video-editor",  "/video-editor"),
            ("features.media-library", "/media"),
            ("features.equipment",     "/equipment-catalog"),
        ];

        foreach (var (flag, prefix) in switched)
        {
            if (url.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
                && await FeatureIsOffAsync(flag))
            {
                return true;
            }
        }
        return false;
    }

    private async Task<JsonElement?> ApiAsync(string path, string token)
    {
        var response = await Page.APIRequest.GetAsync($"{ApiUrl}{path}",
            new() { Headers = new Dictionary<string, string> { ["Authorization"] = $"Bearer {token}" } });
        if (!response.Ok) return null;
        try { return await response.JsonAsync(); } catch { return null; }
    }

    private static string? FirstValue(JsonElement? json, params string[] names)
    {
        if (json is not { ValueKind: JsonValueKind.Array } array) return null;
        foreach (var item in array.EnumerateArray())
            foreach (var name in names)
                if (item.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String
                    && v.GetString() is { Length: > 0 } s)
                    return s;
        return null;
    }

    private async Task ResolveIdsAsync()
    {
        var login = await Page.APIRequest.PostAsync($"{ApiUrl}/login",
            new() { DataObject = new { email = SuperAdminEmail, password = SuperAdminPassword } });
        Assert.That(login.Ok, Is.True, "could not sign in to the API to resolve ids");

        var token = (await login.JsonAsync())?.GetProperty("accessToken").GetString() ?? "";

        var orgs = await ApiAsync("/api/organizations", token);
        if (FirstValue(orgs, "id", "organizationId") is { } orgId)
        {
            _ids["OrgId"] = orgId;

            var cases = await ApiAsync($"/api/organizations/{orgId}/cases", token);
            if (FirstValue(cases, "id", "caseId") is { } caseId) _ids["CaseId"] = caseId;

            // The request-review page needs a request THIS org was offered — the ids travel as a
            // pair, so it is resolved from the same org's pending list rather than independently.
            var pending = await ApiAsync($"/api/organizations/{orgId}/cases/pending-requests", token);
            if (FirstValue(pending, "clientRequestId") is { } pendingId) _ids["ClientRequestId"] = pendingId;
        }

        if (FirstValue(orgs, "urlName") is { } urlName) _ids["UrlName"] = urlName;

        // /publications/{UrlName} wants a PUBLICATION's slug, not an organization's — the two
        // routes spell the parameter the same way, so the shared lookup above quietly handed the
        // publications page an org slug and it correctly answered "not found". Created rather
        // than assumed, exactly as the field session below is: nothing seeds a publication, and
        // without one the site's newest public surface is never visited by the crawl at all.
        if (_ids.TryGetValue("OrgId", out var pubOrgId)
            && await EnsurePublicationAsync(pubOrgId, token) is { } publicationSlug)
        {
            _routeIds["/publications/"] = new Dictionary<string, string> { ["UrlName"] = publicationSlug };
        }

        if (FirstValue(await ApiAsync("/api/admin/app-users", token), "id", "userId") is { } userId)
            _ids["UserId"] = userId;

        if (FirstValue(await ApiAsync("/api/equipment-catalog/models", token), "id", "modelId") is { } modelId)
            _ids["ModelId"] = modelId;

        // A field session the crawler can actually open. Uploaded rather than assumed: nothing
        // seeds one, and without it the player's route is skipped — which would mean the one
        // guard against dead-end links never visits the newest page on the site.
        //
        // The device id is FIXED, so running this a hundred times leaves one row rather than a
        // hundred. That is the same retry behaviour a phone relies on when an upload drops.
        if (await EnsureFieldSessionAsync(token) is { } fieldSessionId)
        {
            _ids["SessionId"] = fieldSessionId;
        }

        // Help documents are embedded in the app rather than served by the API, so the slug comes
        // from the index page's own links — the same route a reader would follow.
        await Page.GotoAsync($"{BaseUrl}/help");

        // Wait for the LINK, not for the network. NetworkIdle never settles on a Blazor Server
        // page — the SignalR circuit keeps the connection busy — so the old wait returned at its
        // own timeout and the locator below then failed on a page that renders perfectly well.
        // That took the whole crawl down with it, and every route it would have visited.
        var firstHelpLink = Page.Locator("a[href^='/help/']").First;
        try
        {
            await firstHelpLink.WaitForAsync(new() { State = WaitForSelectorState.Attached, Timeout = 15_000 });
            var helpHref = await firstHelpLink.GetAttributeAsync("href");
            if (helpHref is { Length: > 6 }) _ids["Slug"] = helpHref["/help/".Length..];
        }
        catch (TimeoutException)
        {
            // No help topics visible to this viewer is a legitimate state, not a crawl failure:
            // the index shows only the topics that apply to you. Routes needing {Slug} are then
            // skipped and reported as skipped, which is the honest outcome.
            TestContext.Out.WriteLine("   no /help/ links visible — {Slug} routes will be skipped");
        }
    }

    /// <summary>Uploads a tiny field session for the crawler, or returns null if it cannot.</summary>
    /// <summary>
    /// Finds a publication on this group, creating one if there is none, and returns its slug.
    /// </summary>
    /// <remarks>
    /// Nothing seeds a publication, so before this the crawl filled /publications/{UrlName} with
    /// an ORGANIZATION slug — the parameter is spelled the same on both routes — and reported the
    /// resulting 404 as a broken route. Creating one makes the page genuinely testable and gives
    /// publications their first end-to-end coverage.
    ///
    /// Idempotent: an existing publication is reused, so running this repeatedly leaves one row
    /// rather than a hundred, the same rule the field-session helper follows.
    /// </remarks>
    private async Task<string?> EnsurePublicationAsync(string orgId, string token)
    {
        var existing = await ApiAsync($"/api/organizations/{orgId}/publications", token);
        if (FirstValue(existing, "urlName") is { } already) return already;

        var created = await Page.APIRequest.PostAsync(
            $"{ApiUrl}/api/organizations/{orgId}/publications",
            new()
            {
                Headers = new Dictionary<string, string> { ["Authorization"] = $"Bearer {token}" },
                DataObject = new { Title = "Field Notes", Description = "Crawl fixture.", IsPublic = true },
            });

        if (!created.Ok) return null;
        var again = await ApiAsync($"/api/organizations/{orgId}/publications", token);
        return FirstValue(again, "urlName");
    }

    private async Task<string?> EnsureFieldSessionAsync(string token)
    {
        if (FirstValue(await ApiAsync("/api/field-sessions/mine", token), "id", "sessionId")
            is { } existing)
        {
            return existing;
        }

        const string document = """
            {"format_version":"1.0.0",
             "device":{"manufacturer":"Apple","model":"iPhone17,1"},
             "session":{"started_at":"2026-08-01T02:00:00.000Z",
                        "ended_at":"2026-08-01T02:05:00.000Z",
                        "location_label":"Route crawl fixture",
                        "trigger":{"mode":"hybrid","interval_seconds":2}},
             "readings":[{"at":"2026-08-01T02:00:00.000Z","triggered_by":"interval",
                          "measurements":{"emf":{"value":48.0,"unit":"uT","baseline":48.0}}}]}
            """;

        var api = await Playwright.APIRequest.NewContextAsync(new() { BaseURL = ApiUrl });
        var form = Context.APIRequest.CreateFormData();
        form.Append("file", new FilePayload
        {
            Name = "data.json",
            MimeType = "application/json",
            Buffer = System.Text.Encoding.UTF8.GetBytes(document),
        });
        // Stable on purpose — see the caller.
        form.Append("deviceSessionId", "11111111-2222-3333-4444-555555555555");

        var response = await api.PostAsync("/api/field-sessions/document", new()
        {
            Headers = new Dictionary<string, string> { ["Authorization"] = $"Bearer {token}" },
            Multipart = form,
        });
        if (!response.Ok) return null;
        return (await response.JsonAsync())?.GetProperty("id").GetString();
    }

    [Test]
    public async Task EveryParameterisedRoute_RendersWithARealId()
    {
        await LoginAsync(SuperAdminEmail, SuperAdminPassword);
        await ResolveIdsAsync();

        Assert.That(_ids, Is.Not.Empty, "no ids could be resolved — is the API up with seed data?");
        TestContext.Out.WriteLine("resolved: " + string.Join(", ", _ids.Keys));

        var routes = RouteCrawlHelper.ParameterisedRoutes();
        var skipped = new List<string>();
        var broken = new List<string>();
        var visited = 0;

        foreach (var route in routes)
        {
            if (TokenPlaceholders.Any(route.Contains)) { skipped.Add($"{route} (token only)"); continue; }

            // A parameter name does not always mean the same thing: {UrlName} is an organization
            // on /o/{UrlName} and a publication on /publications/{UrlName}. Route-specific values
            // win where they exist.
            var idsForRoute = _ids;
            foreach (var (prefix, overrides) in _routeIds)
            {
                if (!route.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) continue;
                var merged = new Dictionary<string, string>(_ids);
                foreach (var (k, v) in overrides) merged[k] = v;
                idsForRoute = merged;
                break;
            }

            var url = RouteCrawlHelper.Fill(route, idsForRoute);
            if (url is null) { skipped.Add($"{route} (no id available)"); continue; }

            // A route behind a switch that is OFF is genuinely not routed. Crawling it and
            // reporting "not routed" makes the crawl fail on a deployment behaving exactly as
            // configured — which is how a crawl stops being read.
            if (await IsBehindAnOffSwitchAsync(url))
            {
                skipped.Add($"{route} (feature switched off)");
                continue;
            }

            visited++;
            try
            {
                await Page.GotoAsync($"{BaseUrl}{url}", new() { Timeout = 25_000 });
                await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);
                await WaitUntilLoadedAsync(10_000);

                var state = await Page.EvaluateAsync<JsonElement>(@"() => {
                    const main = document.querySelector('.app-content, main, .content-wrapper');
                    const body = document.body.innerText || '';
                    const err  = document.querySelector('#blazor-error-ui');
                    return {
                        content: main ? (main.innerText || '').trim().length : 0,
                        unhandled: /An unhandled error has occurred/i.test(body),
                        notFound: /Page not found/i.test(body),
                        circuitDown: !!err && getComputedStyle(err).display !== 'none'
                    };
                }");

                var content = state.GetProperty("content").GetInt32();
                if (state.GetProperty("unhandled").GetBoolean())        broken.Add($"{url} — unhandled error");
                else if (state.GetProperty("notFound").GetBoolean())    broken.Add($"{url} — not routed");
                else if (state.GetProperty("circuitDown").GetBoolean()) broken.Add($"{url} — circuit dropped");
                else if (content < 40) broken.Add($"{url} — rendered {content} chars");
            }
            catch (Exception ex)
            {
                broken.Add($"{url} — {ex.GetType().Name}: {ex.Message.Split('\n')[0]}");
            }
        }

        TestContext.Out.WriteLine($"crawled {visited}, skipped {skipped.Count}, {broken.Count} problem(s)");
        foreach (var s in skipped) TestContext.Out.WriteLine("   skipped: " + s);
        foreach (var b in broken)  TestContext.Out.WriteLine("   BROKEN:  " + b);

        Assert.That(broken, Is.Empty,
            $"{broken.Count} parameterised route(s) did not come up:\n  " + string.Join("\n  ", broken));
    }
}
