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
/// A cart with a seller's items checking out as packages (store sellers, backlog 251, P5): one per
/// seller, each with its own shipping, the seller credited the flat rate, and the cart and the
/// checkout agreeing about every cent.
/// </summary>
public sealed class StoreParcelCheckoutTests : IAsyncLifetime
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
        _hazel.DisplayName = "Hazel Marsh";
        await db.SaveChangesAsync();
        await SetAsync(SiteSettingKeys.StoreShipFromStreet, "1 Depot Road");
        await SetAsync(SiteSettingKeys.StoreShipFromCity, "Franklin");
        await SetAsync(SiteSettingKeys.StoreShipFromState, "TN");
        await SetAsync(SiteSettingKeys.StoreShipFromZip, "37064");
        await SetAsync(SiteSettingKeys.StoreShippingFlatRateUsd, "7.95");
        await SetAsync(SiteSettingKeys.StoreFreeShippingThresholdUsd, "50");
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

    private StoreCheckoutService Checkout()
    {
        var site = Options.Create(new SiteIdentity { Name = "IsHaunted.com", BaseUrl = "https://test.local" });
        var mailer = new StoreOrderMailer(TestOutbox.WithoutMail(_sqlite.Factory), site);
        var alerts = new StoreAlerts(_sqlite.Factory, new PlatformMessageService(_sqlite.Factory), mailer, NullLogger<StoreAlerts>.Instance);
        var payments = new StoreOrderPayments(_sqlite.Factory, _stripe, _tax, mailer, alerts, NullLogger<StoreOrderPayments>.Instance);
        return new StoreCheckoutService(_sqlite.Factory, _stripe, _tax, payments, alerts, new StorePaymentSetup(true, false, false),
            Options.Create(new StripeOptions()), NullLogger<StoreCheckoutService>.Instance);
    }

    private async Task<StoreProductVariant> VariantAsync(decimal price, AppUser? seller = null)
    {
        await using var db = await _sqlite.NewContextAsync();
        var v = StoreTestData.Variant(db, await db.AppUsers.SingleAsync(u => u.Id == _admin.Id), price: price);
        await db.SaveChangesAsync();
        if (seller is not null)
            await db.StoreProducts.Where(p => p.Id == v.ProductId).ExecuteUpdateAsync(u => u.SetProperty(p => p.SellerAppUserId, seller.Id));
        return v;
    }

    private async Task<StoreCartCaller> CartAsync(params (StoreProductVariant Variant, int Quantity)[] lines)
    {
        var caller = StoreCartCaller.From(null, Microsoft.AspNetCore.WebUtilities.WebEncoders.Base64UrlEncode(System.Security.Cryptography.RandomNumberGenerator.GetBytes(32)));
        await using var db = await _sqlite.NewContextAsync();
        var cart = new StoreCartService(db);
        foreach (var (v, q) in lines) Assert.Equal(StoreCartOutcome.Ok, (await cart.AddAsync(caller, v.Id, q)).Outcome);
        return caller;
    }

    private async Task<StoreCartView> ViewAsync(StoreCartCaller caller)
    {
        await using var db = await _sqlite.NewContextAsync();
        return await new StoreCartService(db).ViewAsync(caller);
    }

    private static StoreCheckoutRequest Request()
        => new("sarah@example.com", new StoreAddressInput("Sarah Hollow", "615-555-0100", "13 Crossroads Lane", null, "Nashville", "TN", "37203"), null, null, true, null);

    private async Task<List<StoreOrderParcel>> ParcelsAsync(Guid orderId)
    {
        await using var db = await _sqlite.NewContextAsync();
        return await db.StoreOrderParcels.AsNoTracking().Include(x => x.Items).Where(x => x.OrderId == orderId).OrderBy(x => x.Number).ToListAsync();
    }

    [Fact]
    public async Task Two_sellers_ship_as_two_packages_each_with_its_own_shipping()
    {
        var site = await VariantAsync(20m);            // $40 of the store's own: under $50, pays $7.95
        var hers = await VariantAsync(60m, _hazel);    // $60 of Hazel's: ships free
        var caller = await CartAsync((site, 2), (hers, 1));

        var result = await Checkout().PrepareAsync(caller, null, Request());

        Assert.Equal(StoreCheckoutOutcome.Ok, result.Outcome);
        Assert.Equal(7.95m, result.Prepared!.Totals.Shipping);
        var parcels = await ParcelsAsync(result.Prepared.OrderId);
        Assert.Equal(2, parcels.Count);
        Assert.Equal((1, (Guid?)null, 7.95m, 0m), (parcels[0].Number, parcels[0].SellerAppUserId, parcels[0].ShippingAmount, parcels[0].SellerShippingCredit));
        // Hazel's package shipped free to the buyer, and she is still credited the flat rate for its label.
        Assert.Equal((2, (Guid?)_hazel.Id, "Hazel Marsh", 0m, 7.95m),
            (parcels[1].Number, parcels[1].SellerAppUserId, parcels[1].SellerName, parcels[1].ShippingAmount, parcels[1].SellerShippingCredit));
        Assert.Equal([site.Id], parcels[0].Items.Select(i => i.VariantId));
        Assert.Equal([hers.Id], parcels[1].Items.Select(i => i.VariantId));
        Assert.All(parcels, x => Assert.Equal(StoreParcelStatus.Waiting, x.Status));
    }

    [Fact]
    public async Task The_packages_shares_of_the_shipping_tax_add_up_to_the_orders()
    {
        var a = await VariantAsync(10m);
        var b = await VariantAsync(15m, _hazel);
        var result = await Checkout().PrepareAsync(await CartAsync((a, 1), (b, 1)), null, Request());

        await using var db = await _sqlite.NewContextAsync();
        var order = await db.StoreOrders.AsNoTracking().SingleAsync(o => o.Id == result.Prepared!.OrderId);
        var parcels = await ParcelsAsync(order.Id);
        Assert.Equal(15.90m, order.ShippingAmount);   // two packages, neither free
        Assert.Equal(order.ShippingAmount, parcels.Sum(x => x.ShippingAmount));
        Assert.Equal(order.ShippingTaxAmount, parcels.Sum(x => x.ShippingTaxAmount));
    }

    [Fact]
    public async Task The_cart_and_checkout_price_packages_the_same()
    {
        var site = await VariantAsync(24.99m);
        var hers = await VariantAsync(50m, _hazel);
        var caller = await CartAsync((site, 2), (hers, 1));

        var cart = await ViewAsync(caller);
        var result = await Checkout().PrepareAsync(caller, null, Request());
        var parcels = await ParcelsAsync(result.Prepared!.OrderId);

        Assert.True(cart.IsSplit);
        Assert.Equal(cart.Parcels!.Select(x => (x.Number, x.Shipping)), parcels.Select(x => (x.Number, x.ShippingAmount)));
        Assert.Equal(cart.Shipping, result.Prepared.Totals.Shipping);
        Assert.Equal(["Package 1 · Ships from IsHaunted.com", "Package 2 · Ships from Hazel Marsh"], cart.Parcels!.Select(x => x.Title));
        Assert.Equal(0.02m, cart.Parcels![0].MoreForFree);   // $49.98 of the store's own: two cents short of free
    }

    [Fact]
    public async Task One_sellers_cart_is_one_package_as_before()
    {
        var v = await VariantAsync(20m);
        var caller = await CartAsync((v, 1));
        var cart = await ViewAsync(caller);
        var result = await Checkout().PrepareAsync(caller, null, Request());

        Assert.False(cart.IsSplit);
        var only = Assert.Single(await ParcelsAsync(result.Prepared!.OrderId));
        Assert.Equal((StoreParcelNames.Site, 7.95m), (StoreParcelNames.ShipsFrom(only.SellerAppUserId, only.SellerName), only.ShippingAmount));
    }

    [Fact]
    public async Task Continue_again_reuses_the_order_and_its_packages_at_todays_rate()
    {
        var site = await VariantAsync(10m);
        var hers = await VariantAsync(10m, _hazel);
        var caller = await CartAsync((site, 1), (hers, 1));
        var first = await Checkout().PrepareAsync(caller, null, Request());

        await SetAsync(SiteSettingKeys.StoreShippingFlatRateUsd, "9.00");
        var again = await Checkout().PrepareAsync(caller, null, Request());

        Assert.Equal(first.Prepared!.OrderId, again.Prepared!.OrderId);
        var parcels = await ParcelsAsync(again.Prepared.OrderId);
        Assert.Equal([9.00m, 9.00m], parcels.Select(x => x.ShippingAmount));
        Assert.Equal(9.00m, parcels[1].SellerShippingCredit);
        Assert.Equal(18.00m, again.Prepared.Totals.Shipping);
        Assert.Equal(18.00m, (await ViewAsync(caller)).Shipping);   // the cart says the same: both packages pay
    }

    [Fact]
    public async Task Tax_filed_on_an_earlier_calculation_re_splits_the_packages_shipping_tax()
    {
        var site = await VariantAsync(10m);
        var hers = await VariantAsync(10m, _hazel);
        var caller = await CartAsync((site, 1), (hers, 1));
        var first = await Checkout().PrepareAsync(caller, null, Request());
        string earlier;
        await using (var db = await _sqlite.NewContextAsync())
            earlier = (await db.StoreOrders.AsNoTracking().SingleAsync()).StripeTaxCalculationId!;

        // A second tab at a new rate rewrites the order; the first tab's payment then goes through.
        await SetAsync(SiteSettingKeys.StoreShippingFlatRateUsd, "9.00");
        await Checkout().PrepareAsync(caller, null, Request());
        var orderId = first.Prepared!.OrderId;
        var site2 = Options.Create(new SiteIdentity { Name = "IsHaunted.com", BaseUrl = "https://test.local" });
        var mailer = new StoreOrderMailer(TestOutbox.WithoutMail(_sqlite.Factory), site2);
        var alerts = new StoreAlerts(_sqlite.Factory, new PlatformMessageService(_sqlite.Factory), mailer, NullLogger<StoreAlerts>.Instance);
        await new StoreOrderPayments(_sqlite.Factory, _stripe, _tax, mailer, alerts, NullLogger<StoreOrderPayments>.Instance)
            .MarkPaidAsync(orderId, FakeStoreStripeGateway.IntentIdFor(orderId), "ch_1", "evt_1", null, earlier);

        await using var check = await _sqlite.NewContextAsync();
        var order = await check.StoreOrders.AsNoTracking().SingleAsync();
        Assert.Equal(earlier, order.StripeTaxCalculationId);
        Assert.Equal(order.ShippingTaxAmount, (await ParcelsAsync(orderId)).Sum(x => x.ShippingTaxAmount));
    }

    [Fact]
    public async Task A_checkout_that_cannot_hold_the_stock_leaves_no_packages()
    {
        var site = await VariantAsync(10m);
        var hers = await VariantAsync(10m, _hazel);
        var caller = await CartAsync((site, 1), (hers, 1));
        await using (var db = await _sqlite.NewContextAsync())
            await db.StoreProductVariants.Where(v => v.Id == hers.Id).ExecuteUpdateAsync(u => u.SetProperty(v => v.StockReserved, v => v.StockOnHand));

        var result = await Checkout().PrepareAsync(caller, null, Request());

        Assert.NotEqual(StoreCheckoutOutcome.Ok, result.Outcome);
        await using var check = await _sqlite.NewContextAsync();
        Assert.Empty(await check.StoreOrderParcels.ToListAsync());
        Assert.Empty(await check.StoreOrders.ToListAsync());
    }
}
