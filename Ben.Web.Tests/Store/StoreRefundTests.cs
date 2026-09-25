using Ben.Data.Common;
using Ben.Data.Common.Enums;
using Ben.Data.Common.Mail;
using Ben.Data.Source.Entities;
using Ben.Data.WebApi.Services;
using Ben.Data.WebApi.Services.Billing.StripeIntegration;
using Ben.Data.WebApi.Services.Scheduling;
using Ben.Data.WebApi.Services.Store;
using Ben.Service.Models.Store;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Ben.Web.Tests.Store;

/// <summary>
/// Refunds (storefront S5.3, plan §5.7): the row before Stripe, nothing moving until Stripe says
/// succeeded, one completion however the news arrives, a fresh key on every retry, and the tax
/// filing following.
/// </summary>
public sealed class StoreRefundTests : IAsyncLifetime
{
    private SqliteTestDb _sqlite = null!;
    private AppUser _admin = null!;
    private readonly FakeStoreStripeGateway _stripe = new();
    private readonly FakeStoreTaxService _tax = new();
    private DateTime _now = DateTime.UtcNow;

    private sealed class Clock(Func<DateTime> now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(now(), TimeSpan.Zero);
    }

    public async Task InitializeAsync()
    {
        StoreAlerts.ResetHourlyLimits();
        _sqlite = await SqliteTestDb.CreateAsync();
        await using var db = await _sqlite.NewContextAsync();
        _admin = StoreTestData.Person(db);
        await db.SaveChangesAsync();
    }

    public Task DisposeAsync() => _sqlite.DisposeAsync().AsTask();

    private (StoreRefundService Refunds, StoreOrderTransitions Desk, StoreOrderPayments Payments) Services()
    {
        var site = Options.Create(new SiteIdentity { Name = "IsHaunted.com", BaseUrl = "https://test.local" });
        var mailer = new StoreOrderMailer(TestOutbox.WithoutMail(_sqlite.Factory), site);
        var alerts = new StoreAlerts(_sqlite.Factory, new PlatformMessageService(_sqlite.Factory), mailer, NullLogger<StoreAlerts>.Instance);
        var clock = new Clock(() => _now);
        var payments = new StoreOrderPayments(_sqlite.Factory, _stripe, _tax, mailer, alerts, NullLogger<StoreOrderPayments>.Instance);
        var refunds = new StoreRefundService(_sqlite.Factory, _stripe, _tax, mailer, alerts, NullLogger<StoreRefundService>.Instance, clock);
        return (refunds, new StoreOrderTransitions(_sqlite.Factory, mailer, alerts, refunds, payments, clock), payments);
    }

    private StoreRefundService Refunds => Services().Refunds;

    /// <summary>A paid order: 2 × $20 at 8% tax ($3.20), $5 shipping — $48.20 — with its tax filed.</summary>
    private sealed record Paid(Guid OrderId, Guid ItemId, Guid VariantId, string Pi);

    private async Task<Paid> PaidAsync(bool taxFiled = true, int onHand = 3, string? pi = null)
    {
        await using var db = await _sqlite.NewContextAsync();
        var admin = await db.AppUsers.SingleAsync(u => u.Id == _admin.Id);
        var v = StoreTestData.Variant(db, admin, onHand: onHand, price: 20m);
        var order = StoreTestData.Order(db, StoreOrderStatus.Paid);
        order.Subtotal = 40m; order.ShippingAmount = 5m; order.TaxAmount = 3.20m; order.Total = 48.20m;
        order.PaidUtc = _now.AddHours(-1);
        order.StripePaymentIntentId = pi ?? FakeStoreStripeGateway.IntentIdFor(order.Id);
        order.StripeTaxCalculationId = "taxcalc_1";
        order.StripeTaxTransactionId = taxFiled ? "tax_txn_1" : null;
        var item = new StoreOrderItem
        {
            Id = Guid.NewGuid(), OrderId = order.Id, ProductId = v.ProductId, VariantId = v.Id, ProductName = "K-II", Sku = v.Sku,
            UnitPrice = 20m, Quantity = 2, LineTotal = 40m, TaxAmount = 3.20m, DateCreated = _now,
        };
        db.StoreOrderItems.Add(item);
        await db.SaveChangesAsync();
        return new Paid(order.Id, item.Id, v.Id, order.StripePaymentIntentId);
    }

