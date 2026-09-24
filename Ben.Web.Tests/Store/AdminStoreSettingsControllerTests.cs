using Ben.Data.Common.Enums;
using Ben.Data.Source.Entities;
using Ben.Data.WebApi.Controllers.Admin.Store;
using Ben.Data.WebApi.Services;
using Ben.Data.WebApi.Services.Store;
using Ben.Service.Models.Store;
using Ben.Service.RepositoryService.GenericInterfaces;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Moq;
using Xunit;

namespace Ben.Web.Tests.Store;

/// <summary>
/// The store settings page saves the whole form or none of it, and its checklist says every
/// reason the store could not take an order (storefront S1.7).
/// </summary>
public sealed class AdminStoreSettingsControllerTests : IAsyncLifetime
{
    private SqliteTestDb _sqlite = null!;
    private AppUser _admin = null!;

    public async Task InitializeAsync()
    {
        _sqlite = await SqliteTestDb.CreateAsync();
        await using var db = await _sqlite.NewContextAsync();
        _admin = StoreTestData.Person(db);
        await db.SaveChangesAsync();
    }

    public Task DisposeAsync() => _sqlite.DisposeAsync().AsTask();

    private sealed class Probe(StoreTaxReadiness answer) : IStoreTaxProbe
    {
        public Task<StoreTaxReadiness> ProbeAsync(CancellationToken ct = default) => Task.FromResult(answer);
    }

    private static readonly StoreTaxReadiness TaxOn = new(true, true, ["TN"], null);

    private AdminStoreSettingsController Controller(StoreTaxReadiness? tax = null, StorePaymentSetup? payments = null) => new(
        _sqlite.Factory, new SiteSettingsService(_sqlite.Factory), new Mock<IAuditLogService>().Object,
        new Probe(tax ?? TaxOn), payments ?? new StorePaymentSetup(false, true, true))
    {
        ControllerContext = StoreTestData.SignedInAs(_admin.Id),
    };

    private static SaveStoreSettingsRequest Form(string state = "TN", string street = "13 Crossroads Lane") => new(
        CheckoutEnabled: true, ShippingFlatRate: 7.5m, FreeShippingThreshold: 100m, LowStockThreshold: 3,
        ShipFromStreet: street, ShipFromCity: "Adams", ShipFromState: state, ShipFromZip: "37010",
        SupportEmail: "shop@ishaunted.com", ReturnsWindowDays: 30, ReservationMinutes: 15, LinkEnabled: false);

    private async Task ReadyCatalogueAsync(bool storeOn = true)
    {
        await using var db = await _sqlite.NewContextAsync();
        var admin = await db.AppUsers.SingleAsync(u => u.Id == _admin.Id);
        StoreTestData.Variant(db, admin, onHand: 5);
        if (storeOn)
            db.SiteSettings.Add(new SiteSetting
            {
                Id = Guid.NewGuid(), Key = SiteSettingKeys.FeatureStore, Value = "true",
                DateCreated = StoreTestData.Now, CreatedByAppUserId = admin.Id,
            });
        await db.SaveChangesAsync();
    }

    private static StoreSettingsAdminRecord Ok(ActionResult<StoreSettingsAdminRecord> result)
        => (StoreSettingsAdminRecord)Assert.IsType<OkObjectResult>(result.Result).Value!;

    [Fact]
    public async Task A_bad_value_refuses_the_whole_form()
    {
        var result = await Controller().Save(Form(state: "Tennessee", street: "1 New Street"), default);

        Assert.Equal("Ship-from state is two letters, like TN.", Assert.IsType<BadRequestObjectResult>(result.Result).Value);
        await using var db = await _sqlite.NewContextAsync();
        Assert.False(await db.SiteSettings.AnyAsync(s => s.Key.StartsWith("store.")));
    }

    [Fact]
    public async Task What_is_saved_reads_back_and_a_ready_store_says_so()
    {
        await ReadyCatalogueAsync();

        var saved = Ok(await Controller().Save(Form(state: "tn"), default));

        Assert.Equal(("TN", 7.5m, (decimal?)100m, "shop@ishaunted.com"),
            (saved.ShipFromState, saved.ShippingFlatRate!.Value, saved.FreeShippingThreshold, saved.SupportEmail));
        Assert.True(saved.ReadyToSell, string.Join(" | ", saved.NotReadyBecause));
        Assert.Equal(["TN"], saved.TaxRegisteredStates);
    }

    [Fact]
    public async Task An_empty_store_lists_every_reason_it_cannot_sell()
    {
        var record = Ok(await Controller(payments: new StorePaymentSetup(false, false, false)).Get(default));

        Assert.False(record.ReadyToSell);
        Assert.Equal([
            "No ship-from address, so Stripe can't work out tax.",
            "No live product with stock.",
            "Online payment isn't set up.",
            "The store is switched off.",
        ], record.NotReadyBecause);
    }

