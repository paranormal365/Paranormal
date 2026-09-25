using Ben.Data.Common;
using Ben.Data.Common.Enums;
using Ben.Data.Source.Entities;
using Ben.Data.WebApi.Services;
using Ben.Data.WebApi.Services.Billing.StripeIntegration;
using Ben.Data.WebApi.Services.Store;
using Ben.Service.Models.Store;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Ben.Web.Tests.Store;

/// <summary>
/// Refunds with packages (store sellers, backlog 251, P8): a package's shipping given back, one
/// package cancelled while the rest goes on, and a whole order cancelled taking every package.
/// </summary>
/// <remarks>
/// The order: package 1, the store's — a $20 meter; package 2, Hazel's — a $30 pod. $5 shipping
/// each, 8% tax on everything: 50 + 10 + 4.80 = $64.80.
/// </remarks>
public sealed class StoreParcelRefundTests : IAsyncLifetime
{
    private SqliteTestDb _sqlite = null!;
    private AppUser _admin = null!, _hazel = null!;
    private readonly FakeStoreStripeGateway _stripe = new();
    private readonly FakeStoreTaxService _tax = new();
    private Guid _orderId, _ours, _hers, _podItem, _podVariant;

    public async Task InitializeAsync()
    {
        StoreAlerts.ResetHourlyLimits();
        _sqlite = await SqliteTestDb.CreateAsync();
        await using var db = await _sqlite.NewContextAsync();
        _admin = StoreTestData.Person(db);
        _hazel = StoreTestData.Person(db, "hazel");
        var meter = StoreTestData.Variant(db, _admin, onHand: 3, price: 20m);
        var pod = StoreTestData.Variant(db, _admin, onHand: 3, price: 30m);
        _podVariant = pod.Id;
        var order = StoreTestData.Order(db, StoreOrderStatus.Paid);
        order.Subtotal = 50m; order.ShippingAmount = 10m; order.TaxAmount = 4.80m; order.ShippingTaxAmount = 0.80m; order.Total = 64.80m;
        order.PaidUtc = DateTime.UtcNow.AddHours(-1);
        order.StripePaymentIntentId = FakeStoreStripeGateway.IntentIdFor(order.Id);
        order.StripeTaxCalculationId = "taxcalc_1";
        order.StripeTaxTransactionId = "tax_txn_1";
        _orderId = order.Id;
        _ours = Guid.NewGuid();
        _hers = Guid.NewGuid();
        db.StoreOrderParcels.Add(new StoreOrderParcel { Id = _ours, OrderId = order.Id, Number = 1, ShippingAmount = 5m, ShippingTaxAmount = 0.40m, DateCreated = DateTime.UtcNow });
        db.StoreOrderParcels.Add(new StoreOrderParcel { Id = _hers, OrderId = order.Id, Number = 2, SellerAppUserId = _hazel.Id, ShippingAmount = 5m,
            ShippingTaxAmount = 0.40m, SellerShippingCredit = 5m, DateCreated = DateTime.UtcNow });
        db.StoreOrderItems.Add(new StoreOrderItem { Id = Guid.NewGuid(), OrderId = order.Id, ParcelId = _ours, ProductId = meter.ProductId, VariantId = meter.Id,
            ProductName = "K-II", Sku = meter.Sku, UnitPrice = 20m, Quantity = 1, LineTotal = 20m, TaxAmount = 1.60m, DateCreated = DateTime.UtcNow });
        _podItem = Guid.NewGuid();
        db.StoreOrderItems.Add(new StoreOrderItem { Id = _podItem, OrderId = order.Id, ParcelId = _hers, ProductId = pod.ProductId, VariantId = pod.Id,
            ProductName = "REM Pod", Sku = pod.Sku, UnitPrice = 30m, Quantity = 1, LineTotal = 30m, TaxAmount = 2.40m, DateCreated = DateTime.UtcNow });
        await db.SaveChangesAsync();
    }

    public Task DisposeAsync() => _sqlite.DisposeAsync().AsTask();

    private (StoreRefundService Refunds, StoreOrderTransitions Desk, StoreParcelTransitions Parcels) Services()
    {
        var site = Options.Create(new SiteIdentity { Name = "IsHaunted.com", BaseUrl = "https://test.local" });
        var mailer = new StoreOrderMailer(TestOutbox.WithoutMail(_sqlite.Factory), site);
        var alerts = new StoreAlerts(_sqlite.Factory, new PlatformMessageService(_sqlite.Factory), mailer, NullLogger<StoreAlerts>.Instance);
        var payments = new StoreOrderPayments(_sqlite.Factory, _stripe, _tax, mailer, alerts, NullLogger<StoreOrderPayments>.Instance);
        var refunds = new StoreRefundService(_sqlite.Factory, _stripe, _tax, mailer, alerts, NullLogger<StoreRefundService>.Instance);
        return (refunds, new StoreOrderTransitions(_sqlite.Factory, mailer, alerts, refunds, payments), new StoreParcelTransitions(_sqlite.Factory, mailer, alerts));
    }

