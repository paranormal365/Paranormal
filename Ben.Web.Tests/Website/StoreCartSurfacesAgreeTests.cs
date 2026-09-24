using System.Text.RegularExpressions;
using Xunit;

namespace Ben.Web.Tests.Website;

/// <summary>
/// The cart's surfaces — the header menu, the drawer and the cart page — draw the same cart the
/// same way (storefront S3.5), and the header's cart is there for guests too.
/// </summary>
/// <remarks>
/// <para>Smarty's CART RULE made one cart appear in three places. Three copies of "what the cart
/// costs" would drift the first time one of them was touched, so each draws its lines with
/// StoreCartLine and reads the three money rows — Products, Shipping, Total before tax — from the
/// same fields of one StoreCartView, and shipping through its one ShippingText. The Playwright
/// test The_four_surfaces_agree checks the numbers on screen; this keeps the source honest
/// between runs.</para>
/// </remarks>
public sealed class StoreCartSurfacesAgreeTests
{
    private static string Read(string relative)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Ben.slnx"))) dir = dir.Parent;
        return Regex.Replace(File.ReadAllText(Path.Combine(dir!.FullName, relative)), @"@\*.*?\*@", " ", RegexOptions.Singleline);
    }

    public static TheoryData<string> Surfaces() => new()
    {
        "Ben.Web.Website/Components/Layout/BenCartButton.razor",
        "Ben.Web.Website.Library/Store/Cart/StoreCartDrawer.razor",
        "Ben.Web.Website.Library/Store/Shared/StoreOrderSummary.razor",
    };

    [Theory]
    [MemberData(nameof(Surfaces))]
    public void Every_surface_reads_the_same_three_money_rows(string surface)
    {
        var source = Read(surface);
        foreach (var testId in new[] { "summary-products", "summary-shipping", "summary-total", "summary-why-not" })
            Assert.True(source.Contains($"data-testid=\"{testId}\""), $"{surface} has no {testId} row.");
        Assert.Contains(".Subtotal", source);
        Assert.Contains(".TotalBeforeTax", source);
        Assert.Contains(".ShippingText", source);
        Assert.Contains(".WhyNotCheckout", source);
    }

    [Theory]
    [InlineData("Ben.Web.Website/Components/Layout/BenCartButton.razor")]
    [InlineData("Ben.Web.Website.Library/Store/Cart/StoreCartDrawer.razor")]
    [InlineData("Ben.Web.Website.Library/Store/Cart/StoreCartPage.razor")]
    public void Every_surface_draws_its_lines_with_StoreCartLine(string surface)
        => Assert.Matches(@"<StoreCartLine\b", Read(surface));

    [Fact]
    public void The_cart_page_uses_the_order_summary()
        => Assert.Matches(@"<StoreOrderSummary\b", Read("Ben.Web.Website.Library/Store/Cart/StoreCartPage.razor"));

    /// <summary>A guest has a cart: the button is not inside the header's signed-in branch.</summary>
    [Fact]
    public void The_cart_button_is_outside_the_signed_in_branch()
    {
        var header = Read("Ben.Web.Website/Components/Layout/BenHeader.razor");
        var at = header.IndexOf("<BenCartButton", StringComparison.Ordinal);
        Assert.True(at >= 0, "BenHeader no longer draws BenCartButton.");

        // Every @if (IsAuthenticated) block's braces: the button must fall in none of them.
        foreach (Match m in Regex.Matches(header, @"@if\s*\(\s*IsAuthenticated\s*\)\s*\{"))
        {
            var depth = 1;
            var i = m.Index + m.Length;
            for (; i < header.Length && depth > 0; i++)
                depth += header[i] == '{' ? 1 : header[i] == '}' ? -1 : 0;
            Assert.False(at > m.Index && at < i, "BenCartButton sits inside an IsAuthenticated branch — guests would have no cart.");
        }
    }

    /// <summary>The header stays feature-unaware: the button decides for itself whether the store is open.</summary>
    [Fact]
    public void The_cart_button_checks_the_store_switch_itself()
    {
        Assert.Contains("Features.IsOn(SiteFeatures.Store)", Read("Ben.Web.Website/Components/Layout/BenCartButton.razor"));
        Assert.DoesNotContain("SiteFeatures", Read("Ben.Web.Website/Components/Layout/BenHeader.razor"));
    }
}
