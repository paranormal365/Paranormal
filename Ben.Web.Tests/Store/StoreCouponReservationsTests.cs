using Ben.Data.Source.Entities;
using Ben.Data.WebApi.Services.Store;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Ben.Web.Tests.Store;

/// <summary>
/// A discount code is held when the order is placed and given back exactly once (storefront S0.11).
/// </summary>
public sealed class StoreCouponReservationsTests
{
    private static async Task<(SqliteTestDb Sqlite, StoreCoupon Coupon, AppUser Buyer)> CodeAsync(
        int? maxRedemptions = null, int? perBuyer = 1, params Microsoft.EntityFrameworkCore.Diagnostics.IInterceptor[] interceptors)
    {
        var sqlite = await SqliteTestDb.CreateAsync(interceptors);
        await using var db = await sqlite.NewContextAsync();
        var admin = StoreTestData.Person(db);
        var buyer = StoreTestData.Person(db, "buyer");
        var coupon = StoreTestData.Coupon(db, admin, maxRedemptions: maxRedemptions, perBuyer: perBuyer);
        await db.SaveChangesAsync();
        return (sqlite, coupon, buyer);
    }

    private static async Task<StoreOrder> PlacedAsync(SqliteTestDb sqlite, string email, AppUser? buyer = null)
    {
        await using var db = await sqlite.NewContextAsync();
        var order = StoreTestData.Order(db, buyer: buyer, email: email);
        await db.SaveChangesAsync();
        return order;
    }

    private static async Task<(int Count, int Rows)> ReadAsync(SqliteTestDb sqlite, Guid couponId)
    {
        await using var db = await sqlite.NewContextAsync();
        return ((await db.StoreCoupons.AsNoTracking().SingleAsync(c => c.Id == couponId)).RedemptionCount,
                await db.StoreCouponRedemptions.CountAsync(r => r.CouponId == couponId));
    }

    /// <summary>A single-use code in two checkouts at once: one gets it, the other hears why.</summary>
    [Fact]
    public async Task Two_checkouts_racing_for_a_single_use_code_only_one_gets_it()
    {
        var race = new RunBeforeStatement("UPDATE", "StoreCoupons");
        var (sqlite, coupon, _) = await CodeAsync(maxRedemptions: 1, perBuyer: null, interceptors: race);
        await using var _s = sqlite;
        var mine = await PlacedAsync(sqlite, "sarah@example.com");
        var theirs = await PlacedAsync(sqlite, "ed@example.com");

        string? theirResult = "not run";
        race.Competitor = async () =>
        {
            await using var other = await sqlite.NewContextAsync();
            theirResult = await StoreCouponReservations.TryReserveAsync(other, coupon, theirs, 5m, StoreTestData.Now);
        };

        await using var db = await sqlite.NewContextAsync();
        var myResult = await StoreCouponReservations.TryReserveAsync(db, coupon, mine, 5m, StoreTestData.Now);

        Assert.Null(theirResult);
        Assert.Equal("That code has been used as many times as it can be.", myResult);
        Assert.Equal((1, 1), await ReadAsync(sqlite, coupon.Id));
    }

    [Fact]
    public async Task Releasing_gives_the_use_back_once_however_often_it_is_asked()
    {
        var (sqlite, coupon, _) = await CodeAsync(maxRedemptions: 1);
        await using var _s = sqlite;
        var order = await PlacedAsync(sqlite, "sarah@example.com");

        await using var db = await sqlite.NewContextAsync();
        Assert.Null(await StoreCouponReservations.TryReserveAsync(db, coupon, order, 5m, StoreTestData.Now));
        Assert.Equal((1, 1), await ReadAsync(sqlite, coupon.Id));

        Assert.True(await StoreCouponReservations.ReleaseAsync(db, order.Id));
        Assert.Equal((0, 0), await ReadAsync(sqlite, coupon.Id));

        // The expiry job and a cancel racing each other both release the same order.
        Assert.False(await StoreCouponReservations.ReleaseAsync(db, order.Id));
        Assert.Equal((0, 0), await ReadAsync(sqlite, coupon.Id));
    }

