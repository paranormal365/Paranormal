using Ben.Data.Common;
using Ben.Data.Common.Enums;
using Ben.Data.Source.Entities;
using Ben.Data.WebApi.Controllers.Admin.Store;
using Ben.Data.WebApi.Services;
using Ben.Data.WebApi.Services.Billing.StripeIntegration;
using Ben.Data.WebApi.Services.Store;
using Ben.Service.Models.Store;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Ben.Web.Tests.Store;

/// <summary>
/// A seller's earnings and payouts (store sellers, backlog 251, P10): earned once when a package
/// ships, taken back by a refund after it, and paid by claiming — never twice, never lost.
/// </summary>
/// <remarks>Hazel's package: two pods at $30 each, earning $25 a unit (cost + ask), $5 label credit.</remarks>
public sealed class StoreSellerEarningsTests : IAsyncLifetime
{
    private SqliteTestDb _sqlite = null!;
    private AppUser _admin = null!, _hazel = null!;
    private readonly FakeStoreStripeGateway _stripe = new();
    private readonly FakeStoreTaxService _tax = new();
    private Guid _orderId, _parcelId, _itemId;

    public async Task InitializeAsync()
    {
        StoreAlerts.ResetHourlyLimits();
        _sqlite = await SqliteTestDb.CreateAsync();
        await using var db = await _sqlite.NewContextAsync();
        _admin = StoreTestData.Person(db);
        _hazel = StoreTestData.Person(db, "hazel");
        var pod = StoreTestData.Variant(db, _admin, onHand: 3, price: 30m);
        var order = StoreTestData.Order(db, StoreOrderStatus.Paid);
        order.Subtotal = 60m; order.Total = 60m;
        order.PaidUtc = DateTime.UtcNow.AddHours(-1);
        order.StripePaymentIntentId = FakeStoreStripeGateway.IntentIdFor(order.Id);
        _orderId = order.Id;
        _parcelId = Guid.NewGuid();
        _itemId = Guid.NewGuid();
        db.StoreOrderParcels.Add(new StoreOrderParcel { Id = _parcelId, OrderId = order.Id, Number = 1, SellerAppUserId = _hazel.Id, SellerShippingCredit = 5m, DateCreated = DateTime.UtcNow });
        db.StoreOrderItems.Add(new StoreOrderItem { Id = _itemId, OrderId = order.Id, ParcelId = _parcelId, ProductId = pod.ProductId, VariantId = pod.Id,
            ProductName = "REM Pod", Sku = pod.Sku, UnitPrice = 30m, Quantity = 2, LineTotal = 60m, UnitSellerEarning = 25m, UnitSiteMarkup = 5m, DateCreated = DateTime.UtcNow });
        await db.SaveChangesAsync();
    }

    public Task DisposeAsync() => _sqlite.DisposeAsync().AsTask();

    private (StoreParcelTransitions Parcels, StoreRefundService Refunds) Services()
    {
        var site = Options.Create(new SiteIdentity { Name = "IsHaunted.com", BaseUrl = "https://test.local" });
        var mailer = new StoreOrderMailer(TestOutbox.WithoutMail(_sqlite.Factory), site);
        var alerts = new StoreAlerts(_sqlite.Factory, new PlatformMessageService(_sqlite.Factory), mailer, NullLogger<StoreAlerts>.Instance);
        return (new StoreParcelTransitions(_sqlite.Factory, mailer, alerts),
            new StoreRefundService(_sqlite.Factory, _stripe, _tax, mailer, alerts, NullLogger<StoreRefundService>.Instance));
    }

    private Task<StoreDeskResult> ShipAsync() => Services().Parcels.ShipAsync(_orderId, _parcelId, new StoreShipmentInfo(StoreCarriers.Usps, "9400", null, null), _admin.Id);

    private async Task<List<StoreSellerEarning>> LinesAsync()
    {
        await using var db = await _sqlite.NewContextAsync();
        return await db.StoreSellerEarnings.AsNoTracking().OrderBy(e => e.Kind).ToListAsync();
    }

    private AdminStoreSellerController Books() => new(_sqlite.Factory) { ControllerContext = StoreTestData.SignedInAs(_admin.Id) };

    private static StoreSellerDetailRecord Detail(ActionResult<StoreSellerDetailRecord> r) => (StoreSellerDetailRecord)Assert.IsType<OkObjectResult>(r.Result).Value!;