    private async Task<(StoreOrder Order, List<StoreRefund> Refunds, int Stock, int Letters)> ReadAsync(Paid p)
    {
        await using var db = await _sqlite.NewContextAsync();
        var order = await db.StoreOrders.AsNoTracking().Include(o => o.Items).SingleAsync(o => o.Id == p.OrderId);
        var refunds = await db.StoreRefunds.AsNoTracking().Include(r => r.Items).Where(r => r.OrderId == p.OrderId).ToListAsync();
        var stock = await db.StoreProductVariants.AsNoTracking().Where(v => v.Id == p.VariantId).Select(v => v.StockOnHand).SingleAsync();
        var letters = await db.OutboxEmails.CountAsync(e => e.Kind == MailKinds.StoreOrderRefunded.Key);
        return (order, refunds, stock, letters);
    }

    private static StoreRefundRequest Items(Paid p, int quantity, bool restock = true, string reason = "Arrived damaged")
        => new(null, [new StoreRefundLine(p.ItemId, quantity)], reason, restock);

    private static StripeWebhookEvent RefundEvent(string type, string refundId, string status, long cents, string? pi,
        Guid? ours = null, string? failure = null)
        => new(type, pi, null, null, refundId, cents, status, failure,
            ours is { } id ? new Dictionary<string, string> { [StoreStripeKeys.Refund] = id.ToString() } : new Dictionary<string, string>(),
            new Dictionary<string, string>());

    // ── Asking ───────────────────────────────────────────────────────────────

    [Fact]
    public async Task A_full_refund_by_items_restocks_counts_writes_and_reverses_the_whole_filing()
    {
        var p = await PaidAsync();
        var attempt = await Refunds.RefundAsync(p.OrderId, new StoreRefundRequest(null, [new StoreRefundLine(p.ItemId, 2)], "Wrong size", true), _admin.Id);
        // Items alone are 43.20; the rest (shipping) by amount after.
        var rest = await Refunds.RefundAsync(p.OrderId, new StoreRefundRequest(5m, null, "Shipping back", false), _admin.Id);

        var (order, refunds, stock, letters) = await ReadAsync(p);
        Assert.Equal((StoreRefundOutcomeKind.Completed, StoreRefundOutcomeKind.Completed), (attempt.Kind, rest.Kind));
        Assert.Equal((48.20m, StoreOrderStatus.Refunded), (order.RefundedAmount, order.Status));
        Assert.Equal(43.20m, refunds.Single(r => r.Items.Count > 0).Amount);
        Assert.Equal((2, 2), (order.Items.Single().QuantityRefunded, order.Items.Single().QuantityRestocked));
        Assert.Equal(5, stock);
        Assert.Equal(2, letters);
        Assert.All(_tax.Reversed, r => Assert.Equal(StoreTaxReversalMode.Partial, r.Mode));   // neither was the whole order in one go
    }

    [Fact]
    public async Task One_refund_of_the_whole_order_files_a_full_reversal()
    {
        var p = await PaidAsync();
        var (result, _) = await Services().Desk.CancelAsync(p.OrderId, new CancelStoreOrderRequest("Buyer changed their mind", true), _admin.Id);

        var (order, refunds, stock, _) = await ReadAsync(p);
        Assert.True(result.Ok);
        Assert.Equal((StoreOrderStatus.Cancelled, 48.20m), (order.Status, Assert.Single(refunds).Amount));
        Assert.Equal(5, stock);
        Assert.Equal(StoreTaxReversalMode.Full, Assert.Single(_tax.Reversed).Mode);
        await using var db = await _sqlite.NewContextAsync();
        Assert.True(await db.StoreStockMovements.AnyAsync(m => m.VariantId == p.VariantId && m.Reason == StoreStockReason.Cancelled));
    }

    [Fact]
    public async Task Second_refund_to_zero_is_a_partial_reversal()
    {
        var p = await PaidAsync();
        await Refunds.RefundAsync(p.OrderId, new StoreRefundRequest(10m, null, "Scuffed", false), _admin.Id);
        await Refunds.RefundAsync(p.OrderId, new StoreRefundRequest(38.20m, null, "Returned", false), _admin.Id);

        Assert.Equal([(StoreTaxReversalMode.Partial, 1000L), (StoreTaxReversalMode.Partial, 3820L)],
            _tax.Reversed.Select(r => (r.Mode, r.FlatAmountCents)));
        Assert.Equal(StoreOrderStatus.Refunded, (await ReadAsync(p)).Order.Status);
    }

