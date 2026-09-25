using Ben.Data.WebApi.Services.Store;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Ben.Web.Tests.Store;

/// <summary>Order numbers are unique and never trip a checkout (storefront S0.11).</summary>
public sealed class StoreOrderNumbersTests
{
    [Fact]
    public async Task The_first_order_is_100001_and_the_next_is_one_more()
    {
        await using var sqlite = await SqliteTestDb.CreateAsync();
        await using var db = await sqlite.NewContextAsync();

        var first = StoreTestData.Order(db);
        await StoreOrderNumbers.SaveNumberedAsync(db, first);
        var second = StoreTestData.Order(db);
        await StoreOrderNumbers.SaveNumberedAsync(db, second);

        Assert.Equal(100001, first.OrderNumber);
        Assert.Equal(100002, second.OrderNumber);
    }

    /// <summary>
    /// Two checkouts read the same maximum. The other one saves first; this one's insert is refused
    /// by the unique index, and it takes the next number instead of failing the buyer's checkout.
    /// </summary>
    [Fact]
    public async Task A_checkout_that_loses_the_race_for_a_number_takes_the_next_one()
    {
        var race = new RunBeforeSave();
        await using var sqlite = await SqliteTestDb.CreateAsync(race);
        await using var db = await sqlite.NewContextAsync();

        race.Competitor = async () =>
        {
            await using var other = await sqlite.NewContextAsync();
            StoreTestData.Order(other, number: 100001, email: "other@example.com");
            await other.SaveChangesAsync();
        };

        var mine = StoreTestData.Order(db);
        await StoreOrderNumbers.SaveNumberedAsync(db, mine);

        Assert.Equal(100002, mine.OrderNumber);
        await using var check = await sqlite.NewContextAsync();
        Assert.Equal([100001, 100002], await check.StoreOrders.OrderBy(o => o.OrderNumber).Select(o => o.OrderNumber).ToListAsync());
    }
}
