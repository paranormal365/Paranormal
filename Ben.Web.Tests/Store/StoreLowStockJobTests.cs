using Ben.Data.Common;
using Ben.Data.Common.Constants;
using Ben.Data.Common.Mail;
using Ben.Data.Source.Entities;
using Ben.Data.WebApi.Services;
using Ben.Data.WebApi.Services.Scheduling;
using Ben.Data.WebApi.Services.Store;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Xunit;

namespace Ben.Web.Tests.Store;

/// <summary>The morning's low stock (storefront S5.6): once a day, only while the store is on, only what sells.</summary>
public sealed class StoreLowStockJobTests : IAsyncLifetime
{
    private SqliteTestDb _sqlite = null!;
    private AppUser _admin = null!;
    private static readonly DateTime Morning = new(2026, 9, 24, 14, 0, 0, DateTimeKind.Utc);

    private sealed class Clock(DateTime now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(now, TimeSpan.Zero);
    }

    public async Task InitializeAsync()
    {
        _sqlite = await SqliteTestDb.CreateAsync();
        await using var db = await _sqlite.NewContextAsync();
        _admin = StoreTestData.Person(db);
        _admin.Email = "admin@site.test";
        var role = new IdentityRole<Guid> { Id = Guid.NewGuid(), Name = RoleNames.SuperAdmin, NormalizedName = "SUPERADMIN" };
        db.Roles.Add(role);
        db.UserRoles.Add(new IdentityUserRole<Guid> { UserId = _admin.Id, RoleId = role.Id });
        StoreTestData.Variant(db, _admin, onHand: 2, sku: "LOW-ONE");        // at the default threshold of 3
        StoreTestData.Variant(db, _admin, onHand: 40, sku: "PLENTY");
        StoreTestData.Variant(db, _admin, onHand: 1, sku: "HIDDEN-LOW", productActive: false);
        await db.SaveChangesAsync();
    }

    public Task DisposeAsync() => _sqlite.DisposeAsync().AsTask();

    private async Task StoreOnAsync(bool on)
    {
        await using var db = await _sqlite.NewContextAsync();
        db.SiteSettings.Add(new SiteSetting { Id = Guid.NewGuid(), Key = SiteSettingKeys.FeatureStore, Value = on ? "true" : "false", DateCreated = StoreTestData.Now, CreatedByAppUserId = _admin.Id });
        await db.SaveChangesAsync();
    }

    private StoreLowStockJob Job(DateTime now)
    {
        var site = Options.Create(new SiteIdentity { Name = "IsHaunted.com", BaseUrl = "https://test.local" });
        return new StoreLowStockJob(_sqlite.Factory, new PlatformMessageService(_sqlite.Factory),
            new StoreOrderMailer(TestOutbox.WithoutMail(_sqlite.Factory), site), new Clock(now));
    }

    private async Task<(List<UserMessage> Bells, List<OutboxEmail> Letters)> ReadAsync()
    {
        await using var db = await _sqlite.NewContextAsync();
        return (await db.UserMessages.AsNoTracking().ToListAsync(), await db.OutboxEmails.AsNoTracking().ToListAsync());
    }

    [Fact]
    public async Task It_tells_the_SuperAdmins_once_a_day_about_what_sells_and_is_low()
    {
        await StoreOnAsync(true);
        await Job(Morning).RunAsync(default);
        await Job(Morning.AddMinutes(5)).RunAsync(default);

        var (bells, letters) = await ReadAsync();
        var bell = Assert.Single(bells);
        Assert.Equal("Store stock: 1 running low", bell.MessageSubject);
        var letter = Assert.Single(letters);
        Assert.Equal((MailKinds.StoreLowStock.Key, "admin@site.test"), (letter.Kind, letter.To));
        Assert.Contains("LOW-ONE", letter.HtmlBody);
        Assert.DoesNotContain("PLENTY", letter.HtmlBody);
        Assert.DoesNotContain("HIDDEN-LOW", letter.HtmlBody);

        await Job(Morning.AddDays(1)).RunAsync(default);
        Assert.Equal(2, (await ReadAsync()).Bells.Count);   // tomorrow is another day
    }

    [Fact]
    public async Task Nothing_while_the_store_is_off()
    {
        await StoreOnAsync(false);
        await Job(Morning).RunAsync(default);
        Assert.Empty((await ReadAsync()).Bells);
    }

    [Fact]
    public async Task Nothing_before_the_morning()
    {
        await StoreOnAsync(true);
        await Job(Morning.Date.AddHours(StoreLowStockJob.FromHourUtc - 1)).RunAsync(default);
        Assert.Empty((await ReadAsync()).Bells);
    }
}