    [Fact]
    public async Task Shipping_earns_the_units_and_the_label_once()
    {
        Assert.Empty(await LinesAsync());   // paid, not shipped: nothing earned yet
        Assert.True((await ShipAsync()).Ok);

        var lines = await LinesAsync();
        Assert.Equal([(StoreEarningKind.Sale, 50m, (int?)2), (StoreEarningKind.Shipping, 5m, (int?)null)], lines.Select(l => (l.Kind, l.Amount, l.Units)));
        Assert.All(lines, l => Assert.Equal(_hazel.Id, l.SellerAppUserId));

        // Written once, however often the shipment is recorded.
        await using var db = await _sqlite.NewContextAsync();
        var parcel = await db.StoreOrderParcels.SingleAsync();
        await StoreSellerLedger.RecordShipmentAsync(db, parcel, await db.StoreOrderItems.ToListAsync(), DateTime.UtcNow, default);
        await db.SaveChangesAsync();
        Assert.Equal(2, (await LinesAsync()).Count);
    }

    [Fact]
    public async Task A_refund_before_shipping_is_never_earned_and_one_after_takes_it_back()
    {
        await Services().Refunds.RefundAsync(_orderId, new StoreRefundRequest(null, [new StoreRefundLine(_itemId, 1)], "Changed mind", false), _admin.Id);
        Assert.Empty(await LinesAsync());
        await ShipAsync();
        Assert.Equal(25m, (await LinesAsync()).Single(l => l.Kind == StoreEarningKind.Sale).Amount);   // one unit left to earn

        await Services().Refunds.RefundAsync(_orderId, new StoreRefundRequest(null, [new StoreRefundLine(_itemId, 1)], "Broken in the post", false), _admin.Id);
        var back = (await LinesAsync()).Single(l => l.Kind == StoreEarningKind.Refund);
        Assert.Equal((-25m, (int?)-1), (back.Amount, back.Units));
    }

    [Fact]
    public async Task A_payment_claims_what_is_owed_and_a_void_releases_it()
    {
        await ShipAsync();
        var before = Detail(await Books().Get(_hazel.Id, default));
        Assert.Equal((55m, 0m), (before.Earnings.Owed, before.Earnings.PaidToDate));

        var paid = Detail(await Books().RecordPayment(_hazel.Id, new RecordSellerPaymentRequest(DateTime.UtcNow, 55m, DateTime.UtcNow.Date, "Transfer 1"), default));
        Assert.Equal((0m, 55m), (paid.Earnings.Owed, paid.Earnings.PaidToDate));
        Assert.All(await LinesAsync(), l => Assert.NotNull(l.PayoutId));

        var payout = Assert.Single(paid.Earnings.Payouts);
        var voided = Detail(await Books().VoidPayment(_hazel.Id, payout.Id, new VoidSellerPaymentRequest("Sent to the wrong account"), default));
        Assert.Equal((55m, 0m), (voided.Earnings.Owed, voided.Earnings.PaidToDate));
        Assert.All(await LinesAsync(), l => Assert.Null(l.PayoutId));
    }

    [Fact]
    public async Task A_payment_for_a_stale_figure_is_refused_with_the_new_one()
    {
        await ShipAsync();
        var result = await Books().RecordPayment(_hazel.Id, new RecordSellerPaymentRequest(DateTime.UtcNow, 50m, DateTime.UtcNow.Date, null), default);

        Assert.Equal(StoreSellerLedger.AmountChanged(55m), Assert.IsType<ConflictObjectResult>(result.Result).Value);
        Assert.All(await LinesAsync(), l => Assert.Null(l.PayoutId));   // nothing claimed
    }

    [Fact]
    public async Task Only_what_is_past_the_cutoff_is_claimed()
    {
        await ShipAsync();
        var result = await Books().RecordPayment(_hazel.Id, new RecordSellerPaymentRequest(DateTime.UtcNow.AddDays(-1), 55m, DateTime.UtcNow.Date, null), default);
        Assert.Equal(StoreSellerLedger.NothingOwed, Assert.IsType<ConflictObjectResult>(result.Result).Value);
    }

    [Fact]
    public async Task A_seller_who_owes_the_store_carries_it_forward()
    {
        await ShipAsync();
        Detail(await Books().RecordPayment(_hazel.Id, new RecordSellerPaymentRequest(DateTime.UtcNow, 55m, DateTime.UtcNow.Date, null), default));
        // A refund after they were paid takes an earning back: the balance goes negative.
        await Services().Refunds.RefundAsync(_orderId, new StoreRefundRequest(null, [new StoreRefundLine(_itemId, 2)], "Faulty", false), _admin.Id);

        var owes = Detail(await Books().Get(_hazel.Id, default));
        Assert.Equal(-50m, owes.Earnings.Owed);
        var refused = await Books().RecordPayment(_hazel.Id, new RecordSellerPaymentRequest(DateTime.UtcNow, -50m, DateTime.UtcNow.Date, null), default);
        Assert.Equal(StoreSellerLedger.OwesTheStore(-50m), Assert.IsType<ConflictObjectResult>(refused.Result).Value);

        // An adjustment is a line like any other.
        var adjusted = Detail(await Books().Adjust(_hazel.Id, new SellerAdjustmentRequest(60m, "Bonus for the spring run"), default));
        Assert.Equal(10m, adjusted.Earnings.Owed);
    }
}
