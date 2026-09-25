using System.Text.RegularExpressions;
using Xunit;

namespace Ben.Web.Tests.Store;

/// <summary>
/// Nothing a shopper's screens read carries what an item costs to make, what its seller is paid, or
/// the store's markup (store sellers, backlog 251, P4 on).
/// </summary>
/// <remarks>
/// Source reads of the records the public store, the cart, checkout and a buyer's orders send. A
/// cost field added to one of them would compile, pass every test written for it, and put the
/// seller's pay on the product page.
/// </remarks>
public sealed class StorePublicRecordsCarryNoCostTests
{
    private static readonly string[] PublicFiles =
    [
        "StoreCatalogRecords.cs", "StorePublicRecords.cs", "StoreCartRecords.cs", "StoreCheckoutRecords.cs", "StoreOrderRecords.cs",
        "StoreReviewRecords.cs",
    ];

    [Fact]
    public void No_public_record_names_a_cost_a_seller_s_pay_or_a_markup()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Ben.slnx"))) dir = dir.Parent;
        var folder = Path.Combine(dir!.FullName, "Ben.Service.Models", "Store");

        var found = new List<string>();
        foreach (var file in PublicFiles)
        {
            var source = Regex.Replace(File.ReadAllText(Path.Combine(folder, file)), @"//.*|/\*.*?\*/", "", RegexOptions.Singleline);
            foreach (Match m in Regex.Matches(source, @"\b\w*(CostBasis|CostPerUnit|OtherCost|StorePart\w*|SellerAsk\w*|SellerEarning\w*|Markup|StripeFee\w*|SellerShippingCredit)\b"))
                found.Add($"{file}: {m.Value}");
        }
        Assert.True(found.Count == 0, "A shopper-facing record carries what only the seller and the store may see: " + string.Join(", ", found));
    }
}
