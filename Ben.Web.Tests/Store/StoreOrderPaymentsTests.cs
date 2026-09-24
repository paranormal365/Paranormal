using Ben.Data.Common;
using Ben.Data.Common.Enums;
using Ben.Data.Common.Mail;
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
/// An order's payment, every way Stripe can report it (storefront S4.6), on a real database: each
/// transition is one conditional UPDATE and must survive redelivery, reordering and races.
/// </summary>
public sealed class StoreOrderPaymentsTests : IAsyncLifetime
{
    private SqliteTestDb _sqlite = null!;
    private AppUser _admin = null!;
    private readonly FakeStoreStripeGateway _stripe = new();
    private readonly FakeStoreTaxService _tax = new();

    public async Task InitializeAsync()
    {
        StoreAlerts.ResetHourlyLimits();
        await NewDatabaseAsync();
    }

    private async Task NewDatabaseAsync(params Microsoft.EntityFrameworkCore.Diagnostics.IInterceptor[] interceptors)
    {
        _sqlite = await SqliteTestDb.CreateAsync(interceptors);
        await using var db = await _sqlite.NewContextAsync();
        _admin = StoreTestData.Person(db);
        await db.SaveChangesAsync();
    }

    public Task DisposeAsync() => _sqlite.DisposeAsync().AsTask();

    private StoreOrderPayments Payments()
    {
        var site = Options.Create(new SiteIdentity { Name = "IsHaunted.com", BaseUrl = "https://test.local" });
        var mailer = new StoreOrderMailer(TestOutbox.WithoutMail(_sqlite.Factory), site);
        var alerts = new StoreAlerts(_sqlite.Factory, new PlatformMessageService(_sqlite.Factory), mailer, NullLogger<StoreAlerts>.Instance);
        return new StoreOrderPayments(_sqlite.Factory, _stripe, _tax, mailer, alerts, NullLogger<StoreOrderPayments>.Instance);
    }

    private sealed record Placed(Guid OrderId, Guid VariantId, Guid CartId, string Pi, string CalcId, decimal Total);

    /// <summary>A placed, unpaid order holding two of a variant with 5 on hand, in a cart, with a calculation and an intent.</summary>
    private async Task<Placed> PlaceAsync(int quantity = 2, bool withIntent = true, Guid? couponId = null, int onHand = 5)
    {
        await using var db = await _sqlite.NewContextAsync();
        var admin = await db.AppUsers.SingleAsync(u => u.Id == _admin.Id);
        var v = StoreTestData.Variant(db, admin, onHand: onHand, reserved: quantity, price: 20m);
        var cart = new StoreCart { Id = Guid.NewGuid(), GuestTokenHash = Guid.NewGuid().ToString("N") + Guid.NewGuid().ToString("N"), LastActivityUtc = StoreTestData.Now, DateCreated = StoreTestData.Now, CouponId = couponId };
        db.StoreCarts.Add(cart);
        db.StoreCartItems.Add(new StoreCartItem { Id = Guid.NewGuid(), CartId = cart.Id, VariantId = v.Id, Quantity = quantity, DateCreated = StoreTestData.Now });

        var calc = await _tax.CalculateAsync(new StoreTaxRequest([new StoreTaxLine(v.Sku, 2000 * quantity, quantity, "txcd_99999999")], 0,
            new StoreTaxAddress("1", null, "Nashville", "TN", "37203"), new StoreTaxAddress("2", null, "Franklin", "TN", "37064")));
        var order = StoreTestData.Order(db, StoreOrderStatus.PendingPayment);
        order.StoreCartId = cart.Id;
        order.CouponId = couponId;
        order.Subtotal = 20m * quantity;
        order.TaxAmount = calc.TaxCents / 100m;
        order.Total = order.Subtotal + order.TaxAmount;
        order.StripeTaxCalculationId = calc.CalculationId;
        order.StripePaymentIntentId = withIntent ? FakeStoreStripeGateway.IntentIdFor(order.Id) : null;
        order.ReservationExpiresUtc = DateTime.UtcNow.AddMinutes(15);
        db.StoreOrderItems.Add(new StoreOrderItem
        {
            Id = Guid.NewGuid(), OrderId = order.Id, ProductId = v.ProductId, VariantId = v.Id, ProductName = "K-II", Sku = v.Sku,
            UnitPrice = 20m, Quantity = quantity, LineTotal = 20m * quantity, TaxAmount = calc.TaxCents / 100m, DateCreated = StoreTestData.Now,
        });
        await db.SaveChangesAsync();
        return new Placed(order.Id, v.Id, cart.Id, order.StripePaymentIntentId ?? "", calc.CalculationId, order.Total);
    }

