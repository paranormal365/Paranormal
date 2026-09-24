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
/// A cart becoming an order (storefront S4.7), on a real database: stock and the code held with the
/// order or not at all, a second Continue reusing the same order, and the sentences a buyer hears.
/// </summary>
public sealed class StoreCheckoutServiceTests : IAsyncLifetime
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
        await SetAsync(SiteSettingKeys.StoreShipFromStreet, "1 Depot Road");
        await SetAsync(SiteSettingKeys.StoreShipFromCity, "Franklin");
        await SetAsync(SiteSettingKeys.StoreShipFromState, "TN");
        await SetAsync(SiteSettingKeys.StoreShipFromZip, "37064");
        await SetAsync(SiteSettingKeys.StoreShippingFlatRateUsd, "7.95");
    }

    public Task DisposeAsync() => _sqlite.DisposeAsync().AsTask();

    private async Task SetAsync(string key, string value)
    {
        await using var db = await _sqlite.NewContextAsync();
        var row = await db.SiteSettings.FirstOrDefaultAsync(s => s.Key == key);
        if (row is null)
            db.SiteSettings.Add(new SiteSetting { Id = Guid.NewGuid(), Key = key, Value = value, DateCreated = StoreTestData.Now, CreatedByAppUserId = _admin.Id });
        else row.Value = value;
        await db.SaveChangesAsync();
    }

    private StoreCheckoutService Checkout(StorePaymentSetup? setup = null)
    {
        var clock = new Clock(() => _now);
        var site = Options.Create(new SiteIdentity { Name = "IsHaunted.com", BaseUrl = "https://test.local" });
        var mailer = new StoreOrderMailer(TestOutbox.WithoutMail(_sqlite.Factory), site);
        var alerts = new StoreAlerts(_sqlite.Factory, new PlatformMessageService(_sqlite.Factory), mailer, NullLogger<StoreAlerts>.Instance, clock);
        var payments = new StoreOrderPayments(_sqlite.Factory, _stripe, _tax, mailer, alerts, NullLogger<StoreOrderPayments>.Instance, clock);
        return new StoreCheckoutService(_sqlite.Factory, _stripe, _tax, payments, alerts, setup ?? new StorePaymentSetup(true, false, false),
            Options.Create(new StripeOptions()), NullLogger<StoreCheckoutService>.Instance, clock);
    }

    private static string NewToken() => Microsoft.AspNetCore.WebUtilities.WebEncoders.Base64UrlEncode(
        System.Security.Cryptography.RandomNumberGenerator.GetBytes(32));

    private async Task<StoreProductVariant> VariantAsync(int onHand = 10, decimal price = 20m, bool categoryActive = true)
    {
        await using var db = await _sqlite.NewContextAsync();
        var admin = await db.AppUsers.SingleAsync(u => u.Id == _admin.Id);
        var v = StoreTestData.Variant(db, admin, onHand: onHand, price: price, categoryActive: categoryActive);
        await db.SaveChangesAsync();
        return v;
    }

    private async Task<StoreCartCaller> CartWithAsync(StoreProductVariant v, int quantity = 2, string? code = null)
    {
        var caller = StoreCartCaller.From(null, NewToken());
        await using var db = await _sqlite.NewContextAsync();
        var cart = new StoreCartService(db);
        Assert.Equal(StoreCartOutcome.Ok, (await cart.AddAsync(caller, v.Id, quantity)).Outcome);
        if (code is not null) Assert.Equal(StoreCartOutcome.Ok, (await cart.ApplyCouponAsync(caller, code)).Outcome);
        return caller;
    }

    private static StoreCheckoutRequest Request(string email = "sarah@example.com", string zip = "37203", string state = "TN",
        bool terms = true, string street = "13 Crossroads Lane")
        => new(email, new StoreAddressInput("Sarah Hollow", "615-555-0100", street, null, "Nashville", state, zip), null, null, terms, null);

    private async Task<(List<StoreOrder> Orders, StoreProductVariant Variant)> ReadAsync(Guid variantId)
    {
        await using var db = await _sqlite.NewContextAsync();
        return (await db.StoreOrders.AsNoTracking().Include(o => o.Items).ToListAsync(),
                await db.StoreProductVariants.AsNoTracking().SingleAsync(v => v.Id == variantId));
    }

    // ── placing ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task A_guest_checkout_holds_the_stock_and_starts_a_payment_for_the_servers_total()
    {
        var v = await VariantAsync(price: 20m);
        var caller = await CartWithAsync(v, 2);

        var result = await Checkout().PrepareAsync(caller, "203.0.113.9", Request());

        Assert.Equal(StoreCheckoutOutcome.Ok, result.Outcome);
        var p = result.Prepared!;
        Assert.Equal((40m, 0m, 7.95m, 3.84m, 51.79m), (p.Totals.Subtotal, p.Totals.Discount, p.Totals.Shipping, p.Totals.Tax, p.Totals.Total));
        Assert.EndsWith(FakeStoreStripeGateway.SecretSuffix, p.ClientSecret);
        Assert.True(p.FakeCheckout);
        var intent = _stripe.Intents[FakeStoreStripeGateway.IntentIdFor(p.OrderId)];
        Assert.Equal(5179, intent.AmountCents);
        Assert.Equal(p.OrderId.ToString(), intent.Metadata[StoreStripeKeys.Order]);

        var (orders, variant) = await ReadAsync(v.Id);
        var order = Assert.Single(orders);
        Assert.Equal((StoreOrderStatus.PendingPayment, "203.0.113.9", 2), (order.Status, order.PlacedFromIp, variant.StockReserved));
        Assert.Equal(intent.Metadata[StoreStripeKeys.TaxCalculation], order.StripeTaxCalculationId);
    }

    [Theory]
    [InlineData("not-an-email", "37203", "TN", true, "Enter a valid email address.")]
    [InlineData("sarah@example.com", "3720", "TN", true, "Enter a 5-digit ZIP code.")]
    [InlineData("sarah@example.com", "37203", "XX", true, "Choose a state.")]
    [InlineData("sarah@example.com", "37203", "TN", false, "You need to agree to the terms and conditions to place an order.")]
    public async Task What_was_typed_is_checked_before_anything_is_held(string email, string zip, string state, bool terms, string sentence)
    {
        var v = await VariantAsync();
        var result = await Checkout().PrepareAsync(await CartWithAsync(v), null, Request(email, zip, state, terms));

        Assert.Equal((StoreCheckoutOutcome.BadRequest, sentence), (result.Outcome, result.Sentence));
        Assert.Empty((await ReadAsync(v.Id)).Orders);
    }

    [Fact]
    public async Task The_card_form_offers_Link_only_when_the_store_does()
    {
        // Stripe shows Link's "save my information" inside a card-only form when Link is on for the
        // account; the page hides it unless the store's own setting says otherwise (first real run, 09/24).
        var v = await VariantAsync();
        var off = await Checkout().PrepareAsync(await CartWithAsync(v), null, Request());
        await SetAsync(SiteSettingKeys.StoreLinkEnabled, "true");
        var on = await Checkout().PrepareAsync(await CartWithAsync(await VariantAsync()), null, Request());

        Assert.False(off.Prepared!.AllowLink);
        Assert.True(on.Prepared!.AllowLink);
    }

    [Fact]
    public async Task A_paused_store_or_no_payment_keys_refuses_before_reserving()
    {
        var v = await VariantAsync();
        var caller = await CartWithAsync(v);

        var noKeys = await Checkout(new StorePaymentSetup(false, true, false)).PrepareAsync(caller, null, Request());
        await SetAsync(SiteSettingKeys.StoreCheckoutEnabled, "false");
        var paused = await Checkout().PrepareAsync(caller, null, Request());

        Assert.Equal((StoreCheckoutOutcome.Unavailable, StoreCheckoutSentences.PaymentsNotSetUp), (noKeys.Outcome, noKeys.Sentence));
        Assert.Equal((StoreCheckoutOutcome.Conflict, StoreCheckoutSentences.Paused), (paused.Outcome, paused.Sentence));
        var (orders, variant) = await ReadAsync(v.Id);
        Assert.Empty(orders);
        Assert.Equal(0, variant.StockReserved);
    }

    [Fact]
    public async Task Placement_is_refused_when_the_category_was_hidden_after_adding()
    {
        var v = await VariantAsync();
        var caller = await CartWithAsync(v);
        await using (var db = await _sqlite.NewContextAsync())
        {
            var category = await db.StoreProducts.Where(p => p.Id == v.ProductId).Select(p => p.CategoryId).SingleAsync();
            await db.StoreCategories.Where(c => c.Id == category).ExecuteUpdateAsync(s => s.SetProperty(c => c.IsActive, false));
        }

        var result = await Checkout().PrepareAsync(caller, null, Request());

        Assert.Equal(StoreCheckoutOutcome.Conflict, result.Outcome);
        Assert.Equal(StoreCheckoutSentences.ItemsChanged(StoreCartSentences.NoLongerAvailable), result.Sentence);
        Assert.Empty((await ReadAsync(v.Id)).Orders);
    }

    /// <summary>Somebody takes the last units between the check and the hold: the hold refuses, and nothing is placed.</summary>
    [Fact]
    public async Task Losing_the_race_for_the_stock_answers_only_n_left_and_places_nothing()
    {
        var race = new RunBeforeStatement("UPDATE", "StoreProductVariants");
        await _sqlite.DisposeAsync();
        _sqlite = await SqliteTestDb.CreateAsync(race);
        await using (var db = await _sqlite.NewContextAsync()) { _admin = StoreTestData.Person(db); await db.SaveChangesAsync(); }
        await InitializeSettingsAsync();
        var v = await VariantAsync(onHand: 2);
        var caller = await CartWithAsync(v, 2);

        race.Competitor = async () =>
        {
            await using var other = await _sqlite.NewContextAsync();
            await other.StoreProductVariants.Where(x => x.Id == v.Id).ExecuteUpdateAsync(s => s.SetProperty(x => x.StockReserved, 1));
        };
        var result = await Checkout().PrepareAsync(caller, null, Request());

        Assert.Equal(StoreCheckoutOutcome.Conflict, result.Outcome);
        Assert.StartsWith("Only 1 of ", result.Sentence);
        Assert.Empty((await ReadAsync(v.Id)).Orders);
    }

    private async Task InitializeSettingsAsync()
    {
        await SetAsync(SiteSettingKeys.StoreShipFromStreet, "1 Depot Road");
        await SetAsync(SiteSettingKeys.StoreShipFromCity, "Franklin");
        await SetAsync(SiteSettingKeys.StoreShipFromState, "TN");
        await SetAsync(SiteSettingKeys.StoreShipFromZip, "37064");
        await SetAsync(SiteSettingKeys.StoreShippingFlatRateUsd, "7.95");
    }

    [Fact]
    public async Task A_fourth_open_checkout_from_one_address_is_refused()
    {
        var v = await VariantAsync();
        for (var i = 0; i < StoreCheckoutRules.MaxOpenCheckoutsPerCaller; i++)
            Assert.Equal(StoreCheckoutOutcome.Ok, (await Checkout().PrepareAsync(await CartWithAsync(v, 1), "198.51.100.7", Request($"b{i}@example.com"))).Outcome);

        var fourth = await Checkout().PrepareAsync(await CartWithAsync(v, 1), "198.51.100.7", Request("new@example.com"));
        var sameEmail = await Checkout().PrepareAsync(await CartWithAsync(v, 1), "192.0.2.1", Request("b0@example.com"));

        Assert.Equal((StoreCheckoutOutcome.Conflict, StoreCheckoutSentences.TooManyOpen), (fourth.Outcome, fourth.Sentence));
        Assert.Equal(StoreCheckoutOutcome.Ok, sameEmail.Outcome);   // one open from that email is under the limit
    }

    // ── codes ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Placement_reserves_the_coupon_and_a_second_single_use_placement_is_refused()
    {
        string code;
        await using (var db = await _sqlite.NewContextAsync())
        {
            var admin = await db.AppUsers.SingleAsync(u => u.Id == _admin.Id);
            code = StoreTestData.Coupon(db, admin, percentOff: 10, maxRedemptions: 1, perBuyer: null).Code;
            await db.SaveChangesAsync();
        }
        var v = await VariantAsync();
        var first = await CartWithAsync(v, 1, code);
        var second = await CartWithAsync(v, 1, code);

        var a = await Checkout().PrepareAsync(first, null, Request("a@example.com"));
        var b = await Checkout().PrepareAsync(second, null, Request("b@example.com"));

        Assert.Equal(StoreCheckoutOutcome.Ok, a.Outcome);
        Assert.Equal(2m, a.Prepared!.Totals.Discount);
        Assert.Equal(StoreCheckoutOutcome.BadRequest, b.Outcome);   // re-checked before placing: the count already says used up
        Assert.Equal(StoreCouponMath.UsedUp, b.Sentence);
        await using var check = await _sqlite.NewContextAsync();
        Assert.Equal(1, (await check.StoreCoupons.SingleAsync(c => c.Code == code)).RedemptionCount);
    }

    [Fact]
    public async Task A_coupon_lowers_the_taxable_amount()
    {
        string code;
        await using (var db = await _sqlite.NewContextAsync())
        {
            var admin = await db.AppUsers.SingleAsync(u => u.Id == _admin.Id);
            code = StoreTestData.Coupon(db, admin, percentOff: 10, perBuyer: null).Code;
            await db.SaveChangesAsync();
        }
        var v = await VariantAsync(price: 59.99m);
        var result = await Checkout().PrepareAsync(await CartWithAsync(v, 1, code), null, Request());

        // $59.99 less 10% is $53.99; 8% of that is $4.32, and 8% of $7.95 shipping is $0.64.
        Assert.Equal((6.00m, 4.96m), (result.Prepared!.Totals.Discount, result.Prepared.Totals.Tax));
    }

    /// <summary>$80 of goods less 10% is $72: under a $75 threshold, so shipping is charged.</summary>
    [Fact]
    public async Task Free_shipping_is_judged_after_the_discount()
    {
        await SetAsync(SiteSettingKeys.StoreFreeShippingThresholdUsd, "75");
        string code;
        await using (var db = await _sqlite.NewContextAsync())
        {
            var admin = await db.AppUsers.SingleAsync(u => u.Id == _admin.Id);
            code = StoreTestData.Coupon(db, admin, percentOff: 10, perBuyer: null).Code;
            await db.SaveChangesAsync();
        }
        var v = await VariantAsync(price: 80m);

        var result = await Checkout().PrepareAsync(await CartWithAsync(v, 1, code), null, Request());

        Assert.Equal((8.00m, 7.95m), (result.Prepared!.Totals.Discount, result.Prepared.Totals.Shipping));
    }

    [Fact]
    public async Task Zero_total_marks_paid_synchronously_without_a_payment()
    {
        await SetAsync(SiteSettingKeys.StoreShippingFlatRateUsd, "0");
        string code;
        await using (var db = await _sqlite.NewContextAsync())
        {
            var admin = await db.AppUsers.SingleAsync(u => u.Id == _admin.Id);
            code = StoreTestData.Coupon(db, admin, percentOff: 100, perBuyer: null).Code;
            await db.SaveChangesAsync();
        }
        var v = await VariantAsync();

        var result = await Checkout().PrepareAsync(await CartWithAsync(v, 1, code), null, Request());

        Assert.True(result.Prepared!.PaidWithoutCharge);
        Assert.Null(result.Prepared.ClientSecret);
        Assert.Empty(_stripe.Intents);
        Assert.Equal(StoreOrderStatus.Paid, Assert.Single((await ReadAsync(v.Id)).Orders).Status);
    }

    // ── Continue, again ──────────────────────────────────────────────────────

    [Fact]
    public async Task Second_prepare_for_the_last_unit_reuses_the_order_instead_of_saying_sold_out()
    {
        var v = await VariantAsync(onHand: 1);
        var caller = await CartWithAsync(v, 1);

        var a = await Checkout().PrepareAsync(caller, null, Request());
        var b = await Checkout().PrepareAsync(caller, null, Request());

        Assert.Equal((StoreCheckoutOutcome.Ok, StoreCheckoutOutcome.Ok), (a.Outcome, b.Outcome));
        Assert.Equal(a.Prepared!.OrderId, b.Prepared!.OrderId);
        Assert.Equal(a.Prepared.ClientSecret, b.Prepared.ClientSecret);
        var (orders, variant) = await ReadAsync(v.Id);
        Assert.Single(orders);
        Assert.Equal(1, variant.StockReserved);
    }

    [Fact]
    public async Task Reuse_extends_the_reservation_and_rewrites_the_order_to_what_is_charged()
    {
        var v = await VariantAsync(price: 20m);
        var caller = await CartWithAsync(v, 1);
        var a = await Checkout().PrepareAsync(caller, null, Request());

        _now = _now.AddMinutes(5);
        await SetAsync(SiteSettingKeys.StoreShippingFlatRateUsd, "9.95");
        var b = await Checkout().PrepareAsync(caller, null, Request());

        Assert.True(b.Prepared!.ReservationExpiresUtc > a.Prepared!.ReservationExpiresUtc);
        Assert.Equal(9.95m, b.Prepared.Totals.Shipping);
        var order = Assert.Single((await ReadAsync(v.Id)).Orders);
        Assert.Equal((9.95m, b.Prepared.Totals.Total), (order.ShippingAmount, order.Total));
        Assert.Equal(StoreMoney.Cents(order.Total), _stripe.Intents[order.StripePaymentIntentId!].AmountCents);
        Assert.Equal(order.StripeTaxCalculationId, _stripe.Intents[order.StripePaymentIntentId!].Metadata[StoreStripeKeys.TaxCalculation]);
    }

    [Fact]
    public async Task Unexpected_state_on_update_reverts_the_row()
    {
        var v = await VariantAsync();
        var caller = await CartWithAsync(v, 1);
        var a = await Checkout().PrepareAsync(caller, null, Request());
        var before = Assert.Single((await ReadAsync(v.Id)).Orders);

        await SetAsync(SiteSettingKeys.StoreShippingFlatRateUsd, "12.00");
        _stripe.UpdateIsUnexpectedState = true;
        var b = await Checkout().PrepareAsync(caller, null, Request());

        Assert.Equal((StoreCheckoutOutcome.Conflict, StoreCheckoutSentences.EarlierPaymentGoingThrough), (b.Outcome, b.Sentence));
        var after = Assert.Single((await ReadAsync(v.Id)).Orders);
        Assert.Equal((before.Total, before.ShippingAmount, before.StripeTaxCalculationId), (after.Total, after.ShippingAmount, after.StripeTaxCalculationId));
    }

    [Fact]
    public async Task A_changed_cart_cancels_the_old_order_at_Stripe_and_places_a_new_one()
    {
        var v = await VariantAsync(onHand: 5);
        var caller = await CartWithAsync(v, 1);
        var a = await Checkout().PrepareAsync(caller, null, Request());
        await using (var db = await _sqlite.NewContextAsync())
            await new StoreCartService(db).SetQuantityAsync(caller, v.Id, 2);

        var b = await Checkout().PrepareAsync(caller, null, Request());

        Assert.NotEqual(a.Prepared!.OrderId, b.Prepared!.OrderId);
        Assert.Contains(FakeStoreStripeGateway.IntentIdFor(a.Prepared.OrderId), _stripe.Cancelled);
        var (orders, variant) = await ReadAsync(v.Id);
        Assert.Equal(StoreOrderStatus.Cancelled, orders.Single(o => o.Id == a.Prepared.OrderId).Status);
        Assert.Equal(2, variant.StockReserved);
    }

    [Fact]
    public async Task Restart_while_the_old_intent_is_confirming_answers_409_and_creates_nothing()
    {
        var v = await VariantAsync(onHand: 5);
        var caller = await CartWithAsync(v, 1);
        var a = await Checkout().PrepareAsync(caller, null, Request());
        await using (var db = await _sqlite.NewContextAsync())
            await new StoreCartService(db).SetQuantityAsync(caller, v.Id, 2);
        _stripe.CancelAnswer = StripeCancelOutcome.StillProcessing;

        var b = await Checkout().PrepareAsync(caller, null, Request());

        Assert.Equal((StoreCheckoutOutcome.Conflict, StoreCheckoutSentences.EarlierPaymentProcessing), (b.Outcome, b.Sentence));
        Assert.Equal(a.Prepared!.OrderId, b.StillProcessingOrderId);
        var (orders, variant) = await ReadAsync(v.Id);
        Assert.Single(orders);
        Assert.Equal(1, variant.StockReserved);
    }

    [Fact]
    public async Task A_failed_intent_leaves_a_placed_order_that_the_next_attempt_finishes()
    {
        var v = await VariantAsync();
        var caller = await CartWithAsync(v, 1);
        _stripe.CreateFails = true;
        var a = await Checkout().PrepareAsync(caller, null, Request());
        Assert.Equal((StoreCheckoutOutcome.Unavailable, StoreCheckoutService.StripeDidNotAnswer), (a.Outcome, a.Sentence));
        Assert.Null(Assert.Single((await ReadAsync(v.Id)).Orders).StripePaymentIntentId);

        _stripe.CreateFails = false;
        var b = await Checkout().PrepareAsync(caller, null, Request());

        Assert.Equal(StoreCheckoutOutcome.Ok, b.Outcome);
        var order = Assert.Single((await ReadAsync(v.Id)).Orders);
        Assert.NotNull(order.StripePaymentIntentId);
        Assert.Contains($"store-order-{order.Id:N}", _stripe.CreateKeys);
    }

    // ── sales tax ────────────────────────────────────────────────────────────

    [Theory]
    [InlineData(StoreTaxFailure.BuyerAddress, StoreCheckoutOutcome.BadRequest, "We couldn't match that ZIP code to TN — check the address.")]
    [InlineData(StoreTaxFailure.Configuration, StoreCheckoutOutcome.Unavailable, StoreCheckoutSentences.OrderingPaused)]
    [InlineData(StoreTaxFailure.Transient, StoreCheckoutOutcome.Unavailable, StoreCheckoutSentences.TaxUnavailable)]
    public async Task A_tax_refusal_is_answered_in_the_buyers_words_and_holds_nothing(StoreTaxFailure failure, StoreCheckoutOutcome outcome, string sentence)
    {
        var v = await VariantAsync();
        _tax.Refuse = failure;

        var result = await Checkout().PrepareAsync(await CartWithAsync(v), null, Request());

        Assert.Equal((outcome, sentence), (result.Outcome, result.Sentence));
        var (orders, variant) = await ReadAsync(v.Id);
        Assert.Empty(orders);
        Assert.Equal(0, variant.StockReserved);
    }

    [Fact]
    public async Task No_ship_from_address_means_no_tax_and_no_order()
    {
        await using (var db = await _sqlite.NewContextAsync())
            await db.SiteSettings.Where(s => s.Key == SiteSettingKeys.StoreShipFromZip).ExecuteDeleteAsync();
        var v = await VariantAsync();

        var result = await Checkout().PrepareAsync(await CartWithAsync(v), null, Request());

        Assert.Equal((StoreCheckoutOutcome.Unavailable, StoreCheckoutSentences.TaxUnavailable), (result.Outcome, result.Sentence));
    }

    [Theory]
    [InlineData(100001)]
    [InlineData(999999)]
    [InlineData(1234567)]
    public void The_card_statement_suffix_is_one_Stripe_takes(int orderNumber)
    {
        // A bare order number passed every fake run and Stripe refused the first real payment with
        // it: "The statement descriptor must contain at least one Latin character" (09/24).
        var suffix = Ben.Data.WebApi.Services.Store.StoreCheckoutService.StatementSuffix(orderNumber);
        Assert.Null(FakeStoreStripeGateway.StatementSuffixProblem(suffix));
        Assert.Contains(orderNumber.ToString(), suffix);
        Assert.True(suffix.Length <= 12, "short enough to fit beside the business's name in 22");
        Assert.NotNull(FakeStoreStripeGateway.StatementSuffixProblem(orderNumber.ToString()));
    }
}
