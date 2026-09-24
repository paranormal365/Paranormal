using System.Text;
using Ben.Data.Common;
using Ben.Data.Common.Enums;
using Ben.Data.Common.Mail;
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

/// <summary>The order desk's API (storefront S5.4): finding orders, the CSV, and what each action answers.</summary>
public sealed class AdminStoreOrderControllerTests : IAsyncLifetime
{
    private SqliteTestDb _sqlite = null!;
    private AppUser _admin = null!;
    private readonly FakeStoreStripeGateway _stripe = new();
    private readonly FakeStoreTaxService _tax = new();

    public async Task InitializeAsync()
    {
        StoreAlerts.ResetHourlyLimits();
        _sqlite = await SqliteTestDb.CreateAsync();
        await using var db = await _sqlite.NewContextAsync();
        _admin = StoreTestData.Person(db);
        await db.SaveChangesAsync();
    }

    public Task DisposeAsync() => _sqlite.DisposeAsync().AsTask();

    private AdminStoreOrderController Controller()
    {
        var site = Options.Create(new SiteIdentity { Name = "IsHaunted.com", BaseUrl = "https://test.local" });
        var mailer = new StoreOrderMailer(TestOutbox.WithoutMail(_sqlite.Factory), site);
        var alerts = new StoreAlerts(_sqlite.Factory, new PlatformMessageService(_sqlite.Factory), mailer, NullLogger<StoreAlerts>.Instance);
        var payments = new StoreOrderPayments(_sqlite.Factory, _stripe, _tax, mailer, alerts, NullLogger<StoreOrderPayments>.Instance);
        var refunds = new StoreRefundService(_sqlite.Factory, _stripe, _tax, mailer, alerts, NullLogger<StoreRefundService>.Instance);
        var desk = new StoreOrderTransitions(_sqlite.Factory, mailer, alerts, refunds, payments);
        return new AdminStoreOrderController(_sqlite.Factory, desk, refunds, Options.Create(new StripeOptions { SecretKey = "sk_test_x" }))
        {
            ControllerContext = StoreTestData.SignedInAs(_admin.Id),
        };
    }

    private async Task<StoreOrder> OrderAsync(string sku = "KII-EMF", string product = "K-II EMF Meter", string? coupon = null,
        StoreOrderStatus status = StoreOrderStatus.Paid, string buyer = "Sarah Hollow")
    {
        await using var db = await _sqlite.NewContextAsync();
        var admin = await db.AppUsers.SingleAsync(u => u.Id == _admin.Id);
        var v = StoreTestData.Variant(db, admin, price: 20m);
        var order = StoreTestData.Order(db, status);
        order.PaidUtc = DateTime.UtcNow;
        order.BuyerName = buyer;
        order.CouponCode = coupon;
        order.Total = 20m;
        order.StripePaymentIntentId = FakeStoreStripeGateway.IntentIdFor(order.Id);
        db.StoreOrderItems.Add(new StoreOrderItem
        {
            Id = Guid.NewGuid(), OrderId = order.Id, ProductId = v.ProductId, VariantId = v.Id, ProductName = product, Sku = sku,
            UnitPrice = 20m, Quantity = 1, LineTotal = 20m, DateCreated = DateTime.UtcNow,
        });
        await db.SaveChangesAsync();
        return order;
    }

    private static List<StoreOrderListRecord> Rows(ActionResult<IEnumerable<StoreOrderListRecord>> r)
        => ((IEnumerable<StoreOrderListRecord>)Assert.IsType<OkObjectResult>(r.Result).Value!).ToList();

    [Fact]
    public async Task Q_matches_an_item_sku_and_a_product_name()
    {
        var bag = await OrderAsync(sku: "BAG-OLV-LRG", product: "Investigator's Field Bag");
        await OrderAsync();

        Assert.Equal([bag.Id], Rows(await Controller().List(null, "olv-lrg", null, null, null, null, null, null, null, null, null, default)).Select(r => r.Id));
        Assert.Equal([bag.Id], Rows(await Controller().List(null, "field bag", null, null, null, null, null, null, null, null, null, default)).Select(r => r.Id));
        Assert.Equal([bag.Id], Rows(await Controller().List(null, $"#{bag.OrderNumber}", null, null, null, null, null, null, null, null, null, default)).Select(r => r.Id));
    }

    [Fact]
    public async Task The_coupon_filter_finds_the_orders_that_used_it()
    {
        var ghost = await OrderAsync(coupon: "GHOST10");
        await OrderAsync();
        Assert.Equal([ghost.Id], Rows(await Controller().List(null, null, "ghost10", null, null, null, null, null, null, null, null, default)).Select(r => r.Id));
    }