    private Task PayAsync(Placed p, long? amountCents = -1, string? pi = null, string? calc = null, string? charge = "ch_1")
        => Payments().MarkPaidAsync(p.OrderId, pi ?? p.Pi, charge, pi ?? p.Pi,
            amountCents == -1 ? StoreMoney.Cents(p.Total) : amountCents, calc ?? p.CalcId);

    private async Task<(StoreOrder Order, StoreProductVariant Variant, int Letters, List<StoreOrderEvent> Events)> ReadAsync(Placed p)
    {
        await using var db = await _sqlite.NewContextAsync();
        var order = await db.StoreOrders.AsNoTracking().Include(o => o.Items).SingleAsync(o => o.Id == p.OrderId);
        var variant = await db.StoreProductVariants.AsNoTracking().SingleAsync(v => v.Id == p.VariantId);
        var letters = await db.OutboxEmails.CountAsync(e => e.Kind == MailKinds.StoreOrderConfirmation.Key);
        var events = await db.StoreOrderEvents.AsNoTracking().Where(e => e.OrderId == p.OrderId).ToListAsync();
        return (order, variant, letters, events);
    }

    // ── paid ─────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Delivered_twice_is_fulfilled_once()
    {
        var p = await PlaceAsync();

        await PayAsync(p);
        await PayAsync(p);

        var (order, variant, letters, _) = await ReadAsync(p);
        Assert.Equal(StoreOrderStatus.Paid, order.Status);
        Assert.Equal((3, 0, 2), (variant.StockOnHand, variant.StockReserved, variant.UnitsSold));
        Assert.Equal(1, letters);
        Assert.Equal([p.CalcId], _tax.Committed);
        Assert.NotNull(order.StripeTaxTransactionId);
        Assert.False(order.NeedsAttention);
    }

    [Fact]
    public async Task Succeeded_with_a_different_intent_id_changes_nothing_but_the_flag()
    {
        var p = await PlaceAsync();

        await PayAsync(p, pi: "pi_somebody_elses");

        var (order, variant, letters, events) = await ReadAsync(p);
        Assert.Equal(StoreOrderStatus.PendingPayment, order.Status);
        Assert.Equal((5, 2, 0), (variant.StockOnHand, variant.StockReserved, variant.UnitsSold));
        Assert.Equal(0, letters);
        Assert.True(order.NeedsAttention);
        Assert.Equal(StoreOrderPayments.DifferentIntent("pi_somebody_elses"), order.AttentionReason);
        Assert.Contains(events, e => e.Kind == StoreOrderEventKind.AttentionRaised);
    }

    [Fact]
    public async Task Paid_after_cancel_with_stock_still_queues_the_letter_and_commits_tax()
    {
        var p = await PlaceAsync();
        await Payments().ReleaseAsync(p.OrderId, "Checkout expired", StoreOrderEventKind.ReservationExpired);

        await PayAsync(p);

        var (order, variant, letters, _) = await ReadAsync(p);
        Assert.Equal(StoreOrderStatus.Paid, order.Status);
        Assert.True(order.NeedsAttention);
        Assert.Equal(StoreOrderPayments.PaidAfterCancelWithStock, order.AttentionReason);
        Assert.Equal((3, 0), (variant.StockOnHand, variant.StockReserved));
        Assert.Equal(1, letters);
        Assert.Equal([p.CalcId], _tax.Committed);
    }

    [Fact]
    public async Task Paid_after_cancel_without_stock_says_no_tax_transaction_was_filed()
    {
        var p = await PlaceAsync();
        await Payments().ReleaseAsync(p.OrderId, "Checkout expired");
        await using (var db = await _sqlite.NewContextAsync())   // somebody else bought them meanwhile
            await db.StoreProductVariants.Where(v => v.Id == p.VariantId).ExecuteUpdateAsync(s => s.SetProperty(v => v.StockOnHand, 0));

        await PayAsync(p);

        var (order, _, letters, _) = await ReadAsync(p);
        Assert.Equal(StoreOrderStatus.Cancelled, order.Status);
        Assert.NotNull(order.PaidUtc);
        Assert.Equal(StoreOrderPayments.PaidAfterCancelNoStock, order.AttentionReason);
        Assert.Equal(0, letters);
        Assert.Empty(_tax.Committed);
        Assert.Null(order.StripeTaxTransactionId);
    }

