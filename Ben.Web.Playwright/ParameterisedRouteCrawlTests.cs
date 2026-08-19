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

        // Help documents are embedded in the app rather than served by the API, so the slug comes
        // from the index page's own links — the same route a reader would follow.
        await Page.GotoAsync($"{BaseUrl}/help");
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);
        var helpHref = await Page.Locator("a[href^='/help/']").First.GetAttributeAsync("href");
        if (helpHref is { Length: > 6 }) _ids["Slug"] = helpHref["/help/".Length..];
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
