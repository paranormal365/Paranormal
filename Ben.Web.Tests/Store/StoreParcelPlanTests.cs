using Ben.Data.WebApi.Services.Store;
using Xunit;

namespace Ben.Web.Tests.Store;

/// <summary>A cart as packages (store sellers, P0): flat rate per package, free per package after its share of the code.</summary>
public sealed class StoreParcelPlanTests
{
    private static readonly Guid Hazel = new("00000000-0000-0000-0000-00000000000a");
    private static readonly Guid Ivan = new("00000000-0000-0000-0000-00000000000b");

    private static Guid V(int n) => new($"10000000-0000-0000-0000-{n:D12}");

    [Fact]
    public void The_sites_own_stock_is_one_package_priced_as_the_store_always_has()
    {
        var plan = StoreParcelPlan.Build([new(V(1), null, 40m), new(V(2), null, 20m)], 0m, 7.95m, 75m);

        var only = Assert.Single(plan.Parcels);
        Assert.Equal((1, (Guid?)null, 60m, 7.95m, false, (decimal?)15m), (only.Number, only.SellerAppUserId, only.ItemsSubtotal, only.Shipping, only.IsFree, only.MoreForFree));
        Assert.Equal(7.95m, plan.Shipping);
    }

    [Fact]
    public void Two_sellers_are_two_packages_each_paying_the_flat_rate_site_first()
    {
        var plan = StoreParcelPlan.Build([new(V(3), Hazel, 30m), new(V(1), null, 40m), new(V(2), Hazel, 10m)], 0m, 7.95m, 75m);

        Assert.Equal([null, Hazel], plan.Parcels.Select(p => p.SellerAppUserId));
        Assert.Equal([1, 2], plan.Parcels.Select(p => p.Number));
        Assert.Equal([40m, 40m], plan.Parcels.Select(p => p.ItemsSubtotal));
        Assert.Equal(15.90m, plan.Shipping);
        Assert.Equal([V(2), V(3)], plan.Parcels[1].VariantIds);
    }

    [Fact]
    public void Each_package_ships_free_on_its_own_items_after_its_share_of_the_code()
    {
        // $100 from the site and $50 from Hazel, 10% off: the site's $90 clears $75, Hazel's $45 does not.
        var plan = StoreParcelPlan.Build([new(V(1), null, 100m), new(V(2), Hazel, 50m)], 15m, 7.95m, 75m);

        Assert.Equal((true, 0m, 10m), (plan.Parcels[0].IsFree, plan.Parcels[0].Shipping, plan.Parcels[0].Discount));
        Assert.Equal((false, 7.95m, 5m, (decimal?)30m), (plan.Parcels[1].IsFree, plan.Parcels[1].Shipping, plan.Parcels[1].Discount, plan.Parcels[1].MoreForFree));
        Assert.False(plan.AllFree);
    }

    [Fact]
    public void The_threshold_is_met_to_the_cent_after_the_discount()
    {
        var at = StoreParcelPlan.Build([new(V(1), Hazel, 85m)], 10m, 7.95m, 75m);
        var under = StoreParcelPlan.Build([new(V(1), Hazel, 85m)], 10.01m, 7.95m, 75m);

        Assert.True(at.Parcels[0].IsFree);
        Assert.Equal((false, (decimal?)0.01m), (under.Parcels[0].IsFree, under.Parcels[0].MoreForFree));
    }

    [Fact]
    public void The_order_lines_arrive_in_does_not_change_the_answer()
    {
        // Three equal lines and a code whose split leaves a cent over: the cent lands on the first
        // of equals by variant id whichever order the lines came in, so the packages agree.
        StoreParcelLine[] lines = [new(V(3), Ivan, 25m), new(V(1), Hazel, 25m), new(V(2), null, 25m)];
        var a = StoreParcelPlan.Build(lines, 10m, 7.95m, 21.67m);
        var b = StoreParcelPlan.Build(lines.Reverse(), 10m, 7.95m, 21.67m);

        Assert.Equal(a.Parcels.Select(p => (p.Number, p.Discount, p.Shipping)), b.Parcels.Select(p => (p.Number, p.Discount, p.Shipping)));
        Assert.Equal(10m, a.Parcels.Sum(p => p.Discount));
        // Hazel's line (the first by variant id) takes the extra cent, so hers alone misses $21.67.
        Assert.Equal([true, false, true], a.Parcels.Select(p => p.IsFree));
    }

    [Fact]
    public void No_flat_rate_means_every_package_ships_free_and_no_threshold_means_none_do()
    {
        var noRate = StoreParcelPlan.Build([new(V(1), null, 5m), new(V(2), Hazel, 5m)], 0m, 0m, 75m);
        var noThreshold = StoreParcelPlan.Build([new(V(1), null, 500m)], 0m, 7.95m, 0m);

        Assert.True(noRate.AllFree);
        Assert.Equal((false, 7.95m, (decimal?)null), (noThreshold.Parcels[0].IsFree, noThreshold.Parcels[0].Shipping, noThreshold.Parcels[0].MoreForFree));
    }

    [Fact]
    public void Shipping_tax_is_shared_by_shipping_to_the_cent()
    {
        var split = StoreParcelPlan.SplitShippingTax([7.95m, 7.95m, 0m], 1.47m);

        Assert.Equal(1.47m, split.Sum());
        Assert.Equal(0m, split[2]);
        Assert.Equal([0m, 0m], StoreParcelPlan.SplitShippingTax([0m, 0m], 0m));
    }
}