    [Fact]
    public async Task Amount_mismatch_flags_the_order()
    {
        var p = await PlaceAsync();

        await PayAsync(p, amountCents: 1);

        var (order, _, letters, events) = await ReadAsync(p);
        Assert.Equal(StoreOrderStatus.Paid, order.Status);   // the money is taken and must be on record
        Assert.True(order.NeedsAttention);
        Assert.Equal(StoreOrderPayments.AmountMismatch(1, p.Total), order.AttentionReason);
        Assert.Contains(events, e => e.Kind == StoreOrderEventKind.AttentionRaised);
        Assert.Equal(1, letters);
    }

    [Fact]
    public async Task A_session_event_without_an_amount_does_not_flag_the_order()
    {
        var p = await PlaceAsync();
        await PayAsync(p, amountCents: null);
        Assert.False((await ReadAsync(p)).Order.NeedsAttention);
    }

    [Fact]
    public async Task A_null_charge_ref_is_tolerated()
    {
        var p = await PlaceAsync();
        await PayAsync(p, charge: null);
        Assert.Equal(StoreOrderStatus.Paid, (await ReadAsync(p)).Order.Status);
    }

    [Fact]
    public async Task Row_rewritten_after_confirm_commits_the_calculation_that_was_charged()
    {
        var p = await PlaceAsync();
        // A second tab rewrote the order to a new calculation after the first tab's payment was confirmed.
        var later = await _tax.CalculateAsync(new StoreTaxRequest([new StoreTaxLine("X", 99900, 1, "txcd_99999999")], 0,
            new StoreTaxAddress("1", null, "N", "TN", "37203"), new StoreTaxAddress("2", null, "F", "TN", "37064")));
        await using (var db = await _sqlite.NewContextAsync())
            await db.StoreOrders.Where(o => o.Id == p.OrderId).ExecuteUpdateAsync(s => s.SetProperty(o => o.StripeTaxCalculationId, later.CalculationId));

        await PayAsync(p, calc: p.CalcId);   // the intent names the calculation it was charged on

        var (order, _, _, _) = await ReadAsync(p);
        Assert.Equal([p.CalcId], _tax.Committed);
        Assert.Equal(p.CalcId, order.StripeTaxCalculationId);
        Assert.Equal(StoreOrderPayments.ChargedOnEarlierCalculation, order.AttentionReason);
    }

    [Fact]
    public async Task MarkPaid_removes_only_the_ordered_lines_from_the_cart()
    {
        var p = await PlaceAsync(quantity: 2);
        Guid other;
        await using (var db = await _sqlite.NewContextAsync())
        {
            var admin = await db.AppUsers.SingleAsync(u => u.Id == _admin.Id);
            other = StoreTestData.Variant(db, admin).Id;   // added in another tab after the checkout began
            db.StoreCartItems.Add(new StoreCartItem { Id = Guid.NewGuid(), CartId = p.CartId, VariantId = other, Quantity = 1, DateCreated = StoreTestData.Now });
            await db.StoreCartItems.Where(i => i.CartId == p.CartId && i.VariantId == p.VariantId)
                .ExecuteUpdateAsync(s => s.SetProperty(i => i.Quantity, 3));   // and one more of the same
            await db.SaveChangesAsync();
        }

        await PayAsync(p);

        await using var check = await _sqlite.NewContextAsync();
        var lines = await check.StoreCartItems.Where(i => i.CartId == p.CartId).ToListAsync();
        Assert.Equal(1, lines.Single(l => l.VariantId == p.VariantId).Quantity);
        Assert.Equal(1, lines.Single(l => l.VariantId == other).Quantity);
    }

    [Fact]
    public async Task MarkPaid_commits_tax_after_the_transaction()
    {
        var p = await PlaceAsync();
        _tax.Refuse = StoreTaxFailure.Transient;

        await PayAsync(p);

        var (order, _, letters, events) = await ReadAsync(p);
        Assert.Equal(StoreOrderStatus.Paid, order.Status);
        Assert.Equal(1, letters);
        Assert.Null(order.StripeTaxTransactionId);
        Assert.Equal(1, order.TaxCommitAttempts);
        Assert.Contains(events, e => e.Kind == StoreOrderEventKind.TaxTransactionFailed);
    }

