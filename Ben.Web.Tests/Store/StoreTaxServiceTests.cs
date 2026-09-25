using Ben.Data.WebApi.Services.Store;
using Stripe;
using Xunit;

namespace Ben.Web.Tests.Store;

/// <summary>
/// Sales tax through Stripe Tax (storefront S4.3). The options are inspected as built — they
/// compile against Stripe.net 52.4.0, which is the proof the names are right — and refusals are
/// classified the way the checkout answers them.
/// </summary>
public sealed class StoreTaxServiceTests
{
    private static readonly StoreTaxAddress Nashville = new("13 Crossroads Lane", null, "Nashville", "TN", "37203");
    private static readonly StoreTaxAddress Warehouse = new("1 Depot Road", null, "Franklin", "TN", "37064");

    /// <summary>GHOST10 on the $59.99 K-II: Stripe is sent $53.99, the line net of its discount.</summary>
    [Fact]
    public void A_coupon_lowers_the_taxable_amount_sent_to_Stripe()
    {
        var options = StripeTaxService.BuildCalculationOptions(new StoreTaxRequest(
            [new StoreTaxLine("KII-EMF", 5399, 1, StripeTaxService.DefaultTaxCode)], 795, Nashville, Warehouse));

        var line = Assert.Single(options.LineItems);
        Assert.Equal(5399, line.Amount);
        Assert.Equal("exclusive", line.TaxBehavior);
        Assert.Equal(795, options.ShippingCost.Amount);
        Assert.Equal("shipping", options.CustomerDetails.AddressSource);
        Assert.Equal("37064", options.ShipFromDetails.Address.PostalCode);
    }

    private static StripeException Refusal(string? code, string type)
        => new(System.Net.HttpStatusCode.BadRequest, new StripeError { Code = code, Type = type, Message = "no" }, "no");

    [Fact]
    public void A_bad_buyer_address_is_the_buyers_problem_not_a_configuration_alert()
    {
        var e = StripeTaxService.Classify(Refusal("customer_tax_location_invalid", "invalid_request_error"), "TN");
        Assert.Equal(StoreTaxFailure.BuyerAddress, e.Failure);
        Assert.Equal("We couldn't match that ZIP code to TN — check the address.", e.Message);
    }

    [Theory]
    [InlineData("tax_settings_incomplete", "invalid_request_error", StoreTaxFailure.Configuration)]
    [InlineData(null, "api_connection_error", StoreTaxFailure.Transient)]
    [InlineData("rate_limit", "rate_limit_error", StoreTaxFailure.Transient)]
    public void Other_refusals_are_the_stores_setup_or_worth_a_retry(string? code, string type, StoreTaxFailure expected)
        => Assert.Equal(expected, StripeTaxService.Classify(Refusal(code, type), "TN").Failure);

    /// <summary>Σ line tax + shipping tax == order tax — the identity the order row and the invoice rely on.</summary>
    [Fact]
    public async Task The_fake_adds_up_to_the_cent_across_many_lines()
    {
        var tax = new FakeStoreTaxService();
        var lines = Enumerable.Range(1, 12).Select(i => new StoreTaxLine($"L{i}", 1999 + i * 37, 1, StripeTaxService.DefaultTaxCode)).ToList();

        var result = await tax.CalculateAsync(new StoreTaxRequest(lines, 795, Nashville, Warehouse));

        Assert.Equal(12, result.LineTaxCents.Count);
        Assert.Equal(result.TaxCents, result.LineTaxCents.Values.Sum() + result.ShippingTaxCents);
        Assert.Equal(result, await tax.GetAsync(result.CalculationId));
    }

    [Fact]
    public void The_fake_needs_a_ship_from_address_like_the_real_one()
    {
        var none = new StoreSettingsSnapshot(true, true, 0m, 0m, 3, new StoreShipFrom("", "", "", ""), false, null, 30, 15, false);
        Assert.False(new FakeStoreTaxService().IsAvailable(none));
        Assert.True(new FakeStoreTaxService().IsAvailable(none with { HasShipFrom = true }));
    }

    /// <summary>The store never asks the subscription side's rates.</summary>
    [Fact]
    public void No_TaxResolver_is_consulted()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (!System.IO.File.Exists(Path.Combine(dir!.FullName, "Ben.slnx"))) dir = dir.Parent;
        foreach (var file in Directory.EnumerateFiles(Path.Combine(dir.FullName, "Ben.Data.WebApi", "Services", "Store"), "*.cs"))
        {
            // Code, not prose: a use of the type (a call, a generic, a field), never the word in a comment.
            var code = System.Text.RegularExpressions.Regex.Replace(System.IO.File.ReadAllText(file), @"//[^\n]*", "");
            Assert.DoesNotMatch(@"\bI?TaxResolver\b|\bTaxRateRule\b", code);
        }
    }
}
