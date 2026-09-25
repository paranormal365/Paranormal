using Ben.Data.Common;
using Ben.Data.Common.Constants;
using Ben.Data.Common.Enums;
using Ben.Data.Common.Mail;
using Ben.Data.Source.Entities;
using Ben.Data.WebApi.Controllers.Seller;
using Ben.Data.WebApi.Services;
using Ben.Data.WebApi.Services.Billing.StripeIntegration;
using Ben.Data.WebApi.Services.Store;
using Ben.Service.Models.Store;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Ben.Web.Tests.Store;

/// <summary>
/// An order in two packages, each shipped on its own (store sellers, backlog 251, P7): "Partially
/// shipped" between, one letter per package, a seller moving only their own, and the order closed
/// to cancelling and re-addressing once anything has gone.
/// </summary>
public sealed class StoreParcelTransitionsTests : IAsyncLifetime
{
    private SqliteTestDb _sqlite = null!;
    private AppUser _admin = null!, _hazel = null!, _ivan = null!;
    private readonly FakeStoreStripeGateway _stripe = new();
    private readonly FakeStoreTaxService _tax = new();
    private Guid _orderId, _ours, _hers;

    public async Task InitializeAsync()
    {
        StoreAlerts.ResetHourlyLimits();
        _sqlite = await SqliteTestDb.CreateAsync();
        await using var db = await _sqlite.NewContextAsync();
        _admin = StoreTestData.Person(db);
        _hazel = StoreTestData.Person(db, "hazel");
        _hazel.DisplayName = "Hazel Marsh";
        _hazel.Email = "hazel@store.test";
        _ivan = StoreTestData.Person(db, "ivan");
        var super = new Microsoft.AspNetCore.Identity.IdentityRole<Guid> { Id = Guid.NewGuid(), Name = RoleNames.SuperAdmin, NormalizedName = "SUPERADMIN" };
        db.Roles.Add(super);
        db.UserRoles.Add(new Microsoft.AspNetCore.Identity.IdentityUserRole<Guid> { UserId = _admin.Id, RoleId = super.Id });

        var ourVariant = StoreTestData.Variant(db, _admin, price: 20m);
        var herVariant = StoreTestData.Variant(db, _admin, price: 149m);
        var order = StoreTestData.Order(db, StoreOrderStatus.Paid);
        order.PaidUtc = DateTime.UtcNow;
        _orderId = order.Id;
        _ours = Guid.NewGuid();
        _hers = Guid.NewGuid();
        db.StoreOrderParcels.Add(new StoreOrderParcel { Id = _ours, OrderId = order.Id, Number = 1, ShippingAmount = 7.95m, DateCreated = DateTime.UtcNow });
        db.StoreOrderParcels.Add(new StoreOrderParcel { Id = _hers, OrderId = order.Id, Number = 2, SellerAppUserId = _hazel.Id, SellerName = "Hazel Marsh",
            SellerShippingCredit = 7.95m, DateCreated = DateTime.UtcNow });
        db.StoreOrderItems.Add(new StoreOrderItem { Id = Guid.NewGuid(), OrderId = order.Id, ParcelId = _ours, ProductId = ourVariant.ProductId, VariantId = ourVariant.Id,
            ProductName = "K-II EMF Meter", Sku = "KII-EMF", UnitPrice = 20m, Quantity = 1, LineTotal = 20m, DateCreated = DateTime.UtcNow });
        db.StoreOrderItems.Add(new StoreOrderItem { Id = Guid.NewGuid(), OrderId = order.Id, ParcelId = _hers, ProductId = herVariant.ProductId, VariantId = herVariant.Id,
            ProductName = "Hand-Built REM Pod", Sku = "HM-REMPOD", UnitPrice = 149m, Quantity = 1, LineTotal = 149m, DateCreated = DateTime.UtcNow.AddSeconds(1) });
        await db.SaveChangesAsync();
    }

    public Task DisposeAsync() => _sqlite.DisposeAsync().AsTask();

    private (StoreOrderMailer Mailer, StoreAlerts Alerts) Parts()
    {
        var site = Options.Create(new SiteIdentity { Name = "IsHaunted.com", BaseUrl = "https://test.local" });
        var mailer = new StoreOrderMailer(TestOutbox.WithoutMail(_sqlite.Factory), site);
        return (mailer, new StoreAlerts(_sqlite.Factory, new PlatformMessageService(_sqlite.Factory), mailer, NullLogger<StoreAlerts>.Instance));
    }

