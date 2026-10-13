using System.Text.Json;
using Microsoft.Playwright;
using NUnit.Framework;

namespace Ben.Web.Playwright.Tests;

/// <summary>
/// Designed menus, end to end (Ben, 10/13/2026): the builder's list and design gallery, the editor's
/// live preview and look switcher, publishing, and what guests see on the event's page.
/// </summary>
/// <remarks>
/// It makes its own meal, item and menu through the API rather than relying on the seed's, because
/// <c>EventMenusTests</c> clears the weekend's meals, and puts everything back afterwards.
/// </remarks>
[TestFixture]
[Category("HostedEvents")]
public class EventMenuBuilderTests : BenTestBase
{
    private const string RoomsEventId = "40000002-0000-0000-0000-000000000002";

    private IAPIRequestContext _api = null!;
    private IAPIRequestContext _admin = null!;
    private string _orgId = "";
    private string _mealId = "";
    private string _itemId = "";
    private string _menuId = "";
    private JsonElement _mealsBefore;
    private readonly string _tag = Guid.NewGuid().ToString("N")[..6];

    private string Builder => $"/api/organizations/{_orgId}/events/{RoomsEventId}/menu-builder";

    [SetUp]
    public async Task MakeAMealAnItemAndAMenu()
    {
        _api = await Playwright.APIRequest.NewContextAsync(new() { BaseURL = ApiUrl });
        _orgId = await OrgIdBySlugAsync("paranormal365");

        var login = await _api.PostAsync("/login", new() { DataObject = new { email = SuperAdminEmail, password = SuperAdminPassword } });
        Assert.That(login.Ok, Is.True, "the SuperAdmin could not sign in");
        var token = (await login.JsonAsync())!.Value.GetProperty("accessToken").GetString();
        _admin = await Playwright.APIRequest.NewContextAsync(new()
        {
            BaseURL = ApiUrl, ExtraHTTPHeaders = new Dictionary<string, string> { ["Authorization"] = $"Bearer {token}" },
        });

        // A meal of our own on the first night, keeping whatever meals are already there.
        var meals = await _admin.GetAsync($"/api/organizations/{_orgId}/events/{RoomsEventId}/menus");
        _mealsBefore = (await meals.JsonAsync())!.Value.Clone();
        var ev = await _admin.GetAsync($"/api/organizations/{_orgId}/events/{RoomsEventId}");
        var night = (await ev.JsonAsync())!.Value.GetProperty("nights").EnumerateArray().First().GetProperty("id").GetString();

        var kept = _mealsBefore.GetProperty("menus").EnumerateArray().Select(Kept).ToList();
        kept.Add(new { hostedEventNightId = night, title = $"Supper {_tag}", servedAtLocal = "21:00:00" });
        var put = await _admin.PutAsync($"/api/organizations/{_orgId}/events/{RoomsEventId}/menus", new() { DataObject = new { menus = kept } });
        Assert.That(put.Ok, Is.True, await put.TextAsync());
        _mealId = (await put.JsonAsync())!.Value.GetProperty("menus").EnumerateArray()
            .First(m => m.GetProperty("title").GetString() == $"Supper {_tag}").GetProperty("id").GetString()!;

        var workspace = (await (await _admin.GetAsync(Builder)).JsonAsync())!.Value;
        var desserts = workspace.GetProperty("categories").EnumerateArray().First(c => c.GetProperty("name").GetString() == "Desserts").GetProperty("id").GetString();

        var item = await _admin.PostAsync($"{Builder}/library", new()
        {
            DataObject = new { scope = 1, menuCategoryId = desserts, name = $"Haunted pudding {_tag}", description = "Dark, with cream.", dietary = 2, allergens = 1 },
        });
        Assert.That(item.Ok, Is.True, await item.TextAsync());
        _itemId = (await item.JsonAsync())!.Value.GetProperty("id").GetString()!;

        var menu = await _admin.PostAsync(Builder, new() { DataObject = new { title = $"Test menu {_tag}", designKey = "classic-bistro" } });
        Assert.That(menu.Ok, Is.True, await menu.TextAsync());
        _menuId = (await menu.JsonAsync())!.Value.GetProperty("id").GetString()!;

        await LoginAsync(SuperAdminEmail, SuperAdminPassword);
    }

    [TearDown]
    public async Task PutItBack()
    {
        await _admin.DeleteAsync($"{Builder}/{_menuId}");
        await _admin.PostAsync($"{Builder}/library/{_itemId}/retire", new() { DataObject = new { } });
        var restore = _mealsBefore.GetProperty("menus").EnumerateArray().Select(Kept).ToList();
        await _admin.PutAsync($"/api/organizations/{_orgId}/events/{RoomsEventId}/menus", new() { DataObject = new { menus = restore } });
        await _admin.DisposeAsync();
        await _api.DisposeAsync();
    }