    /// <summary>A refused outbox write takes the whole payment with it: no Paid order without its receipt.</summary>
    [Fact]
    public async Task The_letter_is_queued_through_the_outbox_in_the_same_transaction()
    {
        await _sqlite.DisposeAsync();
        var refuse = new RefuseOutboxWrites();
        await NewDatabaseAsync(refuse);
        var p = await PlaceAsync();
        refuse.Refusing = true;

        await Assert.ThrowsAnyAsync<Exception>(() => PayAsync(p));

        var (order, variant, _, _) = await ReadAsync(p);
        Assert.Equal(StoreOrderStatus.PendingPayment, order.Status);
        Assert.Equal((5, 2), (variant.StockOnHand, variant.StockReserved));
    }

    // ── processing / failed / expired / released ─────────────────────────────

    [Fact]
    public async Task Processing_holds_the_stock_and_after_succeeded_changes_nothing()
    {
        var p = await PlaceAsync();
        await Payments().RecordProcessingAsync(p.Pi);
        Assert.Null((await ReadAsync(p)).Order.ReservationExpiresUtc);

        await PayAsync(p);
        await Payments().RecordProcessingAsync(p.Pi);   // late and out of order

        var (order, _, _, events) = await ReadAsync(p);
        Assert.Equal(StoreOrderStatus.Paid, order.Status);
        Assert.Single(events, e => e.Kind == StoreOrderEventKind.PaymentProcessing);
    }

    [Fact]
    public async Task Failed_after_the_reservation_cancels_at_Stripe_before_releasing()
    {
        var p = await PlaceAsync();
        await using (var db = await _sqlite.NewContextAsync())
            await db.StoreOrders.Where(o => o.Id == p.OrderId).ExecuteUpdateAsync(s => s.SetProperty(o => o.ReservationExpiresUtc, DateTime.UtcNow.AddMinutes(-1)));

        await Payments().RecordFailureAsync(p.Pi, "card_declined");

        Assert.Contains(p.Pi, _stripe.Cancelled);
        var (order, variant, _, _) = await ReadAsync(p);
        Assert.Equal(StoreOrderStatus.Cancelled, order.Status);
        Assert.Equal(0, variant.StockReserved);
    }

    [Fact]
    public async Task Failed_within_the_reservation_leaves_the_buyer_free_to_try_another_card()
    {
        var p = await PlaceAsync();
        await Payments().RecordFailureAsync(p.Pi, "card_declined");

        Assert.Empty(_stripe.Cancelled);
        var (order, _, _, events) = await ReadAsync(p);
        Assert.Equal(StoreOrderStatus.PendingPayment, order.Status);
        Assert.Contains(events, e => e.Kind == StoreOrderEventKind.PaymentFailed && e.Note == "card_declined");
    }

    [Fact]
    public async Task Expiry_goes_on_past_a_checkout_Stripe_cannot_be_asked_about()
    {
        var stuck = await PlaceAsync();
        var fine = await PlaceAsync();
        await using (var db = await _sqlite.NewContextAsync())
            await db.StoreOrders.ExecuteUpdateAsync(s => s.SetProperty(o => o.ReservationExpiresUtc, DateTime.UtcNow.AddMinutes(-1)));
        _stripe.CancelThrowsFor = stuck.Pi;

        var released = await Payments().ExpireReservationsAsync();

        Assert.Equal(1, released);
        Assert.Equal(StoreOrderStatus.Cancelled, (await ReadAsync(fine)).Order.Status);
        Assert.Equal(StoreOrderStatus.PendingPayment, (await ReadAsync(stuck)).Order.Status);   // kept for the next pass

        _stripe.CancelThrowsFor = null;
        Assert.Equal(1, await Payments().ExpireReservationsAsync());
        Assert.Equal(StoreOrderStatus.Cancelled, (await ReadAsync(stuck)).Order.Status);
    }

