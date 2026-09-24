using Ben.Data.Common;
using Ben.Data.Common.Constants;
using Ben.Data.Common.Enums;
using Ben.Data.Common.Mail;
using Ben.Data.Source.Entities;
using Ben.Data.WebApi.Services;
using Ben.Data.WebApi.Services.Store;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Ben.Web.Tests.Store;

/// <summary>The store's bells (storefront S4.5): who hears of a paid order, and the hourly limit on configuration faults.</summary>
[Collection("StoreAlerts")]   // the hourly limit is process-wide
public sealed class StoreAlertsTests : IAsyncLifetime
{
    private SqliteTestDb _sqlite = null!;
    private AppUser _admin = null!, _other = null!;

    public async Task InitializeAsync()
    {
        StoreAlerts.ResetHourlyLimits();
        _sqlite = await SqliteTestDb.CreateAsync();
        await using var db = await _sqlite.NewContextAsync();
        _admin = StoreTestData.Person(db);
        _admin.Email = "admin@site.test";
        _other = StoreTestData.Person(db, "other");
        _other.Email = "other@site.test";
        var role = new IdentityRole<Guid> { Id = Guid.NewGuid(), Name = RoleNames.SuperAdmin, NormalizedName = "SUPERADMIN" };
        db.Roles.Add(role);
        db.UserRoles.Add(new IdentityUserRole<Guid> { UserId = _admin.Id, RoleId = role.Id });
        db.UserRoles.Add(new IdentityUserRole<Guid> { UserId = _other.Id, RoleId = role.Id });
        await db.SaveChangesAsync();
    }

    public Task DisposeAsync() => _sqlite.DisposeAsync().AsTask();

    private StoreAlerts Alerts(DateTime? now = null)
    {
        var site = Options.Create(new SiteIdentity { Name = "IsHaunted.com", BaseUrl = "https://test.local" });
        return new StoreAlerts(_sqlite.Factory, new PlatformMessageService(_sqlite.Factory),
            new StoreOrderMailer(TestOutbox.WithoutMail(_sqlite.Factory), site), NullLogger<StoreAlerts>.Instance,
            now is { } at ? new FixedClock(at) : null);
    }

    private sealed class FixedClock(DateTime now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(now, TimeSpan.Zero);
    }

    private async Task<int> MessagesToAsync(Guid who)
    {
        await using var db = await _sqlite.NewContextAsync();
        return await db.UserMessageTos.CountAsync(t => t.ToAppUserId == who);
    }

    [Fact]
    public async Task A_paid_order_rings_the_buyer_and_every_superadmin_and_writes_to_the_admins()
    {
        Guid orderId, buyerId;
        await using (var db = await _sqlite.NewContextAsync())
        {
            var buyer = StoreTestData.Person(db, "buyer");
            var order = StoreTestData.Order(db, StoreOrderStatus.Paid, buyer);
            await db.SaveChangesAsync();
            (orderId, buyerId) = (order.Id, buyer.Id);
        }

        await Alerts().OrderPaidAsync(orderId);

        Assert.Equal(1, await MessagesToAsync(buyerId));
        Assert.Equal(1, await MessagesToAsync(_admin.Id));
        Assert.Equal(1, await MessagesToAsync(_other.Id));
        await using var check = await _sqlite.NewContextAsync();
        var letters = await check.OutboxEmails.Where(e => e.Kind == MailKinds.StoreOrderPlaced.Key).Select(e => e.To).ToListAsync();
        Assert.Equal(["admin@site.test", "other@site.test"], letters.Order());
    }

    [Fact]
    public async Task A_configuration_fault_is_told_at_most_once_an_hour()
    {
        var t = StoreTestData.Now;
        await Alerts(t).TaxConfigurationAsync("tax_settings_incomplete");
        await Alerts(t.AddMinutes(30)).TaxConfigurationAsync("tax_settings_incomplete");
        Assert.Equal(1, await MessagesToAsync(_admin.Id));

        await Alerts(t.AddMinutes(61)).TaxConfigurationAsync("tax_settings_incomplete");
        Assert.Equal(2, await MessagesToAsync(_admin.Id));

        await Alerts(t.AddMinutes(62)).LinkNotActivatedAsync();   // a different fault has its own hour
        Assert.Equal(3, await MessagesToAsync(_admin.Id));
    }

    [Fact]
    public async Task An_alert_that_cannot_be_sent_never_throws()
    {
        await _sqlite.DisposeAsync();   // no database at all
        await Alerts().OrderPaidAsync(Guid.NewGuid());
        await Alerts().RefundFailedAsync(100001, Guid.NewGuid(), "expired card");
        _sqlite = await SqliteTestDb.CreateAsync();
    }
}