    /// <summary>A meal as it was, time and note included, so putting the set back loses nothing.</summary>
    private static object Kept(JsonElement m) => new
    {
        hostedEventNightId = m.GetProperty("hostedEventNightId").GetString(),
        title = m.GetProperty("title").GetString(),
        servedAtLocal = m.TryGetProperty("servedAtLocal", out var at) && at.ValueKind == JsonValueKind.String ? at.GetString() : null,
        notes = m.TryGetProperty("notes", out var n) && n.ValueKind == JsonValueKind.String ? n.GetString() : null,
        id = m.GetProperty("id").GetString(),
    };

    private async Task OpenAsync(string path, ILocator ready)
    {
        await Page.GotoAsync($"{BaseUrl}{path}");
        await WaitForTheCircuitAsync();
        await Expect(ready).ToBeVisibleAsync(new() { Timeout = 30_000 });
    }

    [Test]
    [Description("The list shows the menu, and every design as a finished sample to choose from.")]
    public async Task The_list_offers_every_design_as_a_finished_sample()
    {
        await OpenAsync($"/organizations/{_orgId}/events/{RoomsEventId}/menu-builder", Page.Locator("#menu-new"));

        await Expect(Page.Locator("[data-testid=menu-card]").Filter(new() { HasText = $"Test menu {_tag}" })).ToHaveCountAsync(1);
        await Expect(Page.Locator(".menu-gallery__card")).ToHaveCountAsync(25); // MenuDesigns.All; this project cannot see Ben.Data.Common
        await Expect(Page.Locator(".menu-gallery__card [data-testid=menu-document]").First).ToBeVisibleAsync();
    }

    [Test]
    [Description("The preview follows the form, unsaved, and a look can be tried before it's kept.")]
    public async Task The_preview_follows_what_is_typed_and_a_look_can_be_tried()
    {
        await OpenAsync($"/organizations/{_orgId}/events/{RoomsEventId}/menu-builder/{_menuId}", Page.Locator("#menu-save"));
        var preview = Page.Locator("[data-testid=menu-preview] [data-testid=menu-document]");
        await Expect(preview).ToHaveAttributeAsync("data-design", "classic-bistro");

        await Page.Locator("#menu-heading").FillAsync($"The Midnight Feast {_tag}");
        await Expect(preview).ToContainTextAsync($"The Midnight Feast {_tag}");
        await Expect(Page.Locator("#menu-unsaved")).ToBeVisibleAsync();

        await Page.Locator("#menu-design").SelectOptionAsync("seance-parlor");
        await Expect(preview).ToHaveAttributeAsync("data-design", "seance-parlor");

        // A section, then the item in it, priced on its own.
        var desserts = await Page.Locator("#menu-section-category option", new() { HasTextString = "Desserts" }).GetAttributeAsync("value");
        await Page.Locator("#menu-section-category").SelectOptionAsync(desserts!);
        await Page.Locator("#menu-section-add").ClickAsync();
        var section = Page.Locator("[data-testid=menu-section]").Last;
        await section.Locator("select").Last.SelectOptionAsync(_itemId);
        await Expect(preview).ToContainTextAsync($"Haunted pudding {_tag}");

        await section.Locator("[data-testid=menu-entry] select").First.SelectOptionAsync("PricedSeparately");
        await section.Locator("[data-testid=menu-entry] input[type=number]").FillAsync("7.5");
        await Expect(preview.Locator("[data-testid=menu-item]")).ToContainTextAsync("$7.50");

        await ClickUntilAsync(Page.Locator("#menu-save"), Page.Locator("#menu-editor-note"));
        await Expect(Page.Locator("#menu-editor-note")).ToHaveTextAsync("Saved.");

        await OpenAsync($"/organizations/{_orgId}/events/{RoomsEventId}/menu-builder/{_menuId}", Page.Locator("#menu-save"));
        await Expect(preview).ToHaveAttributeAsync("data-design", "seance-parlor");
        await Expect(preview).ToContainTextAsync($"Haunted pudding {_tag}");
    }

