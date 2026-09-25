using Ben.Data.Common.Enums;
using Ben.Data.Source.Context;
using Ben.Data.WebApi.Services.Store;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Xunit;

namespace Ben.Web.Tests.Store;

/// <summary>
/// Stock cannot be oversold, over-reserved or corrected below what checkouts hold (storefront S0.11).
/// </summary>
/// <remarks>
/// On <see cref="SqliteTestDb"/>: every method under test is a conditional <c>ExecuteUpdateAsync</c>,
/// which InMemory cannot run. The races are made deterministic with <see cref="RunBeforeStatement"/>, which
/// runs a competing change at the exact moment between a statement being built and executed — the
/// window two real requests would share.
/// </remarks>
public sealed class StoreStockTests
{
    private static async Task<(SqliteTestDb Sqlite, Guid VariantId)> OneVariantAsync(
        int onHand, int reserved = 0, bool categoryActive = true, bool productActive = true, bool variantActive = true,
        params IInterceptor[] interceptors)
    {
        var sqlite = await SqliteTestDb.CreateAsync(interceptors);
        await using var db = await sqlite.NewContextAsync();
        var admin = StoreTestData.Person(db);
        var variant = StoreTestData.Variant(db, admin, onHand, reserved,
            categoryActive: categoryActive, productActive: productActive, variantActive: variantActive);
        await db.SaveChangesAsync();
        return (sqlite, variant.Id);
    }

    private static async Task<(int OnHand, int Reserved, int Movements)> ReadAsync(SqliteTestDb sqlite, Guid variantId)
    {
        await using var db = await sqlite.NewContextAsync();
        var v = await db.StoreProductVariants.AsNoTracking().SingleAsync(x => x.Id == variantId);
        return (v.StockOnHand, v.StockReserved, await db.StoreStockMovements.CountAsync(m => m.VariantId == variantId));
    }

    [Fact]
    public async Task Two_buyers_racing_for_the_last_unit_only_one_gets_it()
    {
        var race = new RunBeforeStatement("UPDATE", "StoreProductVariants");
        var (sqlite, id) = await OneVariantAsync(onHand: 1, interceptors: race);
        await using var _ = sqlite;

        bool? secondBuyer = null;
        race.Competitor = async () =>
        {
            await using var other = await sqlite.NewContextAsync();
            secondBuyer = await StoreStock.TryReserveAsync(other, id, 1);
        };

        await using var db = await sqlite.NewContextAsync();
        var firstBuyer = await StoreStock.TryReserveAsync(db, id, 1);

        Assert.True(secondBuyer, "the competitor ran first and should have had the unit");
        Assert.False(firstBuyer, "both buyers were told they had the last unit");
        Assert.Equal((1, 1, 0), await ReadAsync(sqlite, id));
    }

    [Fact]
    public async Task The_database_itself_refuses_a_hold_bigger_than_the_shelf()
    {
        var (sqlite, id) = await OneVariantAsync(onHand: 2);
        await using var _ = sqlite;
        await using var db = await sqlite.NewContextAsync();

        // Straight past the helper, as a careless future query would.
        await Assert.ThrowsAsync<SqliteException>(() => db.StoreProductVariants.Where(v => v.Id == id)
            .ExecuteUpdateAsync(s => s.SetProperty(v => v.StockReserved, 3)));
    }

    [Fact]
    public async Task A_sale_leaves_the_shelf_and_the_hold_together_and_is_written_down()
    {
        var (sqlite, id) = await OneVariantAsync(onHand: 12, reserved: 3);
        await using var _ = sqlite;
        var orderId = Guid.NewGuid();
        await using (var seed = await sqlite.NewContextAsync())
        {
            var order = StoreTestData.Order(seed);
            order.Id = orderId;
            await seed.SaveChangesAsync();
        }

        await using var db = await sqlite.NewContextAsync();
        Assert.True(await StoreStock.CommitSaleAsync(db, id, 2, orderId, StoreTestData.Now));

        Assert.Equal((10, 1, 1), await ReadAsync(sqlite, id));
        var movement = await db.StoreStockMovements.AsNoTracking().SingleAsync();
        Assert.Equal((StoreStockReason.Sold, -2, 10, orderId), (movement.Reason, movement.Delta, movement.QuantityAfter, movement.OrderId));
        Assert.Equal(2, (await db.StoreProductVariants.AsNoTracking().SingleAsync()).UnitsSold);
    }

