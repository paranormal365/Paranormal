using Ben.Data.Common;
using Ben.Data.Common.Enums;
using Ben.Data.Common.Mail;
using Ben.Data.Source.Entities;
using Ben.Data.WebApi.Services.Store;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Xunit;

namespace Ben.Web.Tests.Store;

/// <summary>
/// The store's letters (storefront S4.4): queued through the outbox in the caller's save, each
/// recorded on its order, a guest's link carrying the token and a member's never.
/// </summary>
public sealed class StoreOrderMailerTests : IAsyncLifetime
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

    private StoreOrderMailer Mailer() => new(TestOutbox.WithoutMail(_sqlite.Factory),
        Options.Create(new SiteIdentity { Name = "IsHaunted.com", BaseUrl = "https://test.local" }));

    private async Task<(StoreOrder Order, List<StoreOrderItem> Items)> OrderAsync(bool member = false)
    {
        await using var db = await _sqlite.NewContextAsync();
        var admin = await db.AppUsers.SingleAsync(u => u.Id == _admin.Id);
        var buyer = member ? StoreTestData.Person(db, "member") : null;
        var v = StoreTestData.Variant(db, admin, price: 59.99m);
        var order = StoreTestData.Order(db, StoreOrderStatus.Paid, buyer);
        var item = new StoreOrderItem
        {
            Id = Guid.NewGuid(), OrderId = order.Id, ProductId = v.ProductId, VariantId = v.Id, ProductName = "K-II EMF Meter",
            VariantName = "Black", Sku = v.Sku, UnitPrice = 59.99m, Quantity = 1, LineTotal = 59.99m, DateCreated = StoreTestData.Now,
        };
        db.StoreOrderItems.Add(item);
        await db.SaveChangesAsync();
        return (order, [item]);
    }

    private async Task<List<OutboxEmail>> LettersAsync()
    {
        await using var db = await _sqlite.NewContextAsync();
        return await db.OutboxEmails.AsNoTracking().ToListAsync();
    }

    [Fact]
    public async Task The_receipt_is_queued_with_the_order_and_recorded_on_it()
    {
        var (order, items) = await OrderAsync();
        await using (var db = await _sqlite.NewContextAsync())
        {
            await Mailer().QueueConfirmationAsync(db, order, items, StoreTestData.Now, default);
            await db.SaveChangesAsync();
        }

        var letter = Assert.Single(await LettersAsync());
        Assert.Equal((order.BuyerEmail, MailKinds.StoreOrderConfirmation.Key), (letter.To, letter.Kind));
        Assert.Contains($"/store/orders/{order.Id}?t={order.AccessToken}", letter.HtmlBody);
        Assert.Contains("1 × K-II EMF Meter — Black", letter.HtmlBody);
        await using var check = await _sqlite.NewContextAsync();
        Assert.True(await check.StoreOrderEvents.AnyAsync(e => e.OrderId == order.Id && e.Kind == StoreOrderEventKind.LetterQueued
                                                              && e.Note == MailKinds.StoreOrderConfirmation.Key));
    }

    /// <summary>Nothing is sent until the caller's save: a failed save leaves no letter behind.</summary>
    [Fact]
    public async Task Without_the_callers_save_there_is_no_letter()
    {
        var (order, items) = await OrderAsync();
        await using (var db = await _sqlite.NewContextAsync())
            await Mailer().QueueConfirmationAsync(db, order, items, StoreTestData.Now, default);

        Assert.Empty(await LettersAsync());
    }

    [Fact]
    public async Task A_members_letters_never_carry_the_token()
    {
        var (order, items) = await OrderAsync(member: true);
        await using (var db = await _sqlite.NewContextAsync())
        {
            await Mailer().QueueConfirmationAsync(db, order, items, StoreTestData.Now, default);
            Assert.True(await Mailer().QueueOrderLinkAsync(db, order, StoreTestData.Now, default));
            await db.SaveChangesAsync();
        }

        var letters = await LettersAsync();
        Assert.Equal(2, letters.Count);
        Assert.All(letters, l => Assert.DoesNotContain(order.AccessToken, l.HtmlBody));
        Assert.Contains("Sign in to see this order", letters.Single(l => l.Kind == MailKinds.StoreOrderLink.Key).HtmlBody);
    }

    [Fact]
    public async Task A_second_lookup_within_ten_minutes_queues_nothing()
    {
        var (order, _) = await OrderAsync();
        await using (var db = await _sqlite.NewContextAsync())
        {
            Assert.True(await Mailer().QueueOrderLinkAsync(db, order, StoreTestData.Now, default));
            await db.SaveChangesAsync();
        }
        await using (var db = await _sqlite.NewContextAsync())
        {
            Assert.False(await Mailer().QueueOrderLinkAsync(db, order, StoreTestData.Now.AddMinutes(9), default));
            await db.SaveChangesAsync();
        }
        await using (var db = await _sqlite.NewContextAsync())
        {
            Assert.True(await Mailer().QueueOrderLinkAsync(db, order, StoreTestData.Now.AddMinutes(11), default));
            await db.SaveChangesAsync();
        }

        Assert.Equal(2, (await LettersAsync()).Count);
    }

    [Fact]
    public async Task Every_superadmin_hears_of_a_paid_order()
    {
        var (order, items) = await OrderAsync();
        await using (var db = await _sqlite.NewContextAsync())
        {
            await Mailer().QueueNewOrderAlertAsync(db, order, items, [("a@site.test", "A"), ("b@site.test", "B")], StoreTestData.Now, default);
            await db.SaveChangesAsync();
        }

        var letters = await LettersAsync();
        Assert.Equal(["a@site.test", "b@site.test"], letters.Select(l => l.To).Order());
        Assert.All(letters, l => Assert.Contains($"/admin/store/orders/{order.Id}", l.HtmlBody));
    }
}
