using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Playwright;
using NUnit.Framework;

namespace Ben.Web.Playwright.Tests;

/// <summary>
/// The CMS editor's working parts (Ben, 10/09/2026): the page grid's More actions menu, Add Section,
/// the section preview, page placement and the Ordering window.
/// </summary>
/// <remarks>
/// Each test builds its own small site through the API — pages stamped with the run, removed
/// afterwards — so none of them depends on what another test left behind.
/// </remarks>
[TestFixture]
[Category("Cms")]
[NonParallelizable]
public class CmsEditorUxTests : BenTestBase
{
    private string _orgId = "";
    private HttpClient? _api;
    private readonly List<string> _made = [];
    private string _stamp = "";

    [SetUp]
    public async Task MakeASiteAsync()
    {
        _stamp = Guid.NewGuid().ToString("N")[..6];
        _orgId = await OrgIdBySlugAsync("benco");
        var token = await SuperAdminTokenAsync();
        Assert.That(token, Is.Not.Null, "the admin seat should be able to sign in");
        _api = new HttpClient { BaseAddress = new Uri(ApiUrl) };
        _api.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
    }

    [TearDown]
    public async Task RemoveTheSiteAsync()
    {
        // Children first, so nothing is re-parented onto a page about to go.
        foreach (var id in Enumerable.Reverse(_made))
        {
            try { await _api!.DeleteAsync($"/api/organizations/{_orgId}/pages/{id}"); }
            catch (HttpRequestException) { }
        }
        _made.Clear();
        _api?.Dispose();
    }

