using Microsoft.Playwright;
using NUnit.Framework;

namespace Ben.Web.Playwright.Tests;

/// <summary>
/// The equipment subsystem, which had no coverage at all — seven routes and one passing mention
/// across the whole suite.
/// </summary>
/// <remarks>
/// The item is created through the UI rather than seeded. The dev data contains no equipment
/// items, so <c>/equipment/{ItemId}</c> and <c>/my-checkouts</c> had nothing to render and were
/// among the routes the crawl had to skip. Creating one exercises the cascade — category, then
/// make, then model, each narrowing the next — which is the part most likely to break quietly.
/// </remarks>
[TestFixture]
[Category("Equipment")]
public class EquipmentTests : BenTestBase
{
    [Test]
    public async Task Catalog_RendersForAVisitor()
    {
        // Public: no sign-in on purpose. The catalogue is readable by a visitor, and that is the
        // path least likely to be exercised by anyone working on the app while signed in.
        await Page.GotoAsync($"{BaseUrl}/equipment-catalog");
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);
        await WaitUntilLoadedAsync();

        var body = await Main.InnerTextAsync();
        Assert.That(body, Does.Not.Contain("An unhandled error has occurred"));
        Assert.That(body, Does.Not.Contain("Page not found"));

        // The catalogue lists gear people have chosen to share, and the dev data has none — so
        // the empty state is the correct result here, and asserting it is real coverage rather
        // than a skip that quietly tests nothing.
        Assert.That(body.Trim().Length, Is.GreaterThan(20), "the catalogue rendered nothing at all");
    }

    [Test]
    public async Task ModelPage_RendersForAVisitor()
    {
        var login = await Page.APIRequest.PostAsync($"{ApiUrl}/login",
            new() { DataObject = new { email = SuperAdminEmail, password = SuperAdminPassword } });
        var token = (await login.JsonAsync())?.GetProperty("accessToken").GetString() ?? "";

        var models = await Page.APIRequest.GetAsync($"{ApiUrl}/api/equipment-catalog/models",
            new() { Headers = new Dictionary<string, string> { ["Authorization"] = $"Bearer {token}" } });
        var json = await models.JsonAsync();

        string? modelId = null;
        foreach (var m in json!.Value.EnumerateArray())
            if (m.TryGetProperty("id", out var v)) { modelId = v.GetString(); break; }

        if (modelId is null) Assert.Ignore("no models in the equipment taxonomy");

        await Page.GotoAsync($"{BaseUrl}/equipment-models/{modelId}");
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);
        await WaitUntilLoadedAsync();

        var body = await Main.InnerTextAsync();
        Assert.That(body, Does.Not.Contain("Page not found"), "the model page did not resolve");
        Assert.That(body, Does.Not.Contain("An unhandled error has occurred"));
        Assert.That(body.Trim().Length, Is.GreaterThan(40), "the model page rendered almost nothing");
    }

    [Test]
    public async Task AddingAPieceOfGear_ShowsItInTheListAndOnItsOwnPage()
    {
        await LoginAsync(UserEmail, UserPassword);
        await Page.GotoAsync($"{BaseUrl}/my-equipment");
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);
        await WaitUntilLoadedAsync();

        var name = $"Playwright recorder {Guid.NewGuid().ToString("N")[..8]}";

        // The editor is an inline card, not a modal — long forms go in the page here. So the thing
        // to wait for is its first field appearing, not a dialog.
        var editor = Main.Locator(".card").Filter(new() { HasTextString = "What kind of gear is it?" }).First;
        var add = Main.GetByRole(AriaRole.Button, new() { Name = "Add equipment" })
                      .Or(Main.GetByRole(AriaRole.Button, new() { Name = "Add your first piece" }))
                      .First;
        await ClickUntilAsync(add, editor);

        // The cascade: each select only populates once the one above it has a value, so these are
        // deliberately sequential rather than filled in one go.
        var selects = editor.Locator("select.form-select");
        await Expect(selects.First).ToBeVisibleAsync(new() { Timeout = 8_000 });

        // Most categories have no makes in the dev data, so taking the first one skipped every
        // run. Walk them until one leads somewhere — that is what a person would do too.
        // Polled, not read once: the select is VISIBLE before its async fetch fills it, and
        // under full-suite load that window stretches past a single read — the taxonomy looked
        // empty when only the request was still in flight.
        string[] categories = [];
        for (var poll = 0; poll < 20 && categories.Length == 0; poll++)
        {
            categories = await selects.Nth(0).Locator("option").EvaluateAllAsync<string[]>(
                "options => options.map(o => o.value).filter(v => v !== '')");
            if (categories.Length == 0) await Page.WaitForTimeoutAsync(500);
        }
        Assert.That(categories, Is.Not.Empty, "no equipment categories in the taxonomy");

        // Walks makes as well as categories. The make list is not filtered by category — only the
        // model list is — so a make can be perfectly valid and still have no model in the category
        // chosen above it. Trying only the first make per category passed for as long as
        // "Generic / Unbranded" was the only make, because it has a model in every category; the
        // first real makes in the seed data broke that assumption immediately.
        var reached = false;
        foreach (var category in categories)
        {
            await selects.Nth(0).SelectOptionAsync(category);

            var makes = await OptionValuesAsync(selects.Nth(1));
            foreach (var make in makes)
            {
                await selects.Nth(1).SelectOptionAsync(make);
                if (!await PickFirstRealOptionAsync(selects.Nth(2))) continue;   // no models here
                reached = true;
                break;
            }

            if (reached) break;
        }

        Assert.That(reached, Is.True,
            "no category in the taxonomy leads to a make and a model, so gear cannot be added at all");

        // By its real label. "Name" matched the "Who can see it" checkbox instead — a loose label
        // match will happily land on a control of a completely different type.
        await editor.GetByLabel("What do you call it?", new() { Exact = false }).First.FillAsync(name);

        var save = editor.GetByRole(AriaRole.Button, new() { Name = "Save", Exact = true });
        await Expect(save).ToBeEnabledAsync(new() { Timeout = 8_000 });
        await save.ClickAsync();
        await Expect(editor).ToBeHiddenAsync(new() { Timeout = 15_000 });

        // It should now be listed…
        await Expect(Main.GetByText(name, new() { Exact = false }).First)
            .ToBeVisibleAsync(new() { Timeout = 15_000 });

        // …and reachable at its own route, which had nothing to render before this.
        var itemLink = Main.Locator("a[href*='/equipment/']").First;
        if (await itemLink.CountAsync() > 0)
        {
            await ClickUntilUrlAsync(itemLink, "/equipment/");
            await WaitUntilLoadedAsync();

            var detail = await Main.InnerTextAsync();
            Assert.That(detail, Does.Not.Contain("Page not found"));
            Assert.That(detail, Does.Not.Contain("An unhandled error has occurred"));
        }

        await DeleteGearAsync(name);
    }

    /// <summary>
    /// Removes gear this fixture created.
    /// </summary>
    /// <remarks>
    /// These run against shared dev data, and without this every run left another "Playwright
    /// recorder …" in Sarah's equipment for good. A test that quietly accumulates rows makes the
    /// data it depends on less and less like the data a person would have.
    /// </remarks>
    private async Task DeleteGearAsync(string name)
    {
        await Page.GotoAsync($"{BaseUrl}/my-equipment");
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);
        await WaitUntilLoadedAsync();

        var row = Main.Locator(".card").Filter(new() { HasTextString = name }).First;
        if (await row.CountAsync() == 0) return;

        var confirm = Page.Locator(".modal.show");
        await ClickUntilAsync(row.GetByRole(AriaRole.Button, new() { Name = "Delete" }).First, confirm);
        await confirm.GetByRole(AriaRole.Button, new() { Name = "Delete", Exact = true }).First.ClickAsync();

        await Expect(Main.GetByText(name, new() { Exact = false })).ToHaveCountAsync(0,
            new() { Timeout = 15_000 });
    }

    /// <summary>
    /// Chooses the first option that is not the "Choose a …" placeholder. Returns false when the
    /// list has nothing but the placeholder, so the caller can skip rather than fail on absent
    /// taxonomy data.
    /// </summary>
    /// <summary>The real (non-placeholder) option values of a select, once it is enabled.</summary>
    private async Task<string[]> OptionValuesAsync(ILocator select)
    {
        await Expect(select).ToBeEnabledAsync(new() { Timeout = 8_000 });

        return await select.Locator("option").EvaluateAllAsync<string[]>(
            "options => options.map(o => o.value).filter(v => v !== '')");
    }

    private async Task<bool> PickFirstRealOptionAsync(ILocator select)
    {
        // Polled, for the same reason the category select is: the options arrive from an async
        // fetch after the select is already visible, and under full-suite load that window
        // stretches past a single read. Read-once here made every category×make pair look
        // model-less while the API was serving models for all of them — the walk exhausted the
        // whole taxonomy and reported that gear could not be added at all.
        var values = Array.Empty<string>();
        for (var poll = 0; poll < 10 && values.Length == 0; poll++)
        {
            values = await OptionValuesAsync(select);
            if (values.Length == 0) await Page.WaitForTimeoutAsync(300);
        }
        if (values.Length == 0) return false;

        await select.SelectOptionAsync(values[0]);
        return true;
    }

    [Test]
    public async Task MyCheckoutsAndGearQuestions_RenderForASignedInUser()
    {
        await LoginAsync(UserEmail, UserPassword);

        foreach (var (path, what) in new[]
                 {
                     ("/my-checkouts", "checkouts"),
                     ("/my-equipment/questions", "gear questions"),
                 })
        {
            await Page.GotoAsync($"{BaseUrl}{path}");
            await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);
            await WaitUntilLoadedAsync();

            var body = await Main.InnerTextAsync();
            Assert.That(body, Does.Not.Contain("An unhandled error has occurred"), $"{what} errored");
            Assert.That(body, Does.Not.Contain("Page not found"), $"{what} is not routed");
            Assert.That(body.Trim().Length, Is.GreaterThan(20), $"{what} rendered almost nothing");
        }
    }
}