    [Fact]
    public async Task Unpaid_finished_checkouts_are_hidden_unless_asked_for()
    {
        var paid = await OrderAsync();
        await using (var db = await _sqlite.NewContextAsync())
        {
            var dead = StoreTestData.Order(db, StoreOrderStatus.Cancelled);
            dead.PaidUtc = null;
            await db.SaveChangesAsync();
        }
        Assert.Equal([paid.Id], Rows(await Controller().List(null, null, null, null, null, null, null, null, null, null, null, default)).Select(r => r.Id));
        Assert.Equal(2, Rows(await Controller().List(null, null, null, null, null, null, null, true, null, null, null, default)).Count);
    }

    [Fact]
    public async Task Export_respects_the_filters_and_quotes_commas()
    {
        await OrderAsync(product: "Bag, olive", coupon: "GHOST10");
        await OrderAsync(product: "K-II EMF Meter");

        var file = Assert.IsType<FileContentResult>(await Controller().Export(null, null, "GHOST10", null, null, null, null, null, null, default));
        var lines = Encoding.UTF8.GetString(file.FileContents).Trim('﻿').Split('\n', StringSplitOptions.RemoveEmptyEntries);

        Assert.Equal("text/csv", file.ContentType);
        Assert.Equal(2, lines.Length);   // the header and the one GHOST10 line
        Assert.Contains("\"Bag, olive\"", lines[1]);
    }

    [Fact]
    public async Task A_shipment_without_tracking_is_refused_unless_it_says_so()
    {
        var order = await OrderAsync();

        var refused = await Controller().Ship(order.Id, new StoreShipmentInfo(StoreCarriers.Usps, null, null, null), default);
        Assert.Equal(StoreOrderDeskSentences.NeedsTracking, Assert.IsType<ConflictObjectResult>(refused).Value);

        var shipped = await Controller().Ship(order.Id, new StoreShipmentInfo(StoreCarriers.Usps, null, null, null, NoTracking: true), default);
        var detail = (StoreOrderDetailAdminRecord)Assert.IsType<OkObjectResult>(shipped).Value!;
        Assert.Equal((StoreOrderStatus.Shipped, (string?)null), (detail.Status, detail.TrackingNumber));
        Assert.True(detail.Can.CanCorrectTracking);
    }

    [Fact]
    public async Task A_refund_refusal_names_the_balance_and_a_pending_one_is_accepted()
    {
        var order = await OrderAsync();

        var tooMuch = await Controller().Refund(order.Id, new StoreRefundRequest(25m, null, "x", false), default);
        Assert.Equal(StoreOrderDeskSentences.MoreThanRemaining(20m), Assert.IsType<BadRequestObjectResult>(tooMuch).Value);

        _stripe.RefundStatus = "pending";
        var pending = await Controller().Refund(order.Id, new StoreRefundRequest(5m, null, "Scuffed", false), default);
        var detail = (StoreOrderDetailAdminRecord)Assert.IsType<AcceptedResult>(pending).Value!;
        Assert.True(Assert.Single(detail.Refunds).IsAwaitingStripe);
        Assert.Equal(15m, detail.Refundable);
    }

    [Fact]
    public async Task Resend_queues_exactly_one_letter_and_answers_the_order()
    {
        var order = await OrderAsync();
        Assert.IsType<OkObjectResult>(await Controller().Resend(order.Id, new ResendStoreOrderLetterRequest("confirmation"), default));

        await using var db = await _sqlite.NewContextAsync();
        Assert.Equal(MailKinds.StoreOrderConfirmation.Key, Assert.Single(await db.OutboxEmails.ToListAsync()).Kind);
    }

    [Fact]
    public async Task The_address_cannot_be_changed_once_shipped()
    {
        var order = await OrderAsync(status: StoreOrderStatus.Shipped);
        var refused = await Controller().Address(order.Id,
            new ChangeStoreOrderAddressRequest(new StoreAddressInput("Ada", "615-555-0100", "1 Elm", null, "Nashville", "TN", "37203"), null), default);
        Assert.Equal(StoreOrderDeskSentences.AddressAfterShipping, Assert.IsType<ConflictObjectResult>(refused).Value);
    }

    [Fact]
    public async Task A_stranger_order_id_is_404()
    {
        Assert.IsType<NotFoundResult>((await Controller().Get(Guid.NewGuid(), default)).Result);
        Assert.IsType<NotFoundResult>(await Controller().Pack(Guid.NewGuid(), default));
    }

    [Fact]
    public async Task The_dashboard_link_follows_the_key_and_skips_pretend_payments()
    {
        await using var db = await _sqlite.NewContextAsync();
        var order = StoreTestData.Order(db, StoreOrderStatus.Paid);
        order.StripePaymentIntentId = "pi_3Real";
        Assert.Equal("https://dashboard.stripe.com/test/payments/pi_3Real", StoreOrderDesk.DashboardUrl(order, "sk_test_x"));
        Assert.Equal("https://dashboard.stripe.com/payments/pi_3Real", StoreOrderDesk.DashboardUrl(order, "sk_live_x"));
        order.StripePaymentIntentId = "pi_fake_1";
        Assert.Null(StoreOrderDesk.DashboardUrl(order, "sk_test_x"));
    }
}