    /// <summary>Creates a page through the API and returns its id.</summary>
    private async Task<string> MakePageAsync(string title, string? parentId = null, int sortOrder = 1)
    {
        var slug = $"{title.ToLowerInvariant().Replace(' ', '-')}-{_stamp}";
        var response = await _api!.PostAsJsonAsync($"/api/organizations/{_orgId}/pages", new
        {
            pageTitle = $"{title} {_stamp}", urlName = slug, pageHtml = "<p>Intro</p>",
            isPublic = true, parentPageId = parentId, sortOrder,
        });
        Assert.That(response.IsSuccessStatusCode, Is.True, await response.Content.ReadAsStringAsync());
        var id = (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetString()!;
        _made.Add(id);
        return id;
    }

    private async Task OpenTheEditorAsync(bool embedded = false)
    {
        await Page.GotoAsync(embedded ? $"{BaseUrl}/organizations/{_orgId}" : $"{BaseUrl}/organizations/{_orgId}/cms");
        await WaitUntilLoadedAsync();
        await SkipAnyTourAsync();
        if (embedded)
            await OpenTabAsync("CMS", Main.GetByRole(AriaRole.Button, new() { Name = "New Page" }));
        await Expect(Main.GetByText($"About {_stamp}").First).ToBeVisibleAsync(new() { Timeout = 30_000 });
    }

    /// <summary>
    /// The More actions menu on a page's row has to be readable and clickable — Ben opened it and
    /// "could not see any of the actions", cut off by the grid row.
    /// </summary>
    [Test]
    public async Task MoreActions_MenuIsNotCutOffByTheGrid()
    {
        await MakePageAsync("About");
        await LoginAsync(SuperAdminEmail, SuperAdminPassword);

        foreach (var (embedded, width) in new[] { (false, 1280), (true, 1280), (false, 1024), (true, 1024) })
        {
            await Page.SetViewportSizeAsync(width, 800);
            await OpenTheEditorAsync(embedded);
            var row = Main.Locator("tr", new() { HasTextString = $"About {_stamp}" }).First;
            var more = row.GetByRole(AriaRole.Button, new() { Name = "More actions" });
            await ClickUntilAsync(more, Main.Locator(".dropdown-menu.show"));

            // Every item must be the thing under its own center — not hidden behind a cell, a row
            // or the grid's edge.
            var items = Main.Locator(".dropdown-menu.show .dropdown-item");
            var count = await items.CountAsync();
            Assert.That(count, Is.GreaterThan(0), "the menu opened with nothing in it");
            for (var i = 0; i < count; i++)
            {
                var onTop = await items.Nth(i).EvaluateAsync<bool>(@"el => {
                    const r = el.getBoundingClientRect();
                    if (r.width < 4 || r.height < 4) return false;
                    const hit = document.elementFromPoint(r.left + r.width / 2, r.top + r.height / 2);
                    return !!hit && (hit === el || el.contains(hit));
                }");
                Assert.That(onTop, Is.True,
                    $"{(embedded ? "group page" : "CMS page")} at {width}px: menu item {i} ('{await items.Nth(i).InnerTextAsync()}') is covered or clipped");
            }
            await Page.Keyboard.PressAsync("Escape");
        }
    }

    /// <summary>A page's stored parent and sort order, read back through the API.</summary>
    private async Task<(string? Parent, int Sort)> PlaceOfAsync(string pageId)
    {
        var page = await _api!.GetFromJsonAsync<JsonElement>($"/api/organizations/{_orgId}/pages/{pageId}");
        var parent = page.GetProperty("parentPageId");
        return (parent.ValueKind == JsonValueKind.Null ? null : parent.GetString(), page.GetProperty("sortOrder").GetInt32());
    }

    private async Task OpenThePageEditorAsync(string pageId)
    {
        await Page.GotoAsync($"{BaseUrl}/organizations/{_orgId}/cms/pages/{pageId}");
        await WaitUntilLoadedAsync();
        await Expect(Main.Locator("#cms-add-section")).ToBeVisibleAsync(new() { Timeout = 30_000 });
    }

    /// <summary>
    /// Add Section has to show where the form opened, offer each kind of section as a picture, and let the
    /// author see the filled-in section as a visitor will before saving it (Ben, 10/09/2026).
    /// </summary>
    [Test]
    public async Task AddSection_ScrollsToTheForm_ShowsSketches_AndPreviewsBeforeSaving()
    {
        var about = await MakePageAsync("About");
        await LoginAsync(SuperAdminEmail, SuperAdminPassword);
        await Page.SetViewportSizeAsync(1280, 720);
        await OpenThePageEditorAsync(about);

        // From the bottom of a long page, where the button is and the form is not.
        await Main.Locator("#cms-add-section").ScrollIntoViewIfNeededAsync();
        await ClickUntilAsync(Main.Locator("#cms-add-section"), Main.Locator("#cms-section-editor"));
        await Expect(Main.Locator("#cms-section-editor")).ToBeInViewportAsync(new() { Timeout = 5_000 });

        // Every kind is a card with a sketch; picking one marks it.
        var cards = Main.Locator("#cms-section-type-picker [role=radio]");
        Assert.That(await cards.CountAsync(), Is.GreaterThanOrEqualTo(9));
        Assert.That(await Main.Locator("#cms-section-type-picker svg.cms-sketch").CountAsync(), Is.EqualTo(await cards.CountAsync()));
        var text = Main.Locator("#cms-section-type-picker [data-section-type=RichText]");
        await text.ClickAsync();
        await Expect(text).ToHaveAttributeAsync("aria-checked", "true");

        await Main.Locator("#orgcmspageedit-section-title-optional-13b4").FillAsync("Our history");
        await Main.Locator("#cms-section-editor .ProseMirror").ClickAsync();
        await Page.Keyboard.TypeAsync("Built in 1859 as a spa hotel.");
        await Page.WaitForTimeoutAsync(400);   // the editor reports what was typed on a 100 ms debounce

        // Preview sits before Cancel, and shows the section the way the public page draws it.
        var buttons = Main.Locator("#cms-section-editor .card-body > .d-flex.justify-content-end button");
        Assert.That(await buttons.Nth(0).InnerTextAsync(), Does.Contain("Preview"));
        Assert.That(await buttons.Nth(1).InnerTextAsync(), Does.Contain("Cancel"));

        var preview = Page.Locator("#cms-section-preview-body");
        await ClickUntilAsync(Main.Locator("#cms-section-preview"), preview);
        await Expect(preview.Locator(".cms-section-title")).ToHaveTextAsync("Our history");
        await Expect(preview).ToContainTextAsync("Built in 1859 as a spa hotel.");
        await Page.Locator("#cms-section-preview-close").ClickAsync();

        // Previewing saved nothing.
        var sections = await _api!.GetFromJsonAsync<JsonElement>($"/api/organizations/{_orgId}/pages/{about}/sections");
        Assert.That(sections.GetArrayLength(), Is.EqualTo(0));
    }

    /// <summary>
    /// A new page's title can come from the Ideas list, its address follows the title, and the tree puts it
    /// under another page — saved where it was put.
    /// </summary>
    [Test]
    public async Task NewPage_TakesATitleIdea_AndGoesWhereTheTreePutsIt()
    {
        var about = await MakePageAsync("About");
        await LoginAsync(SuperAdminEmail, SuperAdminPassword);
        await OpenTheEditorAsync();

        var dialog = Page.Locator(".modal.show");
        await ClickUntilAsync(Main.Locator("#cms-new-page"), dialog);

        await ClickUntilAsync(dialog.Locator(".cms-title-ideas button[title='Ideas for a title']"),
                              dialog.GetByRole(AriaRole.Button, new() { Name = "Frequently asked questions" }));
        await dialog.GetByRole(AriaRole.Button, new() { Name = "Frequently asked questions" }).ClickAsync();
        await Expect(dialog.Locator("#orgcmseditor-title-5a77")).ToHaveValueAsync("Frequently asked questions");
        await Expect(dialog.Locator("#orgcmseditor-url-slug-d5ae")).ToHaveValueAsync("frequently-asked-questions");

        // Make it this run's own. A new page starts last at the top level; the arrow puts it under the page
        // just above it — the group's last top-level page, which is About unless other pages exist.
        var title = $"FAQ {_stamp}";
        await dialog.Locator("#orgcmseditor-title-5a77").FillAsync(title);
        await dialog.Locator("#orgcmseditor-url-slug-d5ae").FillAsync($"faq-{_stamp}");
        await Expect(dialog.Locator(".cms-placer [data-current=true]")).ToContainTextAsync(title);
        var before = await _api!.GetFromJsonAsync<JsonElement>($"/api/organizations/{_orgId}/pages");
        var lastTop = before.EnumerateArray()
            .Where(p => p.GetProperty("parentPageId").ValueKind == JsonValueKind.Null)
            .OrderBy(p => p.GetProperty("sortOrder").GetInt32()).ThenBy(p => p.GetProperty("pageTitle").GetString(), StringComparer.OrdinalIgnoreCase)
            .Last().GetProperty("id").GetString();
        await dialog.Locator("#cms-placer-in").ClickAsync();
        await Expect(dialog.Locator("#cms-placer-out")).ToBeEnabledAsync();   // it is a level down now

        await dialog.GetByRole(AriaRole.Button, new() { Name = "Save", Exact = false }).First.ClickAsync();
        await Expect(dialog).ToBeHiddenAsync(new() { Timeout = 10_000 });

        var pages = await _api!.GetFromJsonAsync<JsonElement>($"/api/organizations/{_orgId}/pages");
        var faq = pages.EnumerateArray().Single(p => p.GetProperty("pageTitle").GetString() == title);
        _made.Add(faq.GetProperty("id").GetString()!);
        Assert.That(faq.GetProperty("parentPageId").GetString(), Is.EqualTo(lastTop));

        // The list shows it under About.
        var row = Main.Locator("tr", new() { HasTextString = title });
        await Expect(row).ToContainTextAsync("↳");
    }

    /// <summary>
    /// The Ordering window shows every page with its sections, its tips can be hidden, and each move is
    /// saved as it is made — pages anywhere, sections only on their own page.
    /// </summary>
    [Test]
    public async Task Ordering_SavesEachMoveAsItIsMade()
    {
        var about = await MakePageAsync("About");
        var contact = await MakePageAsync("Contact", sortOrder: 2);
        foreach (var heading in new[] { "First", "Second" })
        {
            var made = await _api!.PostAsJsonAsync($"/api/organizations/{_orgId}/pages/{about}/sections", new
            {
                sectionType = 1, title = $"{heading} {_stamp}", contentJson = "{\"html\":\"<p>x</p>\"}", sortOrder = heading == "First" ? 1 : 2, isActive = true,
            });
            Assert.That(made.IsSuccessStatusCode, Is.True);
        }

        await LoginAsync(SuperAdminEmail, SuperAdminPassword);
        await OpenTheEditorAsync();
        var dialog = Page.Locator(".modal.show");
        await ClickUntilAsync(Main.Locator("#cms-ordering-open"), dialog.Locator("#cms-ordering-tree"));

        // The tips can be hidden, and brought back.
        if (await dialog.Locator("#cms-ordering-tips").CountAsync() > 0)
        {
            await dialog.Locator("#cms-ordering-hide-tips").ClickAsync();
            await Expect(dialog.Locator("#cms-ordering-tips")).ToHaveCountAsync(0);
        }
        await dialog.Locator("#cms-ordering-show-tips").ClickAsync();
        await Expect(dialog.Locator("#cms-ordering-tips")).ToBeVisibleAsync();

        // A section, moved down on its own page with the arrow.
        await dialog.Locator(".cms-tree-node", new() { HasTextString = $"First {_stamp}" }).ClickAsync();
        await dialog.Locator("#cms-ordering-down").ClickAsync();
        await Expect(dialog.Locator("#cms-ordering-status")).ToContainTextAsync("Saved");
        var sections = await _api!.GetFromJsonAsync<JsonElement>($"/api/organizations/{_orgId}/pages/{about}/sections");
        Assert.That(sections.EnumerateArray().Select(x => x.GetProperty("title").GetString()),
            Is.EqualTo(new[] { $"Second {_stamp}", $"First {_stamp}" }));

        // A page, dragged onto another: it goes under that page.
        var contactRow = dialog.Locator(".cms-tree-node[data-kind=page]", new() { HasTextString = $"Contact {_stamp}" });
        var aboutRow = dialog.Locator(".cms-tree-node[data-kind=page]", new() { HasTextString = $"About {_stamp}" });
        await DragAsync(contactRow, aboutRow);
        await Expect(dialog.Locator("#cms-ordering-status")).ToContainTextAsync($"Contact {_stamp} moved", new() { Timeout = 10_000 });
        Assert.That((await PlaceOfAsync(contact)).Parent, Is.EqualTo(about));

        // Nothing to save at the end: Done just closes.
        await dialog.Locator("#cms-ordering-done").ClickAsync();
        await Expect(dialog).ToBeHiddenAsync();
    }

    /// <summary>
    /// A drag the way a hand does it: press, move a little so the tree knows it is a drag, glide to the target's
    /// middle (onto it, not beside it), let go. Telerik's tree ignores a press-and-jump.
    /// </summary>
    private async Task DragAsync(ILocator from, ILocator to)
    {
        var a = (await from.BoundingBoxAsync())!;
        var b = (await to.BoundingBoxAsync())!;
        await Page.Mouse.MoveAsync(a.X + a.Width / 2, a.Y + a.Height / 2);
        await Page.Mouse.DownAsync();
        await Page.Mouse.MoveAsync(a.X + a.Width / 2 + 8, a.Y + a.Height / 2 + 8, new() { Steps = 4 });
        await Page.Mouse.MoveAsync(b.X + b.Width / 2, b.Y + b.Height / 2, new() { Steps = 20 });
        await Page.WaitForTimeoutAsync(250);
        await Page.Mouse.UpAsync();
    }

    /// <summary>
    /// A visitor reads a page's intro above its sections, and the pages under it as a row of links
    /// beneath the menu.
    /// </summary>
    [Test]
    public async Task Visitor_ReadsTheIntroAboveTheSections_AndThePagesUnderIt()
    {
        var about = await MakePageAsync("About");
        var team = await MakePageAsync("Team", parentId: about);
        await _api!.PostAsJsonAsync($"/api/organizations/{_orgId}/pages/{about}/sections", new
        {
            sectionType = 1, title = $"Below {_stamp}", contentJson = "{\"html\":\"<p>Section text</p>\"}", sortOrder = 1, isActive = true,
        });
        foreach (var id in new[] { about, team })
        {
            var page = await _api.GetFromJsonAsync<JsonElement>($"/api/organizations/{_orgId}/pages/{id}");
            var put = await _api.PutAsJsonAsync($"/api/organizations/{_orgId}/pages/{id}", new
            {
                pageTitle = page.GetProperty("pageTitle").GetString(), urlName = page.GetProperty("urlName").GetString(),
                pageHtml = $"<p>Intro for {_stamp}</p>", isPublished = true, isPublic = true,
                parentPageId = id == team ? about : null, sortOrder = 1,
            });
            Assert.That(put.IsSuccessStatusCode, Is.True, await put.Content.ReadAsStringAsync());
        }

        var orgs = await _api.GetFromJsonAsync<JsonElement>("/api/organizations");
        var urlName = orgs.EnumerateArray().First(o => o.GetProperty("id").GetString() == _orgId).GetProperty("urlName").GetString();

        var visitor = await Page.Context.Browser!.NewContextAsync();
        try
        {
            var page = await visitor.NewPageAsync();
            await page.GotoAsync($"{BaseUrl}/o/{urlName}/about-{_stamp}");
            var intro = page.Locator("[data-testid=cms-intro]");
            await Expect(intro).ToContainTextAsync($"Intro for {_stamp}", new() { Timeout = 30_000 });
            // One look at the document, not two measurements: the page draws once on the server and again when
            // it goes live, and an element measured between the two is briefly not there.
            await page.WaitForFunctionAsync(@"() => {
                const intro = document.querySelector('[data-testid=cms-intro]');
                const section = document.querySelector('.cms-section');
                return !!intro && !!section && !!(intro.compareDocumentPosition(section) & Node.DOCUMENT_POSITION_FOLLOWING);
            }", null, new() { Timeout = 15_000 });

            var sub = page.Locator("[data-testid=org-subnav]");
            await Expect(sub.GetByRole(AriaRole.Link, new() { Name = $"Team {_stamp}" })).ToBeVisibleAsync();
        }
        finally { await visitor.CloseAsync(); }
    }

    /// <summary>
    /// The intro's editor links to the group's own pages from a list, rather than by typing an address.
    /// </summary>
    [Test]
    public async Task IntroEditor_LinksToAnotherOfTheGroupsPages()
    {
        var about = await MakePageAsync("About");
        await MakePageAsync("Contact", sortOrder: 2);
        await LoginAsync(SuperAdminEmail, SuperAdminPassword);
        await OpenThePageEditorAsync(about);

        var intro = Main.Locator(".card", new() { HasTextString = "Summary / Intro" });
        await intro.Locator(".ProseMirror").ClickAsync();
        await Page.Keyboard.PressAsync("End");
        await Page.Keyboard.TypeAsync(" ");

        var links = Page.Locator("#ben-editor-page-links");
        await ClickUntilAsync(intro.Locator(".ben-editor-page-link"), links);
        await links.GetByRole(AriaRole.Button, new() { Name = $"Contact {_stamp}" }).ClickAsync();
        await Expect(intro.Locator($".ProseMirror a[href$='/contact-{_stamp}']")).ToHaveTextAsync($"Contact {_stamp}");
    }

    /// <summary>
    /// "Our members" names only people who said yes from their own profile (backlog 256): a member turns it on,
    /// a visitor sees them; turns it off, and they are gone.
    /// </summary>
    [Test]
    public async Task OurMembers_ListsOnlyMembersWhoAgreed()
    {
        var team = await MakePageAsync("Team");
        var section = await _api!.PostAsJsonAsync($"/api/organizations/{_orgId}/pages/{team}/sections", new
        {
            sectionType = 5, title = $"Our members {_stamp}", contentJson = "{}", sortOrder = 1, isActive = true,
        });
        Assert.That(section.IsSuccessStatusCode, Is.True);
        var page = await _api.GetFromJsonAsync<JsonElement>($"/api/organizations/{_orgId}/pages/{team}");
        (await _api.PutAsJsonAsync($"/api/organizations/{_orgId}/pages/{team}", new
        {
            pageTitle = page.GetProperty("pageTitle").GetString(), urlName = page.GetProperty("urlName").GetString(),
            pageHtml = "", isPublished = true, isPublic = true, parentPageId = (string?)null, sortOrder = 1,
        })).EnsureSuccessStatusCode();
        var address = $"/o/benco/{page.GetProperty("urlName").GetString()}";

        await LoginAsync(UserEmail, UserPassword);
        await Page.GotoAsync($"{BaseUrl}/profile");
        await WaitUntilLoadedAsync();
        var toggle = Page.Locator("#my-public-listing").GetByRole(AriaRole.Switch, new() { Name = "BenCo", Exact = false });
        await Expect(toggle).ToBeVisibleAsync(new() { Timeout = 30_000 });

        async Task<string> VisitorSeesAsync()
        {
            var visitor = await Page.Context.Browser!.NewContextAsync();
            try
            {
                var v = await visitor.NewPageAsync();
                await v.GotoAsync($"{BaseUrl}{address}");
                var roster = v.GetByText($"Our members {_stamp}").Locator("xpath=..");
                await Expect(roster).ToBeVisibleAsync(new() { Timeout = 30_000 });
                return await roster.InnerTextAsync();
            }
            finally { await visitor.CloseAsync(); }
        }

        try
        {
            if (!await toggle.IsCheckedAsync()) await toggle.CheckAsync();
            await Page.WaitForTimeoutAsync(800);
            Assert.That(await VisitorSeesAsync(), Does.Contain("Sarah"), "a member who said yes should be listed");

            await toggle.UncheckAsync();
            await Page.WaitForTimeoutAsync(800);
            Assert.That(await VisitorSeesAsync(), Does.Not.Contain("Sarah"), "a member who said no should not be");
        }
        finally
        {
            if (await toggle.IsCheckedAsync()) await toggle.UncheckAsync();
        }
    }
}