    /// <summary>
    /// The expiry job and a cancel release the same order at the same moment: both find the
    /// redemption, one deletes it, and only that one gives the use back.
    /// </summary>
    [Fact]
    public async Task Two_racing_releases_of_one_order_give_back_one_use()
    {
        var race = new RunBeforeStatement("DELETE", "StoreCouponRedemptions");
        var (sqlite, coupon, _) = await CodeAsync(maxRedemptions: 5, interceptors: race);
        await using var _s = sqlite;
        var order = await PlacedAsync(sqlite, "sarah@example.com");
        var bystander = await PlacedAsync(sqlite, "ed@example.com");

        await using var db = await sqlite.NewContextAsync();
        Assert.Null(await StoreCouponReservations.TryReserveAsync(db, coupon, order, 5m, StoreTestData.Now));
        Assert.Null(await StoreCouponReservations.TryReserveAsync(db, coupon, bystander, 5m, StoreTestData.Now));
        Assert.Equal((2, 2), await ReadAsync(sqlite, coupon.Id));

        bool? theirs = null;
        race.Competitor = async () =>
        {
            await using var other = await sqlite.NewContextAsync();
            theirs = await StoreCouponReservations.ReleaseAsync(other, order.Id);
        };
        var mine = await StoreCouponReservations.ReleaseAsync(db, order.Id);

        Assert.True(theirs);
        Assert.False(mine);
        Assert.Equal((1, 1), await ReadAsync(sqlite, coupon.Id));   // the bystander's use is still counted
    }

    /// <summary>
    /// The per-buyer cap counts an order still waiting for payment — otherwise one buyer could
    /// open three checkouts with a once-each code and pay for all three.
    /// </summary>
    [Fact]
    public async Task An_unpaid_order_holding_the_code_counts_against_the_buyer()
    {
        var (sqlite, coupon, _) = await CodeAsync(perBuyer: 1);
        await using var _s = sqlite;
        var first = await PlacedAsync(sqlite, "sarah@example.com");
        var second = await PlacedAsync(sqlite, "  SARAH@example.com");

        await using var db = await sqlite.NewContextAsync();
        Assert.Null(await StoreCouponReservations.TryReserveAsync(db, coupon, first, 5m, StoreTestData.Now));
        Assert.Equal("That code has already been used with this email address.",
            await StoreCouponReservations.TryReserveAsync(db, coupon, second, 5m, StoreTestData.Now));
        Assert.Equal((1, 1), await ReadAsync(sqlite, coupon.Id));
    }

    /// <summary>A buyer is the same buyer by email OR by account: a new email on the same account still counts.</summary>
    [Fact]
    public async Task Prior_uses_are_counted_by_email_or_by_account()
    {
        var (sqlite, coupon, buyer) = await CodeAsync(perBuyer: 1);
        await using var _s = sqlite;
        var signedIn = await PlacedAsync(sqlite, "sarah@example.com", buyer);
        var newEmailSameAccount = await PlacedAsync(sqlite, "sarah.hollow@example.net", buyer);
        var guestSameEmail = await PlacedAsync(sqlite, "Sarah@Example.com");

        await using var db = await sqlite.NewContextAsync();
        Assert.Null(await StoreCouponReservations.TryReserveAsync(db, coupon, signedIn, 5m, StoreTestData.Now));

        Assert.Equal(1, await StoreCouponReservations.PriorRedemptionsAsync(
            db, coupon.Id, newEmailSameAccount.BuyerEmailNormalized, buyer.Id));
        Assert.Equal(1, await StoreCouponReservations.PriorRedemptionsAsync(
            db, coupon.Id, guestSameEmail.BuyerEmailNormalized, null));
        Assert.Equal(0, await StoreCouponReservations.PriorRedemptionsAsync(
            db, coupon.Id, "SOMEONE@ELSE.COM", null));
        Assert.Equal(StoreCouponMath.AlreadyUsedByBuyer,
            await StoreCouponReservations.TryReserveAsync(db, coupon, newEmailSameAccount, 5m, StoreTestData.Now));
    }
}
