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
    /// Routes keyed only by a token that gets emailed to someone — an invite, an attendance link,
    /// an email validation. A made-up token exercises the rejection path rather than the page, so
    /// these are covered by their own tests instead.
    /// </summary>
    private static readonly string[] TokenPlaceholders =
        { "{Token}", "{AccessToken:guid}" };

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
        }

        if (FirstValue(orgs, "urlName") is { } urlName) _ids["UrlName"] = urlName;

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
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);
        var helpHref = await Page.Locator("a[href^='/help/']").First.GetAttributeAsync("href");
        if (helpHref is { Length: > 6 }) _ids["Slug"] = helpHref["/help/".Length..];
    }

    /// <summary>Uploads a tiny field session for the crawler, or returns null if it cannot.</summary>
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

            var url = RouteCrawlHelper.Fill(route, _ids);
            if (url is null) { skipped.Add($"{route} (no id available)"); continue; }

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
