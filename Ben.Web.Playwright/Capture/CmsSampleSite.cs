using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

namespace Ben.Web.Playwright.Capture;

/// <summary>
/// A small public site for a group, for the screenshots of the CMS editor (10/09/2026): pages three levels
/// deep, an intro on the first, sections to put in order, and one page still a draft.
/// </summary>
/// <remarks>
/// <para>Seeded groups have no CMS pages, and a photograph of an empty page list teaches nothing about a tree.
/// This makes the pages a group would plausibly have, through the API, the way an editor would.</para>
///
/// <para>Idempotent: a page whose address already exists is left alone, so a capture can run again without
/// piling up copies. Captures run against a throwaway database (scripts/run-e2e.sh), never the shared one.</para>
/// </remarks>
public static class CmsSampleSite
{
    private sealed record Spec(string Title, string Slug, string? Parent, bool Published, string Intro, string[] Sections);

    private static readonly Spec[] Pages =
    [
        new("About us", "about-us", null, true,
            "<p>We are volunteers who look into the stories people tell about old places. We have been going out at night since 2009, and we write up everything we find, including the nights we find nothing.</p>",
            ["Who we are", "How we work"]),
        new("Our team", "our-team", "about-us", true,
            "<p>Eleven members, two of them founders, and a rotating cast of guests.</p>", ["Founders", "Members"]),
        new("Our equipment", "our-equipment", "our-team", true,
            "<p>What we take out, and why.</p>", ["Cameras", "Audio"]),
        new("Our investigations", "our-investigations", null, true,
            "<p>Places we have looked into, newest first.</p>", ["Recent nights"]),
        new("Evidence", "evidence", "our-investigations", true,
            "<p>What we recorded, with the context it was recorded in.</p>", ["Audio", "Photographs"]),
        new("Ghost tours", "ghost-tours", null, true,
            "<p>Walks through the old town on Friday and Saturday nights in October.</p>", ["When and where"]),
        new("Contact us", "contact-us", null, false,
            "<p>Write to us, and somebody will answer within two days.</p>", []),
    ];

    /// <summary>Makes the site in <paramref name="orgId"/> unless it is there already; returns its pages by address.</summary>
    public static async Task<Dictionary<string, string>> EnsureAsync(string apiUrl, string token, string orgId)
    {
        using var api = new HttpClient { BaseAddress = new Uri(apiUrl) };
        api.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var ids = new Dictionary<string, string>();
        foreach (var p in (await api.GetFromJsonAsync<JsonElement>($"/api/organizations/{orgId}/pages")).EnumerateArray())
            ids[p.GetProperty("urlName").GetString()!] = p.GetProperty("id").GetString()!;

        var order = 0;
        foreach (var spec in Pages)
        {
            order++;
            if (ids.ContainsKey(spec.Slug)) continue;

            string? parent = spec.Parent is null ? null : ids[spec.Parent];
            var created = await api.PostAsJsonAsync($"/api/organizations/{orgId}/pages", new
            {
                pageTitle = spec.Title, urlName = spec.Slug, pageHtml = spec.Intro, isPublic = true,
                parentPageId = parent, sortOrder = order,
            });
            created.EnsureSuccessStatusCode();
            var id = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetString()!;
            ids[spec.Slug] = id;

            var s = 0;
            foreach (var heading in spec.Sections)
                (await api.PostAsJsonAsync($"/api/organizations/{orgId}/pages/{id}/sections", new
                {
                    sectionType = 1, title = heading, sortOrder = ++s, isActive = true,
                    contentJson = JsonSerializer.Serialize(new { html = $"<p>{heading}: a few paragraphs written by the group.</p>" }),
                })).EnsureSuccessStatusCode();

            if (spec.Published)
                (await api.PutAsJsonAsync($"/api/organizations/{orgId}/pages/{id}", new
                {
                    pageTitle = spec.Title, urlName = spec.Slug, pageHtml = spec.Intro, isPublished = true, isPublic = true,
                    parentPageId = parent, sortOrder = order,
                })).EnsureSuccessStatusCode();
        }
        return ids;
    }

    /// <summary>
    /// Takes the sample site down again after a capture, deepest pages first, so the pages don't stay in the
    /// test database and change what the rest of the suite sees (a group's published-page count, its menu).
    /// </summary>
    public static async Task RemoveAsync(string apiUrl, string token, string orgId)
    {
        using var api = new HttpClient { BaseAddress = new Uri(apiUrl) };
        api.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var ids = new Dictionary<string, string>();
        foreach (var p in (await api.GetFromJsonAsync<JsonElement>($"/api/organizations/{orgId}/pages")).EnumerateArray())
            ids[p.GetProperty("urlName").GetString()!] = p.GetProperty("id").GetString()!;

        foreach (var spec in Enumerable.Reverse(Pages))
            if (ids.TryGetValue(spec.Slug, out var id))
                await api.DeleteAsync($"/api/organizations/{orgId}/pages/{id}");
    }
}
