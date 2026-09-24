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
/// An order's journey after payment (storefront S5.3): pack, ship — with its tracking, or "No
/// tracking provided" (Ben, 09/24) — deliver, and the desk's other work on it.
/// </summary>
public sealed class StoreOrderTransitionsTests : IAsyncLifetime
{
    private SqliteTestDb _sqlite = null!;
    private AppUser _admin = null!;
    private readonly FakeStoreStripeGateway _stripe = new();
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

    private StoreOrderTransitions Desk()
    {
        var site = Options.Create(new SiteIdentity { Name = "IsHaunted.com", BaseUrl = "https://test.local" });
        var mailer = new StoreOrderMailer(TestOutbox.WithoutMail(_sqlite.Factory), site);
        var alerts = new StoreAlerts(_sqlite.Factory, new PlatformMessageService(_sqlite.Factory), mailer, NullLogger<StoreAlerts>.Instance);
        var payments = new StoreOrderPayments(_sqlite.Factory, _stripe, _tax, mailer, alerts, NullLogger<StoreOrderPayments>.Instance);
        var refunds = new StoreRefundService(_sqlite.Factory, _stripe, _tax, mailer, alerts, NullLogger<StoreRefundService>.Instance);
        return new StoreOrderTransitions(_sqlite.Factory, mailer, alerts, refunds, payments);
    }

    private async Task<Guid> OrderAsync(StoreOrderStatus status = StoreOrderStatus.Paid, string? attention = null, string? carrier = null)
    {
        await using var db = await _sqlite.NewContextAsync();
        var order = StoreTestData.Order(db, status);
        order.PaidUtc = status == StoreOrderStatus.PendingPayment ? null : DateTime.UtcNow;
        order.AccessToken = $"old-token-{order.Id:N}";
        order.NeedsAttention = attention is not null;
        order.AttentionReason = attention;
        order.Carrier = carrier;
        await db.SaveChangesAsync();
        return order.Id;
    }

    private async Task<(StoreOrder Order, List<OutboxEmail> Letters, List<StoreOrderEvent> Events)> ReadAsync(Guid id)
    {
        await using var db = await _sqlite.NewContextAsync();
        return (await db.StoreOrders.AsNoTracking().SingleAsync(o => o.Id == id),
                await db.OutboxEmails.AsNoTracking().ToListAsync(),
                await db.StoreOrderEvents.AsNoTracking().Where(e => e.OrderId == id).ToListAsync());
    }

    private static StoreShipmentInfo Tracked(string carrier = StoreCarriers.Usps, string? number = "9400 1111 2222 3333") => new(carrier, number, null, null);
    private static StoreShipmentInfo Untracked(string carrier = StoreCarriers.Usps) => new(carrier, null, null, null, NoTracking: true);

    // ── Pack ─────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Pack_is_refused_while_attention_is_set()
    {
        var id = await OrderAsync(attention: "Stripe reports $10.00 received; the order is $59.99.");
        var result = await Desk().PackAsync(id, _admin.Id);

        Assert.Equal(StoreOrderDeskSentences.NeedsAttentionFirst("Stripe reports $10.00 received; the order is $59.99."), result.Refusal);
        Assert.Equal(StoreOrderStatus.Paid, (await ReadAsync(id)).Order.Status);
    }

    [Fact]
    public async Task Packing_twice_says_what_it_already_is()
    {
        var id = await OrderAsync();
        Assert.True((await Desk().PackAsync(id, _admin.Id)).Ok);
        Assert.Equal(StoreOrderDeskSentences.OnlyPaidCanBePacked("packed"), (await Desk().PackAsync(id, _admin.Id)).Refusal);
    }

    // ── Ship ─────────────────────────────────────────────────────────────────

    [Fact]
    public async Task A_tracked_shipment_builds_the_carriers_link_and_writes_to_the_buyer_once()
    {
        var id = await OrderAsync(StoreOrderStatus.Packed);
        var result = await Desk().ShipAsync(id, Tracked(), _admin.Id);

        var (order, letters, events) = await ReadAsync(id);
        Assert.True(result.Ok);
        Assert.Equal((StoreOrderStatus.Shipped, StoreCarriers.Usps, "9400 1111 2222 3333"), (order.Status, order.Carrier, order.TrackingNumber));
        Assert.Equal(StoreCarriers.TrackingUrl(StoreCarriers.Usps, "9400 1111 2222 3333"), order.TrackingUrl);
        var letter = Assert.Single(letters, l => l.Kind == MailKinds.StoreOrderShipped.Key);
        Assert.Contains("9400 1111 2222 3333", letter.HtmlBody);
        Assert.Contains(events, e => e.Kind == StoreOrderEventKind.Shipped);
    }