    [Fact]
    public async Task A_secret_key_without_a_publishable_key_is_not_ready()
    {
        await ReadyCatalogueAsync();
        await Controller().Save(Form(), default);

        var record = Ok(await Controller(payments: new StorePaymentSetup(false, true, false)).Get(default));

        Assert.Equal(["The publishable key isn't set — the payment form can't load."], record.NotReadyBecause);
    }

    [Fact]
    public async Task Stripe_tax_still_pending_in_the_dashboard_is_not_ready()
    {
        await ReadyCatalogueAsync();
        await Controller().Save(Form(), default);

        var pending = Ok(await Controller(tax: new StoreTaxReadiness(false, true, [], null)).Get(default));
        Assert.Equal(["Stripe Tax isn't active in the Stripe dashboard."], pending.NotReadyBecause);

        var silent = Ok(await Controller(tax: new StoreTaxReadiness(false, false, [], StripeTaxProbe.NoAnswer)).Get(default));
        Assert.Equal([StripeTaxProbe.NoAnswer], silent.NotReadyBecause);
    }

    [Fact]
    public async Task Pretend_stripe_needs_no_keys_in_development()
    {
        await ReadyCatalogueAsync();
        await Controller().Save(Form(), default);

        var record = Ok(await Controller(payments: new StorePaymentSetup(true, false, false)).Get(default));
        Assert.True(record.ReadyToSell, string.Join(" | ", record.NotReadyBecause));
    }
}

/// <summary>The store at a glance, counted from real rows (storefront S1.7).</summary>
public sealed class AdminStoreDashboardControllerTests
{
    [Fact]
    public async Task The_dashboard_counts_what_waits_what_sold_and_what_is_held()
    {
        await using var sqlite = await SqliteTestDb.CreateAsync();
        await using (var db = await sqlite.NewContextAsync())
        {
            var admin = StoreTestData.Person(db);
            var now = DateTime.UtcNow;
            StoreOrder Paid(StoreOrderStatus status, decimal total)
            {
                var o = StoreTestData.Order(db, status, total: total);
                o.PaidUtc = now.AddHours(-1);
                return o;
            }
            Paid(StoreOrderStatus.Paid, 100m);
            Paid(StoreOrderStatus.Paid, 50m);
            Paid(StoreOrderStatus.Packed, 25m);
            var shipped = Paid(StoreOrderStatus.Shipped, 10m);
            shipped.NeedsAttention = true;
            var old = Paid(StoreOrderStatus.Delivered, 999m);
            old.PaidUtc = now.AddDays(-90);

            // Two open checkouts: one still holding 3, one whose hold was released.
            var variant = StoreTestData.Variant(db, admin, onHand: 10, reserved: 3);
            var holding = StoreTestData.Order(db, StoreOrderStatus.PendingPayment, total: 30m);
            var released = StoreTestData.Order(db, StoreOrderStatus.PendingPayment, total: 30m);
            released.ReservationReleasedUtc = now;
            foreach (var (order, qty) in new[] { (holding, 3), (released, 4) })
                db.StoreOrderItems.Add(new StoreOrderItem
                {
                    Id = Guid.NewGuid(), OrderId = order.Id, ProductId = variant.ProductId, VariantId = variant.Id,
                    ProductName = "K-II", Sku = variant.Sku, UnitPrice = 10m, Quantity = qty, LineTotal = 10m * qty,
                    DateCreated = now,
                });

            db.StoreRefunds.Add(new StoreRefund
            {
                Id = Guid.NewGuid(), OrderId = shipped.Id, Amount = 10m, Reason = "Broken",
                Status = StoreRefundStatus.Succeeded, DateCreated = now, CompletedUtc = now,
            });
            db.StoreRefunds.Add(new StoreRefund
            {
                Id = Guid.NewGuid(), OrderId = shipped.Id, Amount = 5m, Reason = "Pending one",
                Status = StoreRefundStatus.Pending, Attempt = 2, DateCreated = now,
            });

            // Low stock: 3 free is at the threshold of 3 and counts; 4 does not.
            StoreTestData.Variant(db, admin, onHand: 3, sku: "AT-3");
            StoreTestData.Variant(db, admin, onHand: 4, sku: "AT-4");
            await db.SaveChangesAsync();
        }

        var controller = new AdminStoreDashboardController(sqlite.Factory);
        var d = (StoreDashboardRecord)((OkObjectResult)(await controller.Get(30, default)).Result!).Value!;

        Assert.Equal((2, 1, 1), (d.OrdersToPack, d.OrdersToShip, d.OrdersNeedingAttention));
        Assert.Equal((185m, 10m, 175m), (d.Sales.GrossUsd, d.Sales.RefundedUsd, d.Sales.NetUsd));
        Assert.Equal(4, d.OrdersInRange);
        Assert.Equal(3, d.UnitsHeldByOpenCheckouts);
        Assert.Contains(d.LowStock, r => r.Sku == "AT-3");
        Assert.DoesNotContain(d.LowStock, r => r.Sku == "AT-4");
        Assert.False(d.StoreIsOn);
    }
}
