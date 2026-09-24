using System.Text.RegularExpressions;
using Xunit;

namespace Ben.Web.Tests.Website;

/// <summary>
/// Where a store screen can be refused, the refusal is drawn on the page in the server's words
/// (storefront S1.11).
/// </summary>
/// <remarks>
/// <para>A per-row Save or an all-or-nothing stock save whose "no" vanishes looks exactly like one
/// that worked — the admin walks away believing the shelf says 22 when it says 12. Each row names a
/// page and the element its refusal lands in; the element must be bound to a field the page sets
/// from the API's error.</para>
///
/// <para>S3.5 adds the cart page's row and S4.11 the checkout page's.</para>
/// </remarks>
public sealed class StoreRefusalReachesThePageTests
{
    public static TheoryData<string, string> Surfaces() => new()
    {
        { "Ben.Web.Website.Library/SuperAdmin/Store/AdminStoreProductEdit.razor", "variant-refusal" },
        { "Ben.Web.Website.Library/SuperAdmin/Store/AdminStoreStock.razor", "stock-refusal" },
        // A cart that failed to load must not look like an empty one (S3.5).
        { "Ben.Web.Website.Library/Store/Cart/StoreCartPage.razor", "cart-refusal" },
        // "Only 1 of … left", "Enter a 5-digit ZIP code", a declined card (S4.11).
        { "Ben.Web.Website.Library/Store/Checkout/StoreCheckoutPage.razor", "checkout-refusal" },
    };

    private static string Read(string relative)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Ben.slnx"))) dir = dir.Parent;
        return File.ReadAllText(Path.Combine(dir!.FullName, relative));
    }

    [Theory]
    [MemberData(nameof(Surfaces))]
    public void The_refusal_is_drawn_where_the_page_says(string page, string id)
    {
        var source = Regex.Replace(Read(page), @"@\*.*?\*@", " ", RegexOptions.Singleline);

        var element = Regex.Match(source, $@"<(\w+)[^>]*\bid=""{Regex.Escape(id)}""[^>]*>\s*@(_\w+)\s*</\1>");
        Assert.True(element.Success,
            $"{page} has no element id=\"{id}\" showing a field — the refusal would have nowhere to land.");

        var field = element.Groups[2].Value;
        Assert.Matches($@"\b{Regex.Escape(field)}\s*=\s*error\b", source);
    }
}
