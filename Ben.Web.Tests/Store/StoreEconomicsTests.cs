using Ben.Data.Common;
using Ben.Data.Common.Enums;
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
/// What a sale is worth to each side (store sellers, backlog 251, P9): the arithmetic, the snapshot
/// checkout fixes on each line, and Stripe's real fee read after payment.
/// </summary>
public sealed class StoreEconomicsTests : IAsyncLifetime
{
    private SqliteTestDb _sqlite = null!;
    private AppUser _admin = null!, _hazel = null!;
    private readonly FakeStoreStripeGateway _stripe = new();
    private readonly FakeStoreTaxService _tax = new();

    public async Task InitializeAsync()
    {
        StoreAlerts.ResetHourlyLimits();
        _sqlite = await SqliteTestDb.CreateAsync();
        await using var db = await _sqlite.NewContextAsync();
        _admin = StoreTestData.Person(db);
        _hazel = StoreTestData.Person(db, "hazel");
        await db.SaveChangesAsync();
        foreach (var (key, value) in new[] { (SiteSettingKeys.StoreShipFromStreet, "1 Depot Road"), (SiteSettingKeys.StoreShipFromCity, "Franklin"),
                     (SiteSettingKeys.StoreShipFromState, "TN"), (SiteSettingKeys.StoreShipFromZip, "37064"), (SiteSettingKeys.StoreShippingFlatRateUsd, "5.00") })
            db.SiteSettings.Add(new SiteSetting { Id = Guid.NewGuid(), Key = key, Value = value, DateCreated = StoreTestData.Now, CreatedByAppUserId = _admin.Id });
        await db.SaveChangesAsync();
    }

    public Task DisposeAsync() => _sqlite.DisposeAsync().AsTask();

    // ── the arithmetic ───────────────────────────────────────────────────────

    [Fact]
    public void The_card_fee_is_a_percentage_plus_a_fixed_part()
    {
        Assert.Equal(4.62m, StoreEconomics.EstimatedFee(149m, 2.9m, 0.30m));   // 4.321 + 0.30 = 4.621
        Assert.Equal(0m, StoreEconomics.EstimatedFee(0m, 2.9m, 0.30m));
    }

    [Fact]
    public void A_seller_earns_cost_plus_ask_and_the_store_its_margin()
    {
        Assert.Equal(129.50m, StoreEconomics.SellerEarning(true, 34.50m, 95m));
        Assert.Equal(0m, StoreEconomics.SellerEarning(false, 34.50m, 95m));     // the store's own stock pays nobody
        Assert.Equal(19.50m, StoreEconomics.Markup(149m, 129.50m));
    }

    [Fact]
    public void The_suggested_price_covers_the_earning_the_markup_and_the_fee()
    {
        var suggested = StoreEconomics.SuggestedPrice(129.50m, 30m, 2.9m, 0.30m);
        // 129.50 × 1.3 = 168.35; (168.35 + 0.30) / 0.971 = 173.687… → up to 173.69
        Assert.Equal(173.69m, suggested);
        Assert.False(StoreEconomics.BelowCost(suggested, 129.50m, 2.9m, 0.30m));
        Assert.True(StoreEconomics.BelowCost(130m, 129.50m, 2.9m, 0.30m));    // covers the earning, not the fee
    }

    // ── fixed at checkout ────────────────────────────────────────────────────

    [Fact]
    public async Task Checkout_fixes_each_lines_cost_ask_earning_and_markup()
    {
        Guid variantId;
        await using (var db = await _sqlite.NewContextAsync())
        {
            var v = StoreTestData.Variant(db, await db.AppUsers.SingleAsync(u => u.Id == _admin.Id), price: 149m);
            db.StoreProductParts.Add(new StoreProductPart { Id = Guid.NewGuid(), ProductId = v.ProductId, Name = "Antenna", PriceBasis = StorePartPriceBasis.PerPiece,
                Price = 31m, PiecesPerPack = 1, QuantityPerUnit = 1m, DateCreated = StoreTestData.Now });
            await db.SaveChangesAsync();
            await db.StoreProducts.Where(p => p.Id == v.ProductId).ExecuteUpdateAsync(u => u.SetProperty(p => p.SellerAppUserId, _hazel.Id)
                .SetProperty(p => p.SellerAskPerUnit, 95m).SetProperty(p => p.OtherCostPerUnit, 3.50m));
            variantId = v.Id;
        }
        var caller = StoreCartCaller.From(null, Microsoft.AspNetCore.WebUtilities.WebEncoders.Base64UrlEncode(System.Security.Cryptography.RandomNumberGenerator.GetBytes(32)));
        await using (var db = await _sqlite.NewContextAsync())
            await new StoreCartService(db).AddAsync(caller, variantId, 1);

        var result = await Checkout().PrepareAsync(caller, null,
            new StoreCheckoutRequest("sarah@example.com", new StoreAddressInput("Sarah", "615-555-0100", "1 Elm", null, "Nashville", "TN", "37203"), null, null, true, null));

        Assert.Equal(StoreCheckoutOutcome.Ok, result.Outcome);
        await using var check = await _sqlite.NewContextAsync();
        var line = await check.StoreOrderItems.AsNoTracking().SingleAsync();
        Assert.Equal((34.50m, 95m, 129.50m, 19.50m), (line.UnitCostBasis, line.UnitSellerAsk, line.UnitSellerEarning, line.UnitSiteMarkup));
    }

