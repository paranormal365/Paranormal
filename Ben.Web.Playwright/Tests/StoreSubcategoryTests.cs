using System.Text.Json;
using Microsoft.Playwright;
using NUnit.Framework;

namespace Ben.Web.Playwright.Tests;

/// <summary>
/// Subcategories on the store (Ben, 09/24): a subcategory sits under its category in the shelf
/// list; a shelf reaches shoppers only when something is on sale under it; an empty one answers
/// "Page not found" at its address. Arranged through the SuperAdmin's API, looked at as a visitor.
/// </summary>
[TestFixture]
[Category("Store")]
public class StoreSubcategoryTests : BenTestBase
{
    private static string Unique(string name) => $"{name} {Guid.NewGuid().ToString("N")[..6]}";

    private static async Task<JsonElement> CategoryAsync(StoreTestApi api, string name, Guid? parent = null)
        => await api.SendAsync(HttpMethod.Post, "/api/admin/store/categories",
            new { name, slug = (string?)null, description = (string?)null, isActive = true, isNew = false, parentCategoryId = parent });

    [Test]
    [Description("A stocked subcategory is listed under its category; its empty sibling is not, and its address is 'Page not found'.")]
    public async Task A_subcategory_shows_under_its_category_only_when_something_is_on_sale()
    {
        using var api = await StoreTestApi.OpenAsync();
        var parent = await CategoryAsync(api, Unique("Cameras"));
        var parentId = parent.GetProperty("id").GetGuid();
        var stocked = await CategoryAsync(api, Unique("Full spectrum"), parentId);
        var empty = await CategoryAsync(api, Unique("Thermal"), parentId);
        var product = await api.ProductAsync(stocked.GetProperty("id").GetGuid(), Unique("FS camera"), live: true, price: 149m);
        await api.SetStockAsync(product, 4);

        await Page.GotoAsync($"{BaseUrl}/store/products");
        await WaitForTheCircuitAsync();
        var tree = Page.Locator("nav[aria-label=Categories]").First;
        var parentLink = tree.Locator($"a[href='/store/c/{parent.GetProperty("slug").GetString()}']");
        var childItem = tree.Locator("[data-testid=subcategory-link]").Filter(new() { HasText = stocked.GetProperty("name").GetString()! });
        await Expect(parentLink).ToBeVisibleAsync(new() { Timeout = 30_000 });
        await Expect(childItem).ToBeVisibleAsync();
        await Expect(tree.GetByText(empty.GetProperty("name").GetString()!)).ToHaveCountAsync(0);

        // The category's own shelf holds its subcategory's product.
        await parentLink.ClickAsync();
        await Expect(Page.Locator($"[data-testid=store-card][data-slug='{product.GetProperty("slug").GetString()}']")).ToBeVisibleAsync(new() { Timeout = 30_000 });

        // The product's breadcrumb walks down through both.
        await Page.GotoAsync($"{BaseUrl}/store/p/{product.GetProperty("slug").GetString()}");
        var crumbs = Page.Locator("nav[aria-label=breadcrumb]").First;
        await Expect(crumbs).ToContainTextAsync(parent.GetProperty("name").GetString()!, new() { Timeout = 30_000 });
        await Expect(crumbs).ToContainTextAsync(stocked.GetProperty("name").GetString()!);

        // The empty subcategory is for sellers, not shoppers.
        await Page.GotoAsync($"{BaseUrl}/store/c/{empty.GetProperty("slug").GetString()}");
        await Expect(Page.Locator("[data-testid=store-not-found]")).ToBeVisibleAsync(new() { Timeout = 30_000 });
    }

    [Test]
    [Description("The SuperAdmin's Categories page lists a subcategory indented under its category, and a category with subcategories cannot be put under another.")]
    public async Task The_admin_page_reads_as_a_tree()
    {
        using var api = await StoreTestApi.OpenAsync();
        var parent = await CategoryAsync(api, Unique("Recorders"));
        var parentName = parent.GetProperty("name").GetString()!;
        var child = await CategoryAsync(api, Unique("Handheld"), parent.GetProperty("id").GetGuid());

        await LoginAsync(SuperAdminEmail, SuperAdminPassword);
        await Page.GotoAsync($"{BaseUrl}/admin/store/categories");
        var childRow = Page.Locator("[data-testid=subcategory-row]").Filter(new() { HasText = child.GetProperty("name").GetString()! });
        await Expect(childRow).ToContainTextAsync($"In {parentName}", new() { Timeout = 30_000 });

        var parentRow = Page.Locator("[data-testid=category-row]").Filter(new() { HasText = parentName });
        await parentRow.GetByRole(AriaRole.Button, new() { Name = "Edit" }).ClickAsync();
        await Expect(Page.Locator("#category-parent")).ToBeDisabledAsync(new() { Timeout = 15_000 });
        await Page.ScreenshotAsync(new() { Path = Path.Combine(Path.GetTempPath(), "store-admin-subcategories.png") });
    }
}