    [Fact]
    public async Task A_sale_counts_on_the_product_too_and_says_when()
    {
        // The product's count is what the "popular" sort and the admin list read; only the
        // variant's was kept until 09/24/2026, so every product read 0.
        var (sqlite, id) = await OneVariantAsync(onHand: 12, reserved: 5);
        await using var _ = sqlite;
        var orderId = Guid.NewGuid();
        await using (var seed = await sqlite.NewContextAsync())
        {
            var order = StoreTestData.Order(seed);
            order.Id = orderId;
            await seed.SaveChangesAsync();
        }
        await using var db = await sqlite.NewContextAsync();

        Assert.True(await StoreStock.CommitSaleAsync(db, id, 2, orderId, StoreTestData.Now));
        Assert.True(await StoreStock.CommitSaleAsync(db, id, 3, orderId, StoreTestData.Now.AddHours(1)));

        var product = await db.StoreProducts.AsNoTracking().SingleAsync();
        Assert.Equal((5, StoreTestData.Now.AddHours(1)), (product.UnitsSold, product.LastSoldUtc));
    }

    [Fact]
    public async Task Taking_more_off_the_shelf_than_is_there_changes_nothing()
    {
        var (sqlite, id) = await OneVariantAsync(onHand: 12);
        await using var _ = sqlite;
        await using var db = await sqlite.NewContextAsync();

        Assert.False(await StoreStock.AdjustAsync(db, id, -20, StoreStockReason.Damaged, null, null, StoreStockTestsNow));
        Assert.Equal((12, 0, 0), await ReadAsync(sqlite, id));
    }

    /// <summary>
    /// The case that happens during a sale: six are held by open checkouts, and an admin marks
    /// eight damaged. Allowed, the CHECK would throw; the helper answers false instead.
    /// </summary>
    [Fact]
    public async Task A_correction_below_what_checkouts_hold_is_refused_without_an_exception()
    {
        var (sqlite, id) = await OneVariantAsync(onHand: 12, reserved: 6);
        await using var _ = sqlite;
        await using var db = await sqlite.NewContextAsync();

        Assert.False(await StoreStock.AdjustAsync(db, id, -8, StoreStockReason.Damaged, "dropped", null, StoreStockTestsNow));
        Assert.False(await StoreStock.SetToAsync(db, id, 5, StoreStockReason.Correction, null, null, StoreStockTestsNow));
        Assert.Equal((12, 6, 0), await ReadAsync(sqlite, id));
    }

    [Fact]
    public async Task Setting_the_count_below_the_holds_is_refused()
    {
        var (sqlite, id) = await OneVariantAsync(onHand: 5, reserved: 2);
        await using var _ = sqlite;
        await using var db = await sqlite.NewContextAsync();

        Assert.False(await StoreStock.SetToAsync(db, id, 1, StoreStockReason.Correction, null, null, StoreStockTestsNow));
        Assert.True(await StoreStock.SetToAsync(db, id, 9, StoreStockReason.Received, null, null, StoreStockTestsNow));

        Assert.Equal((9, 2, 1), await ReadAsync(sqlite, id));
        Assert.Equal(4, (await db.StoreStockMovements.AsNoTracking().SingleAsync()).Delta);
    }

    /// <summary>
    /// Two admins mark eight of twelve damaged at the same moment. One statement each, so the
    /// second sees the first's result and is refused. The naive read-then-write version, under the
    /// identical interleaving, lets both through and loses one of the writes.
    /// </summary>
    [Fact]
    public async Task Two_racing_corrections_cannot_both_take_the_same_units()
    {
        var race = new RunBeforeStatement("UPDATE", "StoreProductVariants");
        var (sqlite, id) = await OneVariantAsync(onHand: 12, interceptors: race);
        await using var _ = sqlite;

        bool? competitor = null;
        race.Competitor = async () =>
        {
            await using var other = await sqlite.NewContextAsync();
            competitor = await StoreStock.AdjustAsync(other, id, -8, StoreStockReason.Damaged, null, null, StoreStockTestsNow);
        };

        await using var db = await sqlite.NewContextAsync();
        var mine = await StoreStock.AdjustAsync(db, id, -8, StoreStockReason.Damaged, null, null, StoreStockTestsNow);

        Assert.True(competitor);
        Assert.False(mine, "sixteen units came off a shelf of twelve");
        Assert.Equal((4, 0, 1), await ReadAsync(sqlite, id));
    }