    [Test]
    [Description("A menu with nowhere to be served can't be published; served at a meal, guests see it.")]
    public async Task A_published_menu_is_on_the_event_page_and_a_menu_served_nowhere_is_refused()
    {
        // The item on the menu, covered by the event, through the API: this test is about publishing.
        var workspace = (await (await _admin.GetAsync(Builder)).JsonAsync())!.Value;
        var desserts = workspace.GetProperty("categories").EnumerateArray().First(c => c.GetProperty("name").GetString() == "Desserts").GetProperty("id").GetString();
        var save = await _admin.PutAsync($"{Builder}/{_menuId}", new()
        {
            DataObject = new
            {
                title = $"Test menu {_tag}", heading = $"Supper menu {_tag}", meal = 4, throughoutTheEvent = false,
                designKey = "midnight-vigil", prices = Array.Empty<object>(),
                sections = new[] { new { menuCategoryId = desserts, entries = new[] { new { menuLibraryItemId = _itemId, pricing = 2 } } } },
                mealIds = Array.Empty<string>(),
            },
        });
        Assert.That(save.Ok, Is.True, await save.TextAsync());

        await OpenAsync($"/organizations/{_orgId}/events/{RoomsEventId}/menu-builder/{_menuId}", Page.Locator("#menu-publish"));
        await ClickUntilAsync(Page.Locator("#menu-publish"), Page.Locator("#menu-editor-error"));
        await Expect(Page.Locator("#menu-editor-error")).ToContainTextAsync("Choose which meals");

        await Page.Locator($"#meal-{_mealId}").CheckAsync();
        await ClickUntilAsync(Page.Locator("#menu-publish"), Page.Locator("#menu-editor-note"));
        await Expect(Page.Locator("#menu-state")).ToHaveTextAsync("Published");

        // What guests read, signed out.
        var ev = (await (await _admin.GetAsync($"/api/organizations/{_orgId}/events/{RoomsEventId}")).JsonAsync())!.Value;
        var slug = ev.GetProperty("urlName").GetString();
        await LogoutAsync();
        await OpenAsync($"/o/paranormal365/events/{slug}", Page.Locator("[data-testid=event-menus]"));

        var menu = Page.Locator($"#menu-{_menuId} [data-testid=menu-document]");
        await Expect(menu).ToContainTextAsync($"Supper menu {_tag}");
        await Expect(menu).ToHaveAttributeAsync("data-design", "midnight-vigil");
        await Expect(Page.Locator("[data-testid=night-menus]").First).ToContainTextAsync($"Supper {_tag}");
        await Expect(Page.Locator("#event-included")).ToContainTextAsync("Included with the event");
        await Expect(Page.Locator($"#menu-{_menuId} [data-testid=event-menu-pdf]")).ToBeVisibleAsync();
    }

    [Test]
    [Description("A published menu's PDF comes from the public address, as a PDF.")]
    public async Task The_published_pdf_is_a_pdf()
    {
        var workspace = (await (await _admin.GetAsync(Builder)).JsonAsync())!.Value;
        var desserts = workspace.GetProperty("categories").EnumerateArray().First(c => c.GetProperty("name").GetString() == "Desserts").GetProperty("id").GetString();
        await _admin.PutAsync($"{Builder}/{_menuId}", new()
        {
            DataObject = new
            {
                title = $"Test menu {_tag}", throughoutTheEvent = true, designKey = "case-file", prices = Array.Empty<object>(),
                sections = new[] { new { menuCategoryId = desserts, entries = new[] { new { menuLibraryItemId = _itemId, pricing = 3, price = 4 } } } },
                mealIds = Array.Empty<string>(),
            },
        });
        var published = await _admin.PostAsync($"{Builder}/{_menuId}/publish", new() { DataObject = new { } });
        Assert.That(published.Ok, Is.True, await published.TextAsync());

        var pdf = await _api.GetAsync($"/api/public/hosted-events/{RoomsEventId}/published-menus/{_menuId}/pdf");
        Assert.That(pdf.Ok, Is.True);
        Assert.That(pdf.Headers["content-type"], Does.StartWith("application/pdf"));
        var body = await pdf.BodyAsync();
        Assert.That(System.Text.Encoding.ASCII.GetString(body, 0, 4), Is.EqualTo("%PDF"));
    }

    [Test]
    [TestCase(1280, 900)]
    [TestCase(375, 812)]
    public async Task The_editor_does_not_slide_sideways(int width, int height)
    {
        await Page.SetViewportSizeAsync(width, height);
        await OpenAsync($"/organizations/{_orgId}/events/{RoomsEventId}/menu-builder/{_menuId}", Page.Locator("#menu-save"));
        var slides = await Page.EvaluateAsync<bool>("document.documentElement.scrollWidth > document.documentElement.clientWidth + 1");
        Assert.That(slides, Is.False, $"the editor slides sideways at {width}px");
    }
}
