using Ben.Service.Models.Store;
using Xunit;

namespace Ben.Web.Tests.Store;

/// <summary>Cents are rounded one way, everywhere (storefront S0.11).</summary>
public sealed class StoreMoneyTests
{
    [Theory]
    [InlineData(0.005, 1)]
    [InlineData(-0.005, -1)]
    [InlineData(0.004, 0)]
    [InlineData(19.99, 1999)]
    [InlineData(1234.565, 123457)]
    public void Cents_round_half_away_from_zero(double dollars, long cents)
        => Assert.Equal(cents, StoreMoney.Cents((decimal)dollars));

    /// <summary>
    /// The amount sent to Stripe is the order's total in cents, and it is the same number as the
    /// sum of its parts in cents — so a PaymentIntent can never disagree with the order by a penny.
    /// </summary>
    [Theory]
    [InlineData(59.99, 6.00, 7.95, 4.94)]
    [InlineData(39.99, 39.99, 7.95, 0.70)]
    [InlineData(0.03, 0.01, 0.00, 0.00)]
    public void A_total_in_cents_is_the_sum_of_its_parts_in_cents(double subtotal, double discount, double shipping, double tax)
    {
        var total = (decimal)subtotal - (decimal)discount + (decimal)shipping + (decimal)tax;
        Assert.Equal(StoreMoney.Cents(total),
            StoreMoney.Cents((decimal)subtotal) - StoreMoney.Cents((decimal)discount)
          + StoreMoney.Cents((decimal)shipping) + StoreMoney.Cents((decimal)tax));
    }

    [Fact]
    public void Amounts_are_written_in_dollars_with_two_places_whatever_the_server_culture()
    {
        var culture = Thread.CurrentThread.CurrentCulture;
        try
        {
            Thread.CurrentThread.CurrentCulture = System.Globalization.CultureInfo.GetCultureInfo("de-DE");
            Assert.Equal("$1,234.50", StoreMoney.Format(1234.5m));
            Assert.Equal("$0.00", StoreMoney.Format(0m));
        }
        finally
        {
            Thread.CurrentThread.CurrentCulture = culture;
        }
    }
}

/// <summary>Where the store ships (storefront S0.11).</summary>
public sealed class UsStatesTests
{
    [Fact]
    public void Fifty_states_and_the_district()
    {
        Assert.Equal(51, UsStates.All.Count);
        Assert.Equal(51, UsStates.All.Select(s => s.Code).Distinct().Count());
        Assert.All(UsStates.All, s => Assert.Matches("^[A-Z]{2}$", s.Code));
        Assert.Contains(UsStates.All, s => s.Code == "DC");
    }

    [Theory]
    [InlineData("TN", "TN")]
    [InlineData(" tn ", "TN")]
    [InlineData("Tennessee", null)]
    [InlineData("PR", null)]   // territories are not shipped to in v1
    [InlineData("", null)]
    [InlineData(null, null)]
    public void A_typed_state_is_normalised_or_refused(string? typed, string? expected)
    {
        Assert.Equal(expected, UsStates.Normalize(typed));
        Assert.Equal(expected is not null, UsStates.IsValid(typed));
    }

    [Fact]
    public void A_code_has_its_name()
        => Assert.Equal("District of Columbia", UsStates.NameOf("dc"));
}

/// <summary>Tracking links are built, never typed (storefront S0.11).</summary>
public sealed class StoreCarriersTests
{
    [Fact]
    public void Every_named_carrier_links_to_its_own_https_tracking_page()
    {
        foreach (var carrier in StoreCarriers.All.Where(c => c != StoreCarriers.Other))
        {
            var url = StoreCarriers.TrackingUrl(carrier, "1Z999AA10123456784");
            Assert.NotNull(url);
            Assert.StartsWith("https://", url);
            Assert.EndsWith("1Z999AA10123456784", url);
        }
    }

    [Fact]
    public void A_tracking_number_cannot_break_out_of_the_link()
        => Assert.Equal("https://www.ups.com/track?tracknum=1Z%20999%26evil%3D1",
            StoreCarriers.TrackingUrl(StoreCarriers.Ups, " 1Z 999&evil=1 "));

    [Theory]
    [InlineData("Other", "123")]
    [InlineData("Royal Mail", "123")]
    [InlineData("UPS", "  ")]
    [InlineData("UPS", null)]
    [InlineData(null, "123")]
    public void No_link_without_a_known_carrier_and_a_number(string? carrier, string? number)
        => Assert.Null(StoreCarriers.TrackingUrl(carrier, number));
}
