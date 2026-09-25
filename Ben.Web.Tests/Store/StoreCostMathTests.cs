using Ben.Data.Common.Enums;
using Ben.Service.Models.Store;
using Xunit;

namespace Ben.Web.Tests.Store;

/// <summary>What a unit costs to make (store sellers, backlog 251, P4): pack or piece, rounded once.</summary>
public sealed class StoreCostMathTests
{
    [Fact]
    public void A_pack_price_is_shared_across_its_pieces()
    {
        Assert.Equal(0.0699m, StoreCostMath.PiecePrice(StorePartPriceBasis.PerPack, 6.99m, 100));
        Assert.Equal(6.99m, StoreCostMath.PiecePrice(StorePartPriceBasis.PerPiece, 6.99m, 100));   // a piece price ignores the pack
    }

    [Fact]
    public void The_total_is_rounded_once_not_part_by_part()
    {
        // Three parts at $0.105 each: rounded one by one they'd be $0.11 × 3 = $0.33; the true
        // total is $0.315, which is $0.32.
        var parts = Enumerable.Repeat(StoreCostMath.PartCostPerUnit(StorePartPriceBasis.PerPiece, 0.105m, 1, 1m), 3).ToList();
        Assert.Equal(0.32m, StoreCostMath.CostBasis(parts, 0m));
        Assert.Equal(0.33m, parts.Sum(StoreMoney.Round));
    }

    [Fact]
    public void The_demo_sellers_rem_pod_costs_what_its_parts_say()
    {
        var parts = new[]
        {
            StoreCostMath.PartCostPerUnit(StorePartPriceBasis.PerPiece, 12.50m, 1, 1m),
            StoreCostMath.PartCostPerUnit(StorePartPriceBasis.PerPack, 4.99m, 10, 1m),
            StoreCostMath.PartCostPerUnit(StorePartPriceBasis.PerPiece, 18.00m, 1, 1m),
        };
        Assert.Equal(34.50m, StoreCostMath.CostBasis(parts, 3.50m));   // 34.499 → 34.50
    }

    [Fact]
    public void The_scarcest_counted_part_says_how_many_can_be_built()
    {
        Assert.Equal(3, StoreCostMath.Buildable([(7, 2m), (3, 1m), (null, 5m), (0, 0m)]));
        Assert.Null(StoreCostMath.Buildable([(null, 1m), (5, 0m)]));
        Assert.Equal(0, StoreCostMath.Buildable([(1, 2m)]));
    }
}