    private async Task<(StoreOrder Order, StoreOrderParcel Ours, StoreOrderParcel Hers, int PodStock)> ReadAsync()
    {
        await using var db = await _sqlite.NewContextAsync();
        var parcels = await db.StoreOrderParcels.AsNoTracking().Where(x => x.OrderId == _orderId).ToListAsync();
        return (await db.StoreOrders.AsNoTracking().SingleAsync(o => o.Id == _orderId), parcels.Single(x => x.Id == _ours), parcels.Single(x => x.Id == _hers),
            await db.StoreProductVariants.Where(v => v.Id == _podVariant).Select(v => v.StockOnHand).SingleAsync());
    }

    [Fact]
    public async Task A_packages_shipping_goes_back_with_its_tax_once()
    {
        var attempt = await Services().Refunds.RefundAsync(_orderId, new StoreRefundRequest(null, null, "Arrived late", false, [_hers]), _admin.Id);

        Assert.Equal(StoreRefundOutcomeKind.Completed, attempt.Kind);
        var (order, ours, hers, _) = await ReadAsync();
        Assert.Equal((5.40m, 0m, 5.40m), (hers.ShippingRefunded, ours.ShippingRefunded, order.RefundedAmount));

        var again = await Services().Refunds.RefundAsync(_orderId, new StoreRefundRequest(null, null, "Twice", false, [_hers]), _admin.Id);
        Assert.Equal("Package 2's shipping has already been refunded.", again.Sentence);
    }

    [Fact]
    public async Task Cancelling_one_package_refunds_its_items_and_shipping_and_the_rest_goes_on()
    {
        var (result, refund) = await Services().Desk.CancelParcelAsync(_orderId, _hers, new CancelStoreOrderRequest("Seller can't make it", true), _admin.Id);

        Assert.True(result.Ok, result.Refusal);
        Assert.Equal(StoreRefundOutcomeKind.Completed, refund!.Kind);
        var (order, ours, hers, podStock) = await ReadAsync();
        Assert.Equal(StoreParcelStatus.Cancelled, hers.Status);
        Assert.Equal(StoreParcelStatus.Waiting, ours.Status);
        Assert.Equal(StoreOrderStatus.Paid, order.Status);           // the store's package still to send
        Assert.Equal(32.40m + 5.40m, order.RefundedAmount);          // the pod with its tax, and its shipping with its tax
        Assert.Equal(4, podStock);                                   // back on the shelf

        // Once the store's own package ships, the order has nothing left to wait for.
        Assert.True((await Services().Parcels.ShipAsync(_orderId, _ours, new StoreShipmentInfo(StoreCarriers.Usps, "9400", null, null), _admin.Id)).Ok);
        Assert.Equal(StoreOrderStatus.Shipped, (await ReadAsync()).Order.Status);
    }

    [Fact]
    public async Task Cancelling_the_package_still_to_go_leaves_a_partly_shipped_order_shipped()
    {
        await Services().Parcels.ShipAsync(_orderId, _ours, new StoreShipmentInfo(StoreCarriers.Usps, "9400", null, null), _admin.Id);
        Assert.Equal(StoreOrderStatus.PartiallyShipped, (await ReadAsync()).Order.Status);

        var (result, _) = await Services().Desk.CancelParcelAsync(_orderId, _hers, new CancelStoreOrderRequest("Seller can't make it", false), _admin.Id);

        Assert.True(result.Ok, result.Refusal);
        Assert.Equal(StoreOrderStatus.Shipped, (await ReadAsync()).Order.Status);
    }

    [Fact]
    public async Task A_package_that_has_gone_or_the_last_one_left_is_not_cancelled_alone()
    {
        await Services().Parcels.ShipAsync(_orderId, _ours, new StoreShipmentInfo(StoreCarriers.Usps, "9400", null, null), _admin.Id);
        var (gone, _) = await Services().Desk.CancelParcelAsync(_orderId, _ours, new CancelStoreOrderRequest("No", false), _admin.Id);
        Assert.Equal(StoreOrderDeskSentences.PackageAlreadyGone, gone.Refusal);

        await using (var db = await _sqlite.NewContextAsync())
            await db.StoreOrderParcels.Where(x => x.Id == _ours).ExecuteUpdateAsync(u => u.SetProperty(x => x.Status, StoreParcelStatus.Cancelled));
        var (last, _) = await Services().Desk.CancelParcelAsync(_orderId, _hers, new CancelStoreOrderRequest("No", false), _admin.Id);
        Assert.Equal(StoreOrderDeskSentences.LastPackageCancelsOrder, last.Refusal);
    }

    [Fact]
    public async Task Cancelling_the_order_gives_back_every_packages_shipping_and_cancels_them()
    {
        var (result, _) = await Services().Desk.CancelAsync(_orderId, new CancelStoreOrderRequest("Changed mind", true), _admin.Id);

        Assert.True(result.Ok, result.Refusal);
        var (order, ours, hers, _) = await ReadAsync();
        Assert.Equal((StoreOrderStatus.Cancelled, 64.80m), (order.Status, order.RefundedAmount));
        Assert.Equal((StoreParcelStatus.Cancelled, StoreParcelStatus.Cancelled), (ours.Status, hers.Status));
        Assert.Equal((5.40m, 5.40m), (ours.ShippingRefunded, hers.ShippingRefunded));
    }
}
