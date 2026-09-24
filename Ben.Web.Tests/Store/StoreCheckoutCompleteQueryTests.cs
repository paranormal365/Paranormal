using Ben.Web.Website.Library.Store.Checkout;
using Xunit;

namespace Ben.Web.Tests.Store;

/// <summary>What the thank-you page reads from its address (storefront S4.11).</summary>
public sealed class StoreCheckoutCompleteQueryTests
{
    private static readonly Guid Order = Guid.Parse("7d1c0a52-5f1e-4b8e-9a53-2f0f7b3c9e11");

    [Fact]
    public void Our_own_return_address_names_the_order()
        => Assert.Equal(new StoreCheckoutCompleteQuery(Order, false),
            StoreCheckoutCompleteQuery.Parse($"https://ishaunted.test/store/checkout/complete?order={Order}"));

    [Fact]
    public void Stripes_additions_are_read_only_for_a_failure()
    {
        var back = StoreCheckoutCompleteQuery.Parse(
            $"/store/checkout/complete?order={Order}&payment_intent=pi_1&payment_intent_client_secret=pi_1_secret_x&redirect_status=failed");
        Assert.Equal(new StoreCheckoutCompleteQuery(Order, true), back);
        Assert.False(StoreCheckoutCompleteQuery.Parse($"/store/checkout/complete?order={Order}&redirect_status=succeeded").StripeSaysFailed);
    }

    [Theory]
    [InlineData("/store/checkout/complete")]
    [InlineData("/store/checkout/complete?order=")]
    [InlineData("/store/checkout/complete?order=100042")]
    [InlineData("/store/checkout/complete?order=00000000-0000-0000-0000-000000000000")]
    public void No_order_means_no_order(string uri) => Assert.Null(StoreCheckoutCompleteQuery.Parse(uri).OrderId);

    [Theory]
    [InlineData("/store/orders/abc?t=tok_123", "tok_123")]
    [InlineData("/store/orders/abc", null)]
    [InlineData(null, null)]
    public void The_order_address_gives_up_its_token(string? orderUrl, string? token)
        => Assert.Equal(token, StoreCheckoutCompleteQuery.TokenIn(orderUrl));

    [Fact]
    public void The_page_waits_about_a_minute_easing_off()
    {
        var total = StoreCheckoutCompleteQuery.Backoff.Aggregate(TimeSpan.Zero, (a, b) => a + b);
        Assert.InRange(total.TotalSeconds, 45, 90);
        Assert.True(StoreCheckoutCompleteQuery.Backoff.Zip(StoreCheckoutCompleteQuery.Backoff.Skip(1)).All(p => p.First <= p.Second));
    }
}