    // ── the real fee ─────────────────────────────────────────────────────────

    private (StoreCheckoutService Checkout, StoreOrderPayments Payments) Services()
    {
        var site = Options.Create(new SiteIdentity { Name = "IsHaunted.com", BaseUrl = "https://test.local" });
        var mailer = new StoreOrderMailer(TestOutbox.WithoutMail(_sqlite.Factory), site);
        var alerts = new StoreAlerts(_sqlite.Factory, new PlatformMessageService(_sqlite.Factory), mailer, NullLogger<StoreAlerts>.Instance);
        var payments = new StoreOrderPayments(_sqlite.Factory, _stripe, _tax, mailer, alerts, NullLogger<StoreOrderPayments>.Instance);
        return (new StoreCheckoutService(_sqlite.Factory, _stripe, _tax, payments, alerts, new StorePaymentSetup(true, false, false),
            Options.Create(new StripeOptions()), NullLogger<StoreCheckoutService>.Instance), payments);
    }

    private StoreCheckoutService Checkout() => Services().Checkout;

    [Fact]
    public async Task Stripes_fee_is_read_when_the_order_is_paid_and_the_sweep_finds_one_it_missed()
    {
        Guid variantId;
        await using (var db = await _sqlite.NewContextAsync())
        {
            var v = StoreTestData.Variant(db, await db.AppUsers.SingleAsync(u => u.Id == _admin.Id), price: 100m);
            await db.SaveChangesAsync();
            variantId = v.Id;
        }
        var caller = StoreCartCaller.From(null, Microsoft.AspNetCore.WebUtilities.WebEncoders.Base64UrlEncode(System.Security.Cryptography.RandomNumberGenerator.GetBytes(32)));
        await using (var db = await _sqlite.NewContextAsync())
            await new StoreCartService(db).AddAsync(caller, variantId, 1);
        var (checkout, payments) = Services();
        var prepared = (await checkout.PrepareAsync(caller, null,
            new StoreCheckoutRequest("sarah@example.com", new StoreAddressInput("Sarah", "615-555-0100", "1 Elm", null, "Nashville", "TN", "37203"), null, null, true, null))).Prepared!;

        await payments.MarkPaidAsync(prepared.OrderId, FakeStoreStripeGateway.IntentIdFor(prepared.OrderId), "ch_1", "evt_1", null, null);

        await using var db2 = await _sqlite.NewContextAsync();
        var order = await db2.StoreOrders.AsNoTracking().SingleAsync();
        var cents = StoreMoney.Cents(order.Total);
        var fee = (Math.Round(cents * 0.029m, MidpointRounding.AwayFromZero) + 30) / 100m;
        Assert.Equal((fee, order.Total - fee), (order.StripeFeeAmount, order.StripeNetAmount));

        // Forgotten, the sweep reads it again.
        await db2.StoreOrders.ExecuteUpdateAsync(u => u.SetProperty(o => o.StripeFeeAmount, (decimal?)null).SetProperty(o => o.StripeNetAmount, (decimal?)null));
        await new StoreFeeCaptureJob(_sqlite.Factory, payments).RunAsync(default);
        Assert.Equal(fee, (await db2.StoreOrders.AsNoTracking().SingleAsync()).StripeFeeAmount);
    }

    [Theory]
    [InlineData("store.markup-percent", "30", "30", null)]
    [InlineData("store.fee-percent", "2.9", "2.9", null)]
    [InlineData("store.fee-percent", "101", null, "Card fee (%) is a percentage from 0 to 100, like 2.9.")]
    [InlineData("store.fee-fixed-usd", "0.3", "0.30", null)]
    public void The_settings_take_percentages_and_dollars(string key, string raw, string? value, string? refusal)
        => Assert.Equal((value, refusal), StoreSettingsValidation.Check(key, raw));
}