    [Fact]
    public async Task The_naive_read_then_write_does_lose_a_racing_correction()
    {
        var race = new RunBeforeSave();
        var (sqlite, id) = await OneVariantAsync(onHand: 12, interceptors: race);
        await using var _ = sqlite;

        await using var db = await sqlite.NewContextAsync();
        var variant = await db.StoreProductVariants.SingleAsync(v => v.Id == id);
        Assert.True(variant.StockOnHand - 8 >= variant.StockReserved);   // the naive check passes…

        race.Competitor = async () =>
        {
            await using var other = await sqlite.NewContextAsync();
            Assert.True(await StoreStock.AdjustAsync(other, id, -8, StoreStockReason.Damaged, null, null, StoreStockTestsNow));
        };
        variant.StockOnHand -= 8;                                          // …and the write lands on a stale read
        await db.SaveChangesAsync();

        // Sixteen units were taken off; the shelf says four. The race the conditional update closes.
        Assert.Equal(4, (await ReadAsync(sqlite, id)).OnHand);
    }

    [Fact]
    public async Task A_bulk_change_with_one_bad_line_changes_nothing_and_names_the_line()
    {
        var sqlite = await SqliteTestDb.CreateAsync();
        await using var _ = sqlite;
        Guid good, bad;
        await using (var seed = await sqlite.NewContextAsync())
        {
            var admin = StoreTestData.Person(seed);
            good = StoreTestData.Variant(seed, admin, onHand: 10, sku: "SPIRIT-BOX-1").Id;
            bad = StoreTestData.Variant(seed, admin, onHand: 4, reserved: 3, sku: "REM-POD-2").Id;
            await seed.SaveChangesAsync();
        }

        await using var db = await sqlite.NewContextAsync();
        var refusal = await StoreStock.AdjustManyAsync(db,
            [new StoreStockChange(good, 5, null), new StoreStockChange(bad, null, 1)],
            StoreStockReason.Correction, "stock take", null, StoreStockTestsNow);

        Assert.Equal("Nothing was changed — REM-POD-2 can't go below the 3 held by open checkouts.", refusal);
        Assert.Equal((10, 0, 0), await ReadAsync(sqlite, good));
        Assert.Equal((4, 3, 0), await ReadAsync(sqlite, bad));

        await using var again = await sqlite.NewContextAsync();
        Assert.Null(await StoreStock.AdjustManyAsync(again,
            [new StoreStockChange(good, 5, null), new StoreStockChange(bad, null, 6)],
            StoreStockReason.Received, null, null, StoreStockTestsNow));
        Assert.Equal((15, 0, 1), await ReadAsync(sqlite, good));
        Assert.Equal((6, 3, 1), await ReadAsync(sqlite, bad));
    }

    [Theory]
    [InlineData(false, true, true)]
    [InlineData(true, false, true)]
    [InlineData(true, true, false)]
    public async Task Nothing_hidden_can_be_reserved(bool categoryActive, bool productActive, bool variantActive)
    {
        var (sqlite, id) = await OneVariantAsync(onHand: 5, categoryActive: categoryActive,
            productActive: productActive, variantActive: variantActive);
        await using var _ = sqlite;
        await using var db = await sqlite.NewContextAsync();

        Assert.False(await StoreStock.TryReserveAsync(db, id, 1));
        Assert.Equal((5, 0, 0), await ReadAsync(sqlite, id));
    }

    [Fact]
    public async Task Releasing_the_same_hold_twice_gives_it_back_once()
    {
        var (sqlite, id) = await OneVariantAsync(onHand: 5);
        await using var _ = sqlite;
        await using var db = await sqlite.NewContextAsync();

        Assert.True(await StoreStock.TryReserveAsync(db, id, 1));
        Assert.True(await StoreStock.ReleaseReservationAsync(db, id, 1));
        Assert.False(await StoreStock.ReleaseReservationAsync(db, id, 1));
        Assert.Equal((5, 0, 0), await ReadAsync(sqlite, id));
    }

    private static readonly DateTime StoreStockTestsNow = StoreTestData.Now;
}