    [Theory]
    [InlineData(null, "Only a paid order can be refunded.")]
    [InlineData("", "A refund needs a reason")]
    [InlineData("amount-restock", "can't restock")]
    [InlineData("too-much", "$48.20 still refundable")]
    [InlineData("seed", "not paid through Stripe")]
    public async Task Refusals_say_what_is_wrong_and_change_nothing(string? shape, string words)
    {
        var p = await PaidAsync(pi: shape == "seed" ? "seed_pi_1" : null);
        if (shape is null)
        {
            await using var db = await _sqlite.NewContextAsync();
            await db.StoreOrders.Where(o => o.Id == p.OrderId).ExecuteUpdateAsync(s => s.SetProperty(o => o.PaidUtc, (DateTime?)null));
        }
        var request = shape switch
        {
            "" => new StoreRefundRequest(5m, null, " ", false),
            "amount-restock" => new StoreRefundRequest(5m, null, "x", true),
            "too-much" => new StoreRefundRequest(50m, null, "x", false),
            _ => new StoreRefundRequest(5m, null, "x", false),
        };

        var attempt = await Refunds.RefundAsync(p.OrderId, request, _admin.Id);

        Assert.Equal(StoreRefundOutcomeKind.Refused, attempt.Kind);
        Assert.Contains(words, attempt.Sentence);
        Assert.Empty((await ReadAsync(p)).Refunds);
        Assert.Empty(_stripe.Refunds);
    }

    [Fact]
    public async Task A_pending_refund_is_counted_against_the_balance_and_moves_nothing()
    {
        var p = await PaidAsync();
        _stripe.RefundStatus = "pending";

        var attempt = await Refunds.RefundAsync(p.OrderId, Items(p, 2), _admin.Id);
        var more = await Refunds.RefundAsync(p.OrderId, new StoreRefundRequest(10m, null, "x", false), _admin.Id);

        var (order, refunds, stock, letters) = await ReadAsync(p);
        Assert.Equal(StoreRefundOutcomeKind.Pending, attempt.Kind);
        Assert.Equal(StoreOrderDeskSentences.RefundPending, attempt.Sentence);
        Assert.Contains("$5.00 still refundable", more.Sentence);         // 48.20 − 43.20 pending
        Assert.Equal((0m, 0, 3, 0), (order.RefundedAmount, order.Items.Single().QuantityRefunded, stock, letters));
        Assert.NotNull(Assert.Single(refunds).StripeRefundId);
        Assert.Empty(_tax.Reversed);
    }

    [Fact]
    public async Task A_pending_refund_completes_once_on_its_succeeded_event()
    {
        var p = await PaidAsync();
        _stripe.RefundStatus = "pending";
        var attempt = await Refunds.RefundAsync(p.OrderId, Items(p, 1), _admin.Id);
        var stripeId = (await ReadAsync(p)).Refunds.Single().StripeRefundId!;

        var done = RefundEvent("refund.updated", stripeId, "succeeded", 2160, p.Pi, attempt.RefundId);
        await Refunds.RecordDashboardRefundAsync(done);
        await Refunds.RecordDashboardRefundAsync(done);   // redelivered

        var (order, refunds, stock, letters) = await ReadAsync(p);
        Assert.Equal((21.60m, 4, 1), (order.RefundedAmount, stock, letters));
        Assert.Equal(StoreRefundStatus.Succeeded, Assert.Single(refunds).Status);
        Assert.Single(_tax.Reversed);
    }

    [Fact]
    public async Task Our_refund_and_its_own_webhook_leave_one_row_and_one_reversal()
    {
        var p = await PaidAsync();
        var attempt = await Refunds.RefundAsync(p.OrderId, new StoreRefundRequest(10m, null, "Scuffed", false), _admin.Id);
        var stripeId = (await ReadAsync(p)).Refunds.Single().StripeRefundId!;

        await Refunds.RecordDashboardRefundAsync(RefundEvent("refund.created", stripeId, "succeeded", 1000, p.Pi, attempt.RefundId));

        var (order, refunds, _, letters) = await ReadAsync(p);
        Assert.Equal((10m, 1), (order.RefundedAmount, letters));
        Assert.Single(refunds);
        Assert.Single(_tax.Reversed);
    }

    [Fact]
    public async Task Stripe_refusing_marks_the_row_failed_and_moves_nothing()
    {
        var p = await PaidAsync();
        _stripe.RefundRefusal = "Charge already refunded";

        var attempt = await Refunds.RefundAsync(p.OrderId, Items(p, 2), _admin.Id);

        var (order, refunds, stock, letters) = await ReadAsync(p);
        Assert.Equal(StoreRefundOutcomeKind.StripeRefused, attempt.Kind);
        Assert.Contains("Charge already refunded", attempt.Sentence);
        Assert.Equal(StoreRefundStatus.Failed, Assert.Single(refunds).Status);
        Assert.Equal((0m, 3, 0), (order.RefundedAmount, stock, letters));
    }