    [Fact]
    public async Task No_tracking_provided_ships_without_a_number_or_a_link_and_the_letter_says_so()
    {
        var id = await OrderAsync();
        var result = await Desk().ShipAsync(id, Untracked(), _admin.Id);

        var (order, letters, events) = await ReadAsync(id);
        Assert.True(result.Ok);
        Assert.Equal((StoreOrderStatus.Shipped, StoreCarriers.Usps, (string?)null, (string?)null),
            (order.Status, order.Carrier, order.TrackingNumber, order.TrackingUrl));
        Assert.Contains("without tracking", Assert.Single(letters, l => l.Kind == MailKinds.StoreOrderShipped.Key).HtmlBody);
        Assert.Contains(events, e => e.Kind == StoreOrderEventKind.Shipped && e.Note!.Contains(StoreOrderDeskSentences.NoTrackingLabel));
    }

    [Theory]
    [InlineData("Pony Express", "9400", false, null, StoreOrderDeskSentences.PickACarrier)]
    [InlineData(StoreCarriers.Ups, "  ", false, null, StoreOrderDeskSentences.NeedsTracking)]
    [InlineData(StoreCarriers.Other, "AB12", false, "http://track.example.com/AB12", StoreOrderDeskSentences.TrackingLinkHttps)]
    public async Task A_shipment_as_typed_is_refused_in_words(string carrier, string number, bool untracked, string? link, string sentence)
    {
        var id = await OrderAsync();
        var result = await Desk().ShipAsync(id, new StoreShipmentInfo(carrier, number, link, null, untracked), _admin.Id);

        Assert.Equal(sentence, result.Refusal);
        var (order, letters, _) = await ReadAsync(id);
        Assert.Equal(StoreOrderStatus.Paid, order.Status);
        Assert.Empty(letters);
    }

    [Fact]
    public async Task Other_takes_a_pasted_https_link()
    {
        var id = await OrderAsync();
        await Desk().ShipAsync(id, new StoreShipmentInfo(StoreCarriers.Other, "AB12", "https://track.example.com/AB12", null), _admin.Id);
        Assert.Equal("https://track.example.com/AB12", (await ReadAsync(id)).Order.TrackingUrl);
    }

    [Fact]
    public async Task Ship_is_refused_while_attention_is_set()
    {
        var id = await OrderAsync(attention: "Paid after the checkout was cancelled.");
        Assert.StartsWith("This order needs attention first", (await Desk().ShipAsync(id, Tracked(), _admin.Id)).Refusal);
    }

    [Fact]
    public async Task Tracking_can_be_added_later_to_an_untracked_parcel_and_only_once_shipped()
    {
        var id = await OrderAsync();
        Assert.Equal(StoreOrderDeskSentences.TrackingOnlyWhenShipped, (await Desk().CorrectTrackingAsync(id, Tracked(), _admin.Id)).Refusal);

        await Desk().ShipAsync(id, Untracked(), _admin.Id);
        Assert.True((await Desk().CorrectTrackingAsync(id, Tracked(StoreCarriers.Ups, "1Z999"), _admin.Id)).Ok);

        var order = (await ReadAsync(id)).Order;
        Assert.Equal((StoreCarriers.Ups, "1Z999", StoreCarriers.TrackingUrl(StoreCarriers.Ups, "1Z999")), (order.Carrier, order.TrackingNumber, order.TrackingUrl));
    }

    [Fact]
    public async Task Only_a_shipped_order_can_be_delivered()
    {
        var id = await OrderAsync();
        Assert.Equal(StoreOrderDeskSentences.OnlyShippedCanBeDelivered, (await Desk().DeliverAsync(id, _admin.Id)).Refusal);
        await Desk().ShipAsync(id, Tracked(), _admin.Id);
        Assert.True((await Desk().DeliverAsync(id, _admin.Id)).Ok);
        Assert.Equal(StoreOrderStatus.Delivered, (await ReadAsync(id)).Order.Status);
    }

    [Fact]
    public async Task A_shipped_order_is_refunded_not_cancelled()
    {
        var id = await OrderAsync(StoreOrderStatus.Shipped, carrier: StoreCarriers.Usps);
        var (result, _) = await Desk().CancelAsync(id, new CancelStoreOrderRequest("Changed mind", true), _admin.Id);
        Assert.Equal(StoreOrderDeskSentences.AlreadyOnItsWay, result.Refusal);
    }

