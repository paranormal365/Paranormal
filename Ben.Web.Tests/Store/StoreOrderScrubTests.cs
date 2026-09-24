using Ben.Data.Common.Constants;
using Ben.Data.Common.Enums;
using Ben.Data.Source.Entities;
using Ben.Data.WebApi.Services.Store;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Ben.Web.Tests.Store;

/// <summary>
/// A closed account leaves its orders behind without its name on them — except on a parcel still
/// on its way (storefront S0.11, plan §5.8).
/// </summary>
public sealed class StoreOrderScrubTests
{
    private sealed record Fixture(SqliteTestDb Sqlite, AppUser Buyer, Guid Delivered, Guid Shipped);

    private static async Task<Fixture> BuyerWithTwoOrdersAsync()
    {
        var sqlite = await SqliteTestDb.CreateAsync();
        await using var db = await sqlite.NewContextAsync();
        var admin = StoreTestData.Person(db);
        var buyer = StoreTestData.Person(db, "buyer");
        var coupon = StoreTestData.Coupon(db, admin, perBuyer: null);
        var delivered = StoreTestData.Order(db, StoreOrderStatus.Delivered, buyer);
        var shipped = StoreTestData.Order(db, StoreOrderStatus.Shipped, buyer);
        foreach (var order in new[] { delivered, shipped })
            db.StoreCouponRedemptions.Add(new StoreCouponRedemption
            {
                Id = Guid.NewGuid(), CouponId = coupon.Id, OrderId = order.Id, BuyerAppUserId = buyer.Id,
                BuyerEmailNormalized = order.BuyerEmailNormalized, DiscountAmount = 5m, RedeemedUtc = StoreTestData.Now,
            });
        await db.SaveChangesAsync();
        return new Fixture(sqlite, buyer, delivered.Id, shipped.Id);
    }

    [Fact]
    public async Task A_finished_order_loses_the_person_and_keeps_the_tax_record()
    {
        var f = await BuyerWithTwoOrdersAsync();
        await using var _ = f.Sqlite;

        await using (var db = await f.Sqlite.NewContextAsync())
            Assert.Equal((1, 1), await StoreOrderScrub.DetachAndScrubAsync(db, f.Buyer.Id, StoreTestData.Now));

        await using var check = await f.Sqlite.NewContextAsync();
        var order = await check.StoreOrders.AsNoTracking().SingleAsync(o => o.Id == f.Delivered);
        var closed = AccountClosure.ClosedEmailFor(f.Buyer.Id);

        Assert.Null(order.BuyerAppUserId);
        Assert.Equal((AccountClosure.FormerMemberName, AccountClosure.FormerMemberName, closed, StoreEmail.Normalize(closed)),
            (order.BuyerName, order.ShipName, order.BuyerEmail, order.BuyerEmailNormalized));
        Assert.Equal((StoreOrderScrub.RemovedStreet, (string?)null, "", (string?)null),
            (order.ShipStreet1, order.ShipStreet2, order.ShipPhone, order.PlacedFromIp));
        Assert.Equal(((string?)null, (string?)null, (string?)null, (string?)null),
            (order.BillName, order.BillCompany, order.BillStreet1, order.BillStreet2));
        Assert.Equal(("Nashville", "TN", "37203"), (order.ShipCity, order.ShipState, order.ShipZip));
        Assert.Equal(59.99m, order.Total);
        Assert.Single(await check.StoreOrderEvents.Where(e => e.OrderId == f.Delivered && e.Kind == StoreOrderEventKind.Anonymised).ToListAsync());

        var redemption = await check.StoreCouponRedemptions.AsNoTracking().SingleAsync(r => r.OrderId == f.Delivered);
        Assert.Null(redemption.BuyerAppUserId);
        Assert.Equal(StoreEmail.Normalize(closed), redemption.BuyerEmailNormalized);
    }

    /// <summary>Ben, 09/24/2026: keep the address until it is delivered, then scrub.</summary>
    [Fact]
    public async Task An_order_on_its_way_keeps_its_address_until_it_arrives()
    {
        var f = await BuyerWithTwoOrdersAsync();
        await using var _ = f.Sqlite;

        await using (var db = await f.Sqlite.NewContextAsync())
            await StoreOrderScrub.DetachAndScrubAsync(db, f.Buyer.Id, StoreTestData.Now);

        await using (var check = await f.Sqlite.NewContextAsync())
        {
            var order = await check.StoreOrders.AsNoTracking().SingleAsync(o => o.Id == f.Shipped);
            Assert.Null(order.BuyerAppUserId);
            Assert.Equal(StoreTestData.Now, order.PendingAnonymisationSinceUtc);
            Assert.Equal(("13 Crossroads Lane", "Sarah Hollow", "sarah@example.com"), (order.ShipStreet1, order.ShipName, order.BuyerEmail));
            Assert.False(await check.StoreOrderEvents.AnyAsync(e => e.OrderId == f.Shipped));

            var redemption = await check.StoreCouponRedemptions.AsNoTracking().SingleAsync(r => r.OrderId == f.Shipped);
            Assert.Null(redemption.BuyerAppUserId);
        }

        // Delivered: the transition scrubs it. The account is long gone, so the order's own id
        // stands in for the closed address.
        await using (var db = await f.Sqlite.NewContextAsync())
        {
            var order = await db.StoreOrders.SingleAsync(o => o.Id == f.Shipped);
            order.Status = StoreOrderStatus.Delivered;
            await StoreOrderScrub.ScrubAsync(db, order, null, StoreTestData.Now.AddDays(3));
            await db.SaveChangesAsync();
            await StoreOrderScrub.ScrubAsync(db, order, null, StoreTestData.Now.AddDays(4));   // twice is once
            await db.SaveChangesAsync();
        }

        await using var after = await f.Sqlite.NewContextAsync();
        var scrubbed = await after.StoreOrders.AsNoTracking().SingleAsync(o => o.Id == f.Shipped);
        Assert.Equal((StoreOrderScrub.RemovedStreet, AccountClosure.ClosedEmailFor(f.Shipped), (DateTime?)null),
            (scrubbed.ShipStreet1, scrubbed.BuyerEmail, scrubbed.PendingAnonymisationSinceUtc));
        Assert.Single(await after.StoreOrderEvents.Where(e => e.OrderId == f.Shipped).ToListAsync());
    }

    [Fact]
    public async Task After_the_scrub_nothing_in_the_store_points_at_the_person()
    {
        var f = await BuyerWithTwoOrdersAsync();
        await using var _ = f.Sqlite;

        await using var db = await f.Sqlite.NewContextAsync();
        await StoreOrderScrub.DetachAndScrubAsync(db, f.Buyer.Id, StoreTestData.Now);

        Assert.False(await db.StoreOrders.AnyAsync(o => o.BuyerAppUserId == f.Buyer.Id));
        Assert.False(await db.StoreCouponRedemptions.AnyAsync(r => r.BuyerAppUserId == f.Buyer.Id));
    }
}
