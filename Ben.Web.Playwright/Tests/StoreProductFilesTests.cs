using Microsoft.Playwright;
using NUnit.Framework;

namespace Ben.Web.Playwright.Tests;

/// <summary>
/// A product's files (store sellers, backlog 251, P11): the seller keeps them on the item's Files
/// tab, each for buyers or private; a buyer finds the ones for buyers under Downloads on a paid
/// order, and a written manual opens as a printable page.
/// </summary>
/// <remarks>
/// Seeded (StoreDemoSeeder): Hazel's Hand-Built REM Pod carries "REM Pod quick start" for buyers and
/// "Build notes", private. Sarah's two-package order (…0110) has the REM pod in it.
/// </remarks>
[TestFixture]
[Category("Store")]
public class StoreProductFilesTests : BenTestBase
{
    private const string RemPod = "a1000000-0000-0000-0000-000000000028";
    private const string SarahTwoPackages = "a1000000-0000-0000-0000-000000000110";

    [Test]
    [Description("Hazel's Files tab lists her manual for buyers and her private notes; she writes another manual and it joins the list.")]
    public async Task A_seller_keeps_files_for_buyers_and_private_ones()
    {
        await LoginAsync(SellerEmail, SellerPassword);
        await Page.GotoAsync($"{BaseUrl}/store/selling/items/{RemPod}?tab=files");
        await WaitForTheCircuitAsync();

        var quickStart = Page.Locator("[data-testid=product-file][data-title='REM Pod quick start']");
        await Expect(quickStart).ToBeVisibleAsync(new() { Timeout = 30_000 });
        await Expect(quickStart.Locator("[data-testid=file-audience]")).ToHaveTextAsync("For buyers");
        await Expect(Page.Locator("[data-testid=product-file][data-title='Build notes'] [data-testid=file-audience]")).ToHaveTextAsync("Private");

        var title = $"Battery care {Guid.NewGuid().ToString("N")[..6]}";
        await FillAndConfirmAsync("#manual-title", title);
        await FillAndConfirmAsync("#manual-body", "# Batteries\n\nTake them out between investigations.");
        await Page.Locator("#manual-audience").SelectOptionAsync("Private");
        await Page.Locator("#manual-save").ClickAsync();

        var added = Page.Locator($"[data-testid=product-file][data-title='{title}']");
        await Expect(added).ToBeVisibleAsync(new() { Timeout = 15_000 });
        await Expect(added.Locator("[data-testid=file-audience]")).ToHaveTextAsync("Private");
        await Expect(Page.Locator("#files-refusal")).ToHaveCountAsync(0);

        // Tidy up: it goes again, so a rerun starts from the seeded two.
        await added.Locator("[data-testid=file-delete]").ClickAsync();
        await Expect(added).ToHaveCountAsync(0, new() { Timeout = 15_000 });
    }

    [Test]
    [Description("Sarah's paid order lists the REM pod's quick-start manual under Downloads — never the private notes — and it opens as a printable page.")]
    public async Task A_buyer_downloads_the_manual_from_their_order()
    {
        await LoginAsync(UserEmail, UserPassword);
        await Page.GotoAsync($"{BaseUrl}/store/orders/{SarahTwoPackages}");
        await WaitForTheCircuitAsync();

        var downloads = Page.Locator("[data-testid=order-downloads]");
        await Expect(downloads).ToBeVisibleAsync(new() { Timeout = 30_000 });
        await Expect(downloads).ToContainTextAsync("REM Pod quick start");
        await Expect(downloads).Not.ToContainTextAsync("Build notes");

        await Page.GotoAsync(await downloads.GetByText("REM Pod quick start").GetAttributeAsync("href") is { } href ? $"{BaseUrl}{href}" : throw new AssertionException("no link"));
        await WaitForTheCircuitAsync();
        var manual = Page.Locator("[data-testid=store-manual]");
        await Expect(manual).ToBeVisibleAsync(new() { Timeout = 30_000 });
        await Expect(manual.Locator("h1")).ToHaveTextAsync("REM Pod quick start");
        await Expect(manual).ToContainTextAsync("Before you start");
        await Expect(Page.Locator("#manual-print")).ToBeVisibleAsync();
    }

    [Test]
    [Description("A member who didn't buy it is told there's no manual here, not shown it.")]
    public async Task A_stranger_is_not_shown_the_manual()
    {
        await LoginAsync(MemberEmail, MemberPassword);
        await Page.GotoAsync($"{BaseUrl}/store/orders/{SarahTwoPackages}/manuals/a1000000-0000-0000-0028-000000000800");
        await WaitForTheCircuitAsync();
        await Expect(Page.GetByText("There's no manual here")).ToBeVisibleAsync(new() { Timeout = 30_000 });
        await Expect(Page.Locator("[data-testid=store-manual]")).ToHaveCountAsync(0);
    }
}