    // ── Address, attention, letters ──────────────────────────────────────────

    private static StoreAddressInput Address(string state = "TN") => new("Ada Buyer", "615-555-0199", "9 Oak Ave", null, "Franklin", state, "37064");

    [Fact]
    public async Task Email_change_rotates_the_link_and_sends_the_receipt_to_the_new_address()
    {
        var id = await OrderAsync();
        var result = await Desk().ChangeAddressAsync(id, new ChangeStoreOrderAddressRequest(Address(), "new@example.com"), _admin.Id);

        var (order, letters, events) = await ReadAsync(id);
        Assert.True(result.Ok);
        Assert.Equal(("new@example.com", "9 Oak Ave"), (order.BuyerEmail, order.ShipStreet1));
        Assert.NotEqual($"old-token-{id:N}", order.AccessToken);
        Assert.Equal("new@example.com", Assert.Single(letters, l => l.Kind == MailKinds.StoreOrderConfirmation.Key).To);
        Assert.Contains(events, e => e.Note == "The emailed link was replaced");
        Assert.Contains(events, e => e.Kind == StoreOrderEventKind.AddressChanged && e.Note!.Contains("13 Crossroads Lane"));
    }

    [Fact]
    public async Task A_new_state_is_noted_and_tax_is_not_recalculated()
    {
        var id = await OrderAsync();
        await Desk().ChangeAddressAsync(id, new ChangeStoreOrderAddressRequest(Address("KY") with { Zip = "40202", City = "Louisville" }, null), _admin.Id);

        var (order, letters, events) = await ReadAsync(id);
        Assert.Equal(("KY", 59.99m), (order.ShipState, order.Total));
        Assert.Equal($"old-token-{id:N}", order.AccessToken);
        Assert.Empty(letters);
        Assert.Contains(events, e => e.Note == "Sales tax was calculated for TN; it was not recalculated for KY.");
    }

    [Fact]
    public async Task The_address_cannot_change_once_shipped()
    {
        var id = await OrderAsync(StoreOrderStatus.Shipped, carrier: StoreCarriers.Usps);
        Assert.Equal(StoreOrderDeskSentences.AddressAfterShipping,
            (await Desk().ChangeAddressAsync(id, new ChangeStoreOrderAddressRequest(Address(), null), _admin.Id)).Refusal);
    }

    [Fact]
    public async Task Resend_queues_exactly_one_letter_of_the_asked_kind()
    {
        var id = await OrderAsync(StoreOrderStatus.Shipped, carrier: StoreCarriers.Usps);
        Assert.True((await Desk().ResendLetterAsync(id, "shipped", _admin.Id, "Ben")).Ok);

        var (_, letters, events) = await ReadAsync(id);
        Assert.Equal(MailKinds.StoreOrderShipped.Key, Assert.Single(letters).Kind);
        Assert.Contains(events, e => e.Note == "The shipped letter was re-sent by Ben");
    }

    [Fact]
    public async Task The_shipped_letter_is_not_resent_before_there_is_a_shipment()
    {
        var id = await OrderAsync();
        Assert.Equal(StoreOrderDeskSentences.ShippedLetterNeedsCarrier, (await Desk().ResendLetterAsync(id, "shipped", _admin.Id, "Ben")).Refusal);
        Assert.Empty((await ReadAsync(id)).Letters);
    }

    [Fact]
    public async Task Clearing_attention_lets_the_order_be_packed()
    {
        var id = await OrderAsync(attention: "Check it");
        Assert.True((await Desk().ClearAttentionAsync(id, _admin.Id)).Ok);
        Assert.True((await Desk().PackAsync(id, _admin.Id)).Ok);
        Assert.Equal(StoreOrderDeskSentences.NothingToClear, (await Desk().ClearAttentionAsync(id, _admin.Id)).Refusal);
    }

    [Fact]
    public async Task Only_an_open_checkout_can_be_released()
    {
        var paid = await OrderAsync();
        Assert.Equal(StoreOrderDeskSentences.OnlyOpenCheckoutsRelease, (await Desk().ReleaseAsync(paid)).Refusal);

        var open = await OrderAsync(StoreOrderStatus.PendingPayment);
        Assert.True((await Desk().ReleaseAsync(open)).Ok);
        Assert.Equal(StoreOrderStatus.Cancelled, (await ReadAsync(open)).Order.Status);
    }
}