    [Fact]
    public async Task A_refund_failing_later_at_Stripe_is_marked_failed_and_nothing_moved()
    {
        var p = await PaidAsync();
        _stripe.RefundStatus = "pending";
        var attempt = await Refunds.RefundAsync(p.OrderId, Items(p, 2), _admin.Id);
        var stripeId = (await ReadAsync(p)).Refunds.Single().StripeRefundId!;

        await Refunds.RecordDashboardRefundAsync(RefundEvent("refund.failed", stripeId, "failed", 4320, p.Pi, attempt.RefundId, "expired_or_canceled_card"));

        var (order, refunds, stock, letters) = await ReadAsync(p);
        var refund = Assert.Single(refunds);
        Assert.Equal((StoreRefundStatus.Failed, "expired_or_canceled_card"), (refund.Status, refund.FailureReason));
        Assert.Equal((0m, 3, 0), (order.RefundedAmount, stock, letters));
        Assert.Empty(_tax.Reversed);
    }

    [Fact]
    public async Task A_dashboard_refund_is_recorded_once_by_its_payment()
    {
        var p = await PaidAsync();
        var e = RefundEvent("refund.created", "re_dash_1", "succeeded", 700, p.Pi);

        await Refunds.RecordDashboardRefundAsync(e);
        await Refunds.RecordDashboardRefundAsync(e);

        var (order, refunds, stock, _) = await ReadAsync(p);
        var refund = Assert.Single(refunds);
        Assert.Equal((7m, StoreRefundService.DashboardReason, false, StoreRefundStatus.Succeeded), (refund.Amount, refund.Reason, refund.Restock, refund.Status));
        Assert.Equal((7m, 3), (order.RefundedAmount, stock));
    }

    [Fact]
    public async Task A_refund_of_something_that_is_not_a_store_order_is_ignored()
    {
        var p = await PaidAsync();
        await Refunds.RecordDashboardRefundAsync(RefundEvent("refund.created", "re_sub_1", "succeeded", 1999, "pi_subscription_charge"));
        Assert.Empty((await ReadAsync(p)).Refunds);
    }

    // ── Trying again ─────────────────────────────────────────────────────────

    [Fact]
    public async Task A_stuck_refund_that_Stripe_already_has_is_adopted_not_created_again()
    {
        var p = await PaidAsync();
        // A crash after Stripe's yes and before our save: Stripe lists it, our row has no id yet.
        _stripe.RefundStatus = "pending";
        var attempt = await Refunds.RefundAsync(p.OrderId, new StoreRefundRequest(10m, null, "Scuffed", false), _admin.Id);
        await using (var db = await _sqlite.NewContextAsync())
            await db.StoreRefunds.ExecuteUpdateAsync(s => s.SetProperty(r => r.StripeRefundId, (string?)null));
        _stripe.RefundStatus = "succeeded";
        _now = _now.AddMinutes(3);
        var created = _stripe.Refunds.Count;

        var retry = await Refunds.RetryAsync(attempt.RefundId!.Value, _admin.Id);

        Assert.Equal(StoreRefundOutcomeKind.Completed, retry.Kind);
        Assert.Equal(created, _stripe.Refunds.Count);
        Assert.Equal(10m, (await ReadAsync(p)).Order.RefundedAmount);
    }

    [Fact]
    public async Task A_failed_refund_is_tried_again_with_a_new_key()
    {
        var p = await PaidAsync();
        _stripe.RefundRefusal = "Your card's issuer declined";
        var attempt = await Refunds.RefundAsync(p.OrderId, new StoreRefundRequest(10m, null, "Scuffed", false), _admin.Id);
        _stripe.RefundRefusal = null;

        var retry = await Refunds.RetryAsync(attempt.RefundId!.Value, _admin.Id);

        var refund = Assert.Single((await ReadAsync(p)).Refunds);
        Assert.Equal(StoreRefundOutcomeKind.Completed, retry.Kind);
        Assert.Equal((2, StoreRefundStatus.Succeeded), (refund.Attempt, refund.Status));
        var key = Assert.Single(_stripe.Refunds).IdempotencyKey;
        Assert.EndsWith("-2", key);
    }

    [Fact]
    public async Task A_refund_made_before_the_tax_filing_is_reversed_after_it_lands()
    {
        var p = await PaidAsync(taxFiled: false);
        await Refunds.RefundAsync(p.OrderId, new StoreRefundRequest(10m, null, "Scuffed", false), _admin.Id);
        Assert.Empty(_tax.Reversed);

        await using (var db = await _sqlite.NewContextAsync())
            await db.StoreOrders.Where(o => o.Id == p.OrderId).ExecuteUpdateAsync(s => s.SetProperty(o => o.StripeTaxTransactionId, "tax_txn_late"));
        var (refunds, _, payments) = Services();
        await new StoreTaxRetryJob(_sqlite.Factory, payments, refunds).RunAsync(default);

        var reversed = Assert.Single(_tax.Reversed);
        Assert.Equal(("tax_txn_late", 1000L), (reversed.TransactionId, reversed.FlatAmountCents));
        Assert.NotNull(Assert.Single((await ReadAsync(p)).Refunds).StripeTaxReversalId);
    }
}
