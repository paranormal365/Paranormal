using Ben.Data.Common.Enums;
using Ben.Data.Source.Entities;
using Ben.Data.WebApi.Services.Store;
using Xunit;

namespace Ben.Web.Tests.Store;

/// <summary>What a discount code is worth and the sentences that refuse it (storefront S0.11).</summary>
public sealed class StoreCouponMathTests
{
    private static readonly DateTime Now = StoreTestData.Now;

    private static StoreCoupon Code(
        StoreCouponKind kind = StoreCouponKind.Percent, int? percentOff = 10, decimal? amountOff = null,
        DateTime? starts = null, DateTime? ends = null, int? max = null, int count = 0, int? perBuyer = 1,
        decimal? minimum = null, bool active = true)
        => new()
        {
            Id = Guid.NewGuid(), Code = "BOO10", Name = "Boo", Kind = kind, PercentOff = percentOff, AmountOff = amountOff,
            StartsUtc = starts, EndsUtc = ends, MaxRedemptions = max, RedemptionCount = count,
            MaxRedemptionsPerBuyer = perBuyer, MinimumOrderAmount = minimum, IsActive = active,
        };

    [Fact]
    public void Each_reason_a_code_is_refused_has_its_own_sentence()
    {
        Assert.Equal("That code isn't one we recognise.", StoreCouponMath.WhyNotRedeemable(null, 50m, 0, Now));
        Assert.Equal("That code isn't one we recognise.", StoreCouponMath.WhyNotRedeemable(Code(active: false), 50m, 0, Now));
        Assert.Equal("That code has expired.", StoreCouponMath.WhyNotRedeemable(Code(ends: Now), 50m, 0, Now));
        Assert.Equal("That code isn't active yet.", StoreCouponMath.WhyNotRedeemable(Code(starts: Now.AddMinutes(1)), 50m, 0, Now));
        Assert.Equal("That code has been used as many times as it can be.",
            StoreCouponMath.WhyNotRedeemable(Code(max: 5, count: 5), 50m, 0, Now));
        Assert.Equal("That code has already been used with this email address.",
            StoreCouponMath.WhyNotRedeemable(Code(perBuyer: 1), 50m, 1, Now));
        Assert.Equal("That code needs an order of at least $50.00.",
            StoreCouponMath.WhyNotRedeemable(Code(minimum: 50m), 49.99m, 0, Now));
        Assert.Null(StoreCouponMath.WhyNotRedeemable(Code(minimum: 50m, max: 5, count: 4, perBuyer: 2), 50m, 1, Now));
    }

    [Fact]
    public void A_broken_code_is_unrecognised_to_the_buyer_and_explained_to_the_admin()
    {
        var broken = Code(percentOff: 0);
        Assert.Equal("A percent-off code takes between 1 and 100 percent.", StoreCouponMath.Misconfiguration(broken));
        Assert.Equal(StoreCouponMath.NotRecognised, StoreCouponMath.WhyNotRedeemable(broken, 50m, 0, Now));

        Assert.Equal("A dollars-off code needs an amount above $0.",
            StoreCouponMath.Misconfiguration(Code(StoreCouponKind.Fixed, percentOff: null, amountOff: 0m)));
        Assert.Equal("The code has to end after it starts.",
            StoreCouponMath.Misconfiguration(Code(starts: Now, ends: Now)));
        Assert.Null(StoreCouponMath.Misconfiguration(Code()));
    }

    [Theory]
    [InlineData(10, 59.99, 6.00)]   // 5.999 rounds up
    [InlineData(15, 33.30, 5.00)]   // 4.995 rounds half away from zero, never to even
    [InlineData(100, 12.34, 12.34)]
    public void Percent_off_rounds_to_the_cent_half_away_from_zero(int percent, double subtotal, double expected)
        => Assert.Equal((decimal)expected, StoreCouponMath.DiscountFor(Code(percentOff: percent), (decimal)subtotal));

    /// <summary>
    /// A $50 code on a $39.99 order takes the order, not the shipping: the buyer still pays
    /// shipping and tax, and the total is never negative.
    /// </summary>
    [Fact]
    public void A_fixed_code_larger_than_the_order_takes_the_order_and_not_the_shipping()
    {
        const decimal subtotal = 39.99m, shipping = 7.95m, tax = 0.70m;
        var discount = StoreCouponMath.DiscountFor(Code(StoreCouponKind.Fixed, percentOff: null, amountOff: 50m), subtotal);

        Assert.Equal(39.99m, discount);
        Assert.Equal(shipping + tax, subtotal - discount + shipping + tax);
    }

    [Theory]
    [InlineData(new[] { 0.01, 0.02, 0.03 }, 0.03)]      // 50 % of $0.06
    [InlineData(new[] { 10.00, 10.00, 10.00 }, 10.00)]
    [InlineData(new[] { 59.99, 12.50, 0.99 }, 7.35)]
    [InlineData(new[] { 5.00 }, 5.00)]
    public void Allocated_shares_add_up_to_the_discount_exactly(double[] lines, double discount)
    {
        var totals = lines.Select(l => (decimal)l).ToList();
        var shares = StoreCouponMath.Allocate(totals, (decimal)discount);

        Assert.Equal((decimal)discount, shares.Sum());
        Assert.All(shares.Zip(totals), p => Assert.InRange(p.First, 0m, p.Second));
        Assert.All(shares, s => Assert.Equal(s, Math.Round(s, 2)));
    }

    [Fact]
    public void The_rounding_remainder_goes_on_the_largest_line()
    {
        // $10 over three $10 lines is 3.33 + 3.33 + 3.34; the extra cent lands on the first largest.
        Assert.Equal([3.34m, 3.33m, 3.33m], StoreCouponMath.Allocate([10m, 10m, 10m], 10m));
        Assert.Equal([0.33m, 3.34m, 3.33m], StoreCouponMath.Allocate([1m, 10m, 10m], 7m));
    }
}
