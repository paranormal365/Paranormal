using Ben.Data.Common.Enums;
using Ben.Data.WebApi.Services.Store;
using Xunit;

namespace Ben.Web.Tests.Store;

/// <summary>An order's status from its packages' (store sellers, backlog 251, P7).</summary>
public sealed class StoreParcelRollupTests
{
    [Theory]
    [InlineData(new[] { StoreParcelStatus.Waiting, StoreParcelStatus.Waiting }, StoreOrderStatus.Paid)]
    [InlineData(new[] { StoreParcelStatus.Packed, StoreParcelStatus.Waiting }, StoreOrderStatus.Paid)]
    [InlineData(new[] { StoreParcelStatus.Packed, StoreParcelStatus.Packed }, StoreOrderStatus.Packed)]
    [InlineData(new[] { StoreParcelStatus.Shipped, StoreParcelStatus.Waiting }, StoreOrderStatus.PartiallyShipped)]
    [InlineData(new[] { StoreParcelStatus.Delivered, StoreParcelStatus.Packed }, StoreOrderStatus.PartiallyShipped)]
    [InlineData(new[] { StoreParcelStatus.Shipped, StoreParcelStatus.Delivered }, StoreOrderStatus.Shipped)]
    [InlineData(new[] { StoreParcelStatus.Delivered, StoreParcelStatus.Delivered }, StoreOrderStatus.Delivered)]
    [InlineData(new[] { StoreParcelStatus.Shipped }, StoreOrderStatus.Shipped)]
    public void The_order_reads_what_its_packages_say(StoreParcelStatus[] parcels, StoreOrderStatus order)
        => Assert.Equal(order, StoreParcelRollup.Status(parcels));

    [Fact]
    public void A_cancelled_package_does_not_hold_the_order_back()
    {
        Assert.Equal(StoreOrderStatus.Delivered, StoreParcelRollup.Status([StoreParcelStatus.Delivered, StoreParcelStatus.Cancelled]));
        Assert.Equal(StoreOrderStatus.Shipped, StoreParcelRollup.Status([StoreParcelStatus.Cancelled, StoreParcelStatus.Shipped]));
        Assert.Null(StoreParcelRollup.Status([StoreParcelStatus.Cancelled]));
    }
}