    private StoreParcelTransitions Parcels() { var (m, a) = Parts(); return new StoreParcelTransitions(_sqlite.Factory, m, a); }

    private StoreOrderTransitions Desk()
    {
        var (mailer, alerts) = Parts();
        var payments = new StoreOrderPayments(_sqlite.Factory, _stripe, _tax, mailer, alerts, NullLogger<StoreOrderPayments>.Instance);
        var refunds = new StoreRefundService(_sqlite.Factory, _stripe, _tax, mailer, alerts, NullLogger<StoreRefundService>.Instance);
        return new StoreOrderTransitions(_sqlite.Factory, mailer, alerts, refunds, payments);
    }

    private static StoreShipmentInfo Usps(string number = "9400 1111") => new(StoreCarriers.Usps, number, null, null);

    private async Task<StoreOrderStatus> StatusAsync()
    {
        await using var db = await _sqlite.NewContextAsync();
        return await db.StoreOrders.Where(o => o.Id == _orderId).Select(o => o.Status).SingleAsync();
    }

    private async Task<List<OutboxEmail>> LettersAsync(string kind)
    {
        await using var db = await _sqlite.NewContextAsync();
        return await db.OutboxEmails.AsNoTracking().Where(l => l.Kind == kind).ToListAsync();
    }

    [Fact]
    public async Task One_package_gone_is_partially_shipped_and_both_gone_is_shipped()
    {
        Assert.True((await Parcels().ShipAsync(_orderId, _ours, Usps(), _admin.Id)).Ok);
        Assert.Equal(StoreOrderStatus.PartiallyShipped, await StatusAsync());

        Assert.True((await Parcels().ShipAsync(_orderId, _hers, new StoreShipmentInfo(StoreCarriers.Ups, "1Z999", null, null), _hazel.Id, seller: _hazel.Id)).Ok);
        Assert.Equal(StoreOrderStatus.Shipped, await StatusAsync());

        Assert.True((await Parcels().DeliverAsync(_orderId, _ours, _admin.Id)).Ok);
        Assert.Equal(StoreOrderStatus.Shipped, await StatusAsync());   // one still on its way
        Assert.True((await Parcels().DeliverAsync(_orderId, _hers, _hazel.Id, seller: _hazel.Id)).Ok);
        Assert.Equal(StoreOrderStatus.Delivered, await StatusAsync());
    }

    [Fact]
    public async Task Each_package_has_its_own_letter_with_only_its_items()
    {
        await Parcels().ShipAsync(_orderId, _ours, Usps(), _admin.Id);
        await Parcels().ShipAsync(_orderId, _hers, new StoreShipmentInfo(StoreCarriers.Ups, "1Z999", null, null), _hazel.Id, seller: _hazel.Id);

        var letters = (await LettersAsync(MailKinds.StoreOrderShipped.Key)).OrderBy(l => l.Subject).ToList();
        Assert.Equal(2, letters.Count);
        Assert.Contains("package 1 of 2", letters[0].Subject);
        Assert.Contains("K-II EMF Meter", letters[0].HtmlBody);
        Assert.DoesNotContain("REM Pod", letters[0].HtmlBody);
        Assert.Contains("package 2 of 2", letters[1].Subject);
        Assert.Contains("1Z999", letters[1].HtmlBody);
        Assert.DoesNotContain("K-II", letters[1].HtmlBody);
    }

    [Fact]
    public async Task Two_packages_shipped_at_once_leave_the_order_shipped()
    {
        // The race the order-row lock closes: each mover must see the other's package as it now is.
        var results = await Task.WhenAll(
            Parcels().ShipAsync(_orderId, _ours, Usps(), _admin.Id),
            Parcels().ShipAsync(_orderId, _hers, Usps("9400 2222"), _hazel.Id, seller: _hazel.Id));

        Assert.All(results, r => Assert.True(r.Ok, r.Refusal));
        Assert.Equal(StoreOrderStatus.Shipped, await StatusAsync());
    }

    /// <summary>
    /// The lock itself, read from the source: SQLite serialises every writer, so the race it closes
    /// can't be shown here — only on SQL Server, where each mover's read of the other packages would
    /// otherwise run beside the other's write. The order row must be written first, inside the
    /// transaction, before any package is read.
    /// </summary>
    [Fact]
    public void The_order_row_is_locked_before_the_packages_are_read()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Ben.slnx"))) dir = dir.Parent;
        var source = File.ReadAllText(Path.Combine(dir!.FullName, "Ben.Data.WebApi", "Services", "Store", "StoreParcelTransitions.cs"));
        var move = source[source.IndexOf("private async Task<StoreDeskResult> MoveAsync(", StringComparison.Ordinal)..];

