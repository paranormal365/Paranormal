using Ben.Data.Common;
using Ben.Data.Common.Enums;
using Ben.Data.Source.Entities;
using Ben.Data.WebApi.Services;
using Ben.Data.WebApi.Services.Billing.StripeIntegration;
using Ben.Data.WebApi.Services.Scheduling;
using Ben.Data.WebApi.Services.Store;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Ben.Web.Tests.Store;

/// <summary>The store's jobs (storefront S4.8): tax filings retried until done or refused for good, and idle carts swept.</summary>
public sealed class StoreJobsTests : IAsyncLifetime
{
    private SqliteTestDb _sqlite = null!;
    private AppUser _admin = null!;
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

    private StoreOrderPayments Payments()
    {
        var site = Options.Create(new SiteIdentity { Name = "IsHaunted.com", BaseUrl = "https://test.local" });
        var mailer = new StoreOrderMailer(TestOutbox.WithoutMail(_sqlite.Factory), site);
        return new StoreOrderPayments(_sqlite.Factory, new FakeStoreStripeGateway(), _tax, mailer,
            new StoreAlerts(_sqlite.Factory, new PlatformMessageService(_sqlite.Factory), mailer, NullLogger<StoreAlerts>.Instance),
            NullLogger<StoreOrderPayments>.Instance);
    }

    private async Task<Guid> PaidWithoutTaxFiledAsync()
    {
        var calc = await _tax.CalculateAsync(new StoreTaxRequest([new StoreTaxLine("X", 1000, 1, "txcd_99999999")], 0,
            new StoreTaxAddress("1", null, "N", "TN", "37203"), new StoreTaxAddress("2", null, "F", "TN", "37064")));
        await using var db = await _sqlite.NewContextAsync();
        var order = StoreTestData.Order(db, StoreOrderStatus.Paid);
        order.PaidUtc = DateTime.UtcNow;
        order.StripeTaxCalculationId = calc.CalculationId;
        await db.SaveChangesAsync();
        return order.Id;
    }

    private async Task<StoreOrder> ReadAsync(Guid id)
    {
        await using var db = await _sqlite.NewContextAsync();
        return await db.StoreOrders.AsNoTracking().SingleAsync(o => o.Id == id);
    }

    [Fact]
    public async Task A_filing_that_failed_is_retried_until_it_lands()
    {
        var id = await PaidWithoutTaxFiledAsync();
        _tax.Refuse = StoreTaxFailure.Transient;
        await new StoreTaxRetryJob(_sqlite.Factory, Payments()).RunAsync(default);
        Assert.Equal(1, (await ReadAsync(id)).TaxCommitAttempts);

        _tax.Refuse = null;
        await new StoreTaxRetryJob(_sqlite.Factory, Payments()).RunAsync(default);
        Assert.NotNull((await ReadAsync(id)).StripeTaxTransactionId);
    }

    [Fact]
    public async Task A_permanent_refusal_raises_attention_and_stops()
    {
        var id = await PaidWithoutTaxFiledAsync();
        _tax.Refuse = StoreTaxFailure.Configuration;

        await new StoreTaxRetryJob(_sqlite.Factory, Payments()).RunAsync(default);
        await new StoreTaxRetryJob(_sqlite.Factory, Payments()).RunAsync(default);

        var order = await ReadAsync(id);
        Assert.True(order.NeedsAttention);
        Assert.StartsWith("Stripe refused to file this order's sales tax", order.AttentionReason);
        Assert.Equal(1, order.TaxCommitAttempts);   // exactly one attempt after the flag went up
    }

    [Fact]
    public async Task Idle_carts_are_swept_and_an_empty_guest_cart_sooner()
    {
        var now = new DateTime(2026, 12, 1, 0, 0, 0, DateTimeKind.Utc);
        Guid keep, oldFull, emptyGuest, emptyMember, member;
        await using (var db = await _sqlite.NewContextAsync())
        {
            var admin = await db.AppUsers.SingleAsync(u => u.Id == _admin.Id);
            var m = StoreTestData.Person(db, "member");
            member = m.Id;
            var v = StoreTestData.Variant(db, admin);
            StoreCart Cart(DateTime last, Guid? user = null) => new()
            {
                Id = Guid.NewGuid(), AppUserId = user, GuestTokenHash = user is null ? Guid.NewGuid().ToString("N") + Guid.NewGuid().ToString("N") : null,
                LastActivityUtc = last, DateCreated = last,
            };
            var a = Cart(now.AddDays(-30)); var b = Cart(now.AddDays(-91)); var c = Cart(now.AddDays(-8)); var d = Cart(now.AddDays(-8), m.Id);
            db.StoreCarts.AddRange(a, b, c, d);
            db.StoreCartItems.Add(new StoreCartItem { Id = Guid.NewGuid(), CartId = a.Id, VariantId = v.Id, Quantity = 1, DateCreated = now });
            db.StoreCartItems.Add(new StoreCartItem { Id = Guid.NewGuid(), CartId = b.Id, VariantId = v.Id, Quantity = 1, DateCreated = now });
            await db.SaveChangesAsync();
            (keep, oldFull, emptyGuest, emptyMember) = (a.Id, b.Id, c.Id, d.Id);
        }

        await new StoreCartSweepJob(_sqlite.Factory, new FixedClock(now)).RunAsync(default);

        await using var check = await _sqlite.NewContextAsync();
        var left = await check.StoreCarts.Select(c => c.Id).ToListAsync();
        Assert.Contains(keep, left);
        Assert.Contains(emptyMember, left);   // a member's cart waits the full 90 days
        Assert.DoesNotContain(oldFull, left);
        Assert.DoesNotContain(emptyGuest, left);
    }

    [Fact]
    public void Expiry_runs_every_minute_on_its_own_timer()
        => Assert.Equal(TimeSpan.FromMinutes(1), StoreReservationExpiryService.Interval);

    private sealed class FixedClock(DateTime now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(now, TimeSpan.Zero);
    }
}
