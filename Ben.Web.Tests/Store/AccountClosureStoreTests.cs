using Ben.Data.Common.Constants;
using Ben.Data.Common.Enums;
using Ben.Data.WebApi.Services;
using Ben.Data.WebApi.Services.Store;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Ben.Web.Tests.Store;

/// <summary>
/// Somebody closing their own account leaves their store orders behind without their name — on
/// the real SQL path, transaction and all (storefront S0.12).
/// </summary>
/// <remarks>
/// Beside the InMemory <c>AccountClosureTests</c> rather than inside them: that suite suppresses
/// the transaction warning because InMemory has none, and the scrub runs inside the closure's
/// transaction. Here the transaction is real.
/// </remarks>
public sealed class AccountClosureStoreTests
{
    [Fact]
    public async Task Closing_an_account_scrubs_its_delivered_order_and_detaches_the_one_on_its_way()
    {
        await using var sqlite = await SqliteTestDb.CreateAsync();
        Guid buyerId, delivered, shipped;
        await using (var db = await sqlite.NewContextAsync())
        {
            var buyer = StoreTestData.Person(db, "buyer");
            buyer.DisplayName = "Sarah Hollow";
            buyer.Email = "sarah@example.com";
            delivered = StoreTestData.Order(db, StoreOrderStatus.Delivered, buyer).Id;
            shipped = StoreTestData.Order(db, StoreOrderStatus.Shipped, buyer).Id;
            await db.SaveChangesAsync();
            buyerId = buyer.Id;
        }

        var service = new AccountClosureService(sqlite.Factory, Support.AppleTestSupport.Credentials(sqlite.Factory),
            TestMedia.Ingest(), NullLogger<AccountClosureService>.Instance);
        var result = await service.CloseAsync(buyerId);
        Assert.True(result.Closed, result.Refusal);

        await using var check = await sqlite.NewContextAsync();
        var finished = await check.StoreOrders.AsNoTracking().SingleAsync(o => o.Id == delivered);
        Assert.Equal((AccountClosure.FormerMemberName, AccountClosure.ClosedEmailFor(buyerId), StoreOrderScrub.RemovedStreet, (Guid?)null),
            (finished.BuyerName, finished.BuyerEmail, finished.ShipStreet1, finished.BuyerAppUserId));

        var onItsWay = await check.StoreOrders.AsNoTracking().SingleAsync(o => o.Id == shipped);
        Assert.Equal(("Sarah Hollow", "13 Crossroads Lane", (Guid?)null), (onItsWay.ShipName, onItsWay.ShipStreet1, onItsWay.BuyerAppUserId));
        Assert.NotNull(onItsWay.PendingAnonymisationSinceUtc);
    }
}
