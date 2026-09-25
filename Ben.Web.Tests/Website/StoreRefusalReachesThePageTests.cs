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
/// <para>S3.5 adds the cart page's row, S4.11 the checkout page's and S6.3 the reviews' and favourites'.
/// Store sellers P3 moved the product editor's grid into a shared component, drawn by both the
/// admin's editor and the seller's: <see cref="ComponentSurfaces"/> holds each page to handing it a
/// field set from the API's error.</para>
/// </remarks>
public sealed class StoreRefusalReachesThePageTests
{
    public static TheoryData<string, string> Surfaces() => new()
    {
        { "Ben.Web.Website.Library/SuperAdmin/Store/AdminStoreStock.razor", "stock-refusal" },
        // A cart that failed to load must not look like an empty one (S3.5).
        { "Ben.Web.Website.Library/Store/Cart/StoreCartPage.razor", "cart-refusal" },
        // "Only 1 of … left", "Enter a 5-digit ZIP code", a declined card (S4.11).
        { "Ben.Web.Website.Library/Store/Checkout/StoreCheckoutPage.razor", "checkout-refusal" },
        // The order pages (S4.13): a list or an order that failed to load must not read as "none".
        { "Ben.Web.Website.Library/Store/Orders/StoreOrders.razor", "orders-refusal" },
        { "Ben.Web.Website.Library/Store/Orders/StoreOrderDetail.razor", "order-refusal" },
        { "Ben.Web.Website.Library/Store/Orders/StoreInvoice.razor", "invoice-refusal" },
        { "Ben.Web.Website.Library/Store/Orders/StoreOrderLookup.razor", "lookup-refusal" },
        // The order desk (S5.5): a refused ship, refund or release is said on the page.
        { "Ben.Web.Website.Library/SuperAdmin/Store/AdminStoreOrders.razor", "orders-refusal" },
        { "Ben.Web.Website.Library/SuperAdmin/Store/AdminStoreOrderDetail.razor", "order-desk-refusal" },
        // Favourites and reviews (S6.3): a refused review stays in its boxes with the reason under it.
        { "Ben.Web.Website.Library/Store/Shared/StoreReviewModal.razor", "review-refusal" },
        { "Ben.Web.Website.Library/Store/Shared/StoreReviewsBlock.razor", "reviews-refusal" },
        { "Ben.Web.Website.Library/Store/StoreFavourites.razor", "favourites-refusal" },
    };

    /// <summary>A shared component that draws a refusal it is given, and the pages that give it one.</summary>
    public static TheoryData<string, string, string, string> ComponentSurfaces() => new()
    {
        { "Ben.Web.Website.Library/Store/Editor/StoreVariantsGrid.razor", "variant-refusal", "Ben.Web.Website.Library/SuperAdmin/Store/AdminStoreProductEdit.razor", "StoreVariantsGrid" },
        { "Ben.Web.Website.Library/Store/Editor/StoreVariantsGrid.razor", "variant-refusal", "Ben.Web.Website.Library/Store/Selling/SellerItemEdit.razor", "StoreVariantsGrid" },
        { "Ben.Web.Website.Library/Store/Editor/StoreStockDialog.razor", "stock-adjust-refusal", "Ben.Web.Website.Library/SuperAdmin/Store/AdminStoreProductEdit.razor", "StoreStockDialog" },
        { "Ben.Web.Website.Library/Store/Editor/StoreStockDialog.razor", "stock-adjust-refusal", "Ben.Web.Website.Library/Store/Selling/SellerItemEdit.razor", "StoreStockDialog" },
    };

    [Theory]
    [MemberData(nameof(ComponentSurfaces))]
    public void A_shared_component_draws_the_refusal_each_page_hands_it(string component, string id, string page, string tag)
    {
        var drawn = Regex.Replace(Read(component), @"@\*.*?\*@", " ", RegexOptions.Singleline);
        Assert.Matches($@"<(\w+)[^>]*\bid=""{Regex.Escape(id)}""[^>]*>\s*@Error\s*</\1>", drawn);

        var source = Regex.Replace(Read(page), @"@\*.*?\*@", " ", RegexOptions.Singleline);
        var use = Regex.Match(source, $@"<{tag}\b[^>]*\bError=""@(_\w+)""");
        Assert.True(use.Success, $"{page} draws <{tag}> without handing it a refusal field as Error.");
        // The field is set from the API's answer: "= error" directly, or through a small setter (e => field = e).
        Assert.Matches($@"\b{Regex.Escape(use.Groups[1].Value)}\s*=\s*(error|e)\b", source);
    }

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