    [Fact]
    public async Task Expiry_cancels_at_Stripe_before_releasing_and_leaves_a_payment_that_is_going_through()
    {
        var gone = await PlaceAsync();
        var paying = await PlaceAsync();
        var none = await PlaceAsync(withIntent: false);
        await using (var db = await _sqlite.NewContextAsync())
            await db.StoreOrders.ExecuteUpdateAsync(s => s.SetProperty(o => o.ReservationExpiresUtc, DateTime.UtcNow.AddMinutes(-1)));

        _stripe.CancelAnswer = StripeCancelOutcome.Cancelled;
        await Payments().CancelPendingOrderAsync(gone.OrderId, "Checkout expired");
        _stripe.CancelAnswer = StripeCancelOutcome.StillProcessing;
        var outcome = await Payments().CancelPendingOrderAsync(paying.OrderId, "Checkout expired");
        var direct = await Payments().CancelPendingOrderAsync(none.OrderId, "Checkout expired");

        Assert.Equal(StoreOrderStatus.Cancelled, (await ReadAsync(gone)).Order.Status);
        Assert.Equal(StoreOrderPayments.CancelOutcome.StillProcessing, outcome);
        Assert.Equal(StoreOrderStatus.PendingPayment, (await ReadAsync(paying)).Order.Status);
        Assert.Equal(StoreOrderPayments.CancelOutcome.NoIntent, direct);
        Assert.Equal(StoreOrderStatus.Cancelled, (await ReadAsync(none)).Order.Status);
        Assert.DoesNotContain(none.Pi, _stripe.Cancelled.Where(c => c.Length > 0));
    }

    [Fact]
    public async Task Release_twice_moves_stock_and_the_coupon_once()
    {
        Guid couponId;
        await using (var db = await _sqlite.NewContextAsync())
        {
            var admin = await db.AppUsers.SingleAsync(u => u.Id == _admin.Id);
            var coupon = StoreTestData.Coupon(db, admin, maxRedemptions: 5);
            coupon.RedemptionCount = 1;
            await db.SaveChangesAsync();
            couponId = coupon.Id;
        }
        var p = await PlaceAsync(couponId: couponId);
        await using (var db = await _sqlite.NewContextAsync())
        {
            var order = await db.StoreOrders.SingleAsync(o => o.Id == p.OrderId);
            db.StoreCouponRedemptions.Add(new StoreCouponRedemption
            {
                Id = Guid.NewGuid(), CouponId = couponId, OrderId = p.OrderId, BuyerEmailNormalized = order.BuyerEmailNormalized,
                DiscountAmount = 1m, RedeemedUtc = StoreTestData.Now,
            });
            await db.SaveChangesAsync();
        }

        Assert.True(await Payments().ReleaseAsync(p.OrderId, "Checkout expired"));
        Assert.False(await Payments().ReleaseAsync(p.OrderId, "Checkout expired"));
        await Payments().RecordCancelledAtStripeAsync(p.Pi);   // the webhook our cancel set off

        var (_, variant, _, _) = await ReadAsync(p);
        Assert.Equal(0, variant.StockReserved);
        await using var check = await _sqlite.NewContextAsync();
        Assert.Equal(0, (await check.StoreCoupons.SingleAsync(c => c.Id == couponId)).RedemptionCount);
        Assert.Equal(0, await check.StoreCouponRedemptions.CountAsync());
    }

    // ── dispatch ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task A_store_payment_is_marked_paid_through_fulfilment_never_the_subscription_path()
    {
        var p = await PlaceAsync();
        var fulfilment = new StripeFulfillmentService(_sqlite.Factory, NullLogger<StripeFulfillmentService>.Instance, store: Payments());

        await fulfilment.FulfillAsync(new StripeCompletedCheckout(p.Pi, p.Pi, null, null,
            new Dictionary<string, string> { [StoreStripeKeys.Order] = p.OrderId.ToString(), [StoreStripeKeys.TaxCalculation] = p.CalcId },
            ChargeRef: "ch_1", AmountReceivedCents: StoreMoney.Cents(p.Total)));

        Assert.Equal(StoreOrderStatus.Paid, (await ReadAsync(p)).Order.Status);
        await using var db = await _sqlite.NewContextAsync();
        Assert.Equal(0, await db.BillingLedgerEntries.CountAsync());
    }

    [Fact]
    public async Task A_missing_store_dependency_throws_instead_of_ignoring()
    {
        var fulfilment = new StripeFulfillmentService(_sqlite.Factory, NullLogger<StripeFulfillmentService>.Instance);
        await Assert.ThrowsAsync<InvalidOperationException>(() => fulfilment.FulfillAsync(new StripeCompletedCheckout("pi_1", "pi_1", null, null,
            new Dictionary<string, string> { [StoreStripeKeys.Order] = Guid.NewGuid().ToString() })));
    }
}