        var begin = move.IndexOf("BeginTransactionAsync", StringComparison.Ordinal);
        var locked = move.IndexOf("db.StoreOrders.Where(o => o.Id == orderId).ExecuteUpdateAsync", StringComparison.Ordinal);
        var read = move.IndexOf("var tracked = await db.StoreOrderParcels", StringComparison.Ordinal);
        Assert.True(begin > 0 && locked > begin && read > locked,
            "MoveAsync must begin its transaction, write the order row, and only then read the packages.");
    }

    [Fact]
    public async Task A_seller_moves_only_their_own_package()
    {
        Assert.True((await Parcels().ShipAsync(_orderId, _ours, Usps(), _hazel.Id, seller: _hazel.Id)).NotFound);
        Assert.True((await Parcels().ShipAsync(_orderId, _hers, Usps(), _ivan.Id, seller: _ivan.Id)).NotFound);
        Assert.Equal(StoreOrderStatus.Paid, await StatusAsync());
    }

    [Fact]
    public async Task Attention_holds_every_package_a_seller_included()
    {
        await using (var db = await _sqlite.NewContextAsync())
            await db.StoreOrders.Where(o => o.Id == _orderId).ExecuteUpdateAsync(u => u.SetProperty(o => o.NeedsAttention, true).SetProperty(o => o.AttentionReason, "Check the amount."));

        Assert.StartsWith("This order needs attention first", (await Parcels().ShipAsync(_orderId, _hers, Usps(), _hazel.Id, seller: _hazel.Id)).Refusal);
    }

    [Fact]
    public async Task Once_anything_has_gone_the_order_is_neither_cancelled_whole_nor_re_addressed()
    {
        await Parcels().ShipAsync(_orderId, _ours, Usps(), _admin.Id);

        var (cancel, _) = await Desk().CancelAsync(_orderId, new CancelStoreOrderRequest("Changed mind", true), _admin.Id);
        Assert.Equal(StoreOrderDeskSentences.AlreadyOnItsWay, cancel.Refusal);
        var move = await Desk().ChangeAddressAsync(_orderId, new ChangeStoreOrderAddressRequest(
            new StoreAddressInput("Ada", "615-555-0100", "1 Elm", null, "Franklin", "TN", "37064"), null), _admin.Id);
        Assert.Equal(StoreOrderDeskSentences.AddressAfterShipping, move.Refusal);
    }

    [Fact]
    public async Task A_paid_order_tells_each_seller_of_their_package()
    {
        await Parts().Alerts.OrderPaidAsync(_orderId);

        var letter = Assert.Single(await LettersAsync(MailKinds.StoreSellerParcelToShip.Key));
        Assert.Equal("hazel@store.test", letter.To);
        Assert.Contains("REM Pod", letter.HtmlBody);
        Assert.DoesNotContain("K-II", letter.HtmlBody);
        await using var db = await _sqlite.NewContextAsync();
        Assert.True(await db.UserMessageTos.AnyAsync(t => t.ToAppUserId == _hazel.Id && t.UserMessage.MessageSubject!.Contains("a package for you to ship")));
    }

    [Fact]
    public async Task The_seller_sees_the_address_until_the_package_is_delivered()
    {
        var (_, alerts) = Parts();
        var controller = new SellerStoreParcelController(_sqlite.Factory, Parcels()) { ControllerContext = StoreTestData.SignedInAs(_hazel.Id) };
        var before = Assert.Single((IEnumerable<SellerParcelRecord>)((OkObjectResult)(await controller.GetMine(null, default)).Result!).Value!);
        Assert.NotNull(before.ShipTo);
        Assert.Equal(7.95m, before.LabelCredit);
        Assert.Equal((2, 2), (before.Number, before.PackageCount));

        await Parcels().ShipAsync(_orderId, _hers, Usps(), _hazel.Id, seller: _hazel.Id);
        await Parcels().DeliverAsync(_orderId, _hers, _hazel.Id, seller: _hazel.Id);
        var after = (SellerParcelRecord)((OkObjectResult)(await controller.GetById(_hers, default)).Result!).Value!;
        Assert.Null(after.ShipTo);
        Assert.IsType<NotFoundResult>((await controller.GetById(_ours, default)).Result);
        _ = alerts;
    }
}
