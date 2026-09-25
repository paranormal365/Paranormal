using System.Security.Claims;
using Ben.Data.Common;
using Ben.Data.Common.Enums;
using Ben.Data.Common.Mail;
using Ben.Data.Source.Entities;
using Ben.Data.WebApi.Controllers.Store;
using Ben.Data.WebApi.Services;
using Ben.Data.WebApi.Services.Store;
using Ben.Service.Models.Store;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Xunit;

namespace Ben.Web.Tests.Store;

/// <summary>
/// The doors to an order (storefront S4.9): its buyer, its token and — for the status only — the
/// browser that placed it may open it; anybody else gets 404; "find my order" never says whether it found one.
/// </summary>
public sealed class StoreOrderDoorsTests : IAsyncLifetime
{
    private SqliteTestDb _sqlite = null!;
    private AppUser _admin = null!, _member = null!;

    public async Task InitializeAsync()
    {
        _sqlite = await SqliteTestDb.CreateAsync();
        await using var db = await _sqlite.NewContextAsync();
        _admin = StoreTestData.Person(db);
        _member = StoreTestData.Person(db, "member");
        await db.SaveChangesAsync();
    }

    public Task DisposeAsync() => _sqlite.DisposeAsync().AsTask();

    private StoreOrderController Doors(Guid? signedIn = null, string? cartHeader = null)
    {
        var context = new DefaultHttpContext
        {
            User = signedIn is { } who
                ? new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, who.ToString())], "Bearer"))
                : new ClaimsPrincipal(new ClaimsIdentity()),
        };
        if (cartHeader is not null) context.Request.Headers[StoreCartController.CartHeader] = cartHeader;
        var site = Options.Create(new SiteIdentity { Name = "IsHaunted.com", BaseUrl = "https://test.local" });
        return new StoreOrderController(_sqlite.Factory, new StoreOrderMailer(TestOutbox.WithoutMail(_sqlite.Factory), site), site,
            new StoreImageStorage(TestMedia.StorageOnDisk(Path.GetTempPath()), new MediaSanitizationService(), TestMedia.Ingest()))
        {
            ControllerContext = new ControllerContext { HttpContext = context },
        };
    }

    private async Task<StoreOrder> OrderAsync(AppUser? buyer = null, bool paid = true, string? cartToken = null)
    {
        await using var db = await _sqlite.NewContextAsync();
        var order = StoreTestData.Order(db, paid ? StoreOrderStatus.Paid : StoreOrderStatus.PendingPayment, buyer is null ? null : await db.AppUsers.SingleAsync(u => u.Id == buyer.Id));
        order.PaidUtc = paid ? DateTime.UtcNow : null;
        order.PlacedUtc = DateTime.UtcNow;
        if (cartToken is not null)
        {
            var cart = new StoreCart { Id = Guid.NewGuid(), GuestTokenHash = StoreCartCaller.Hash(cartToken), LastActivityUtc = DateTime.UtcNow, DateCreated = DateTime.UtcNow };
            db.StoreCarts.Add(cart);
            order.StoreCartId = cart.Id;
        }
        await db.SaveChangesAsync();
        return order;
    }

    private static string NewToken() => Microsoft.AspNetCore.WebUtilities.WebEncoders.Base64UrlEncode(
        System.Security.Cryptography.RandomNumberGenerator.GetBytes(32));

    [Fact]
    public async Task A_stranger_gets_404_never_403()
    {
        var order = await OrderAsync(_member);

        Assert.IsType<NotFoundResult>((await Doors().Get(order.Id, null, default)).Result);
        Assert.IsType<NotFoundResult>((await Doors().Get(order.Id, "wrong-token", default)).Result);
        Assert.IsType<NotFoundResult>((await Doors(_admin.Id).Get(order.Id, null, default)).Result);   // another member
        Assert.IsType<NotFoundResult>((await Doors().Invoice(order.Id, null, default)).Result);
        Assert.IsType<NotFoundResult>((await Doors().Status(order.Id, null, default)).Result);
    }

    [Fact]
    public async Task The_buyer_or_the_token_opens_the_order_and_its_invoice()
    {
        var mine = await OrderAsync(_member);
        var guests = await OrderAsync();

        var byMember = Assert.IsType<OkObjectResult>((await Doors(_member.Id).Get(mine.Id, null, default)).Result).Value as StoreOrderView;
        var byToken = Assert.IsType<OkObjectResult>((await Doors().Get(guests.Id, guests.AccessToken, default)).Result).Value as StoreOrderView;
        var invoice = Assert.IsType<OkObjectResult>((await Doors().Invoice(guests.Id, guests.AccessToken, default)).Result).Value as StoreInvoiceRecord;

        Assert.Equal(mine.OrderNumber, byMember!.OrderNumber);
        Assert.Equal(guests.OrderNumber, byToken!.OrderNumber);
        Assert.Equal(guests.Total, invoice!.Total);
    }

    /// <summary>Stripe's return page: the browser that placed the order may see how it stands, for an hour.</summary>
    [Fact]
    public async Task The_browser_that_placed_it_sees_the_status_but_not_the_order()
    {
        var token = NewToken();
        var order = await OrderAsync(cartToken: token);

        var status = Assert.IsType<OkObjectResult>((await Doors(cartHeader: token).Status(order.Id, null, default)).Result).Value as StoreOrderStatusView;
        Assert.Equal(StoreOrderStatus.Paid, status!.Status);
        Assert.Equal($"/store/orders/{order.Id}?t={order.AccessToken}", status.OrderUrl);
        Assert.IsType<NotFoundResult>((await Doors(cartHeader: NewToken()).Status(order.Id, null, default)).Result);
        Assert.IsType<NotFoundResult>((await Doors(cartHeader: token).Get(order.Id, null, default)).Result);
    }

    [Fact]
    public async Task Find_my_order_says_the_same_either_way_and_emails_a_match()
    {
        var order = await OrderAsync();

        var hit = await Doors().Lookup(new GuestOrderLookupRequest($"#{order.OrderNumber}", order.BuyerEmail.ToUpperInvariant()), default);
        var miss = await Doors().Lookup(new GuestOrderLookupRequest("999999", "nobody@example.com"), default);

        Assert.IsType<NoContentResult>(hit);
        Assert.IsType<NoContentResult>(miss);
        await using var db = await _sqlite.NewContextAsync();
        var letter = Assert.Single(await db.OutboxEmails.ToListAsync());
        Assert.Equal((order.BuyerEmail, MailKinds.StoreOrderLink.Key), (letter.To, letter.Kind));
    }

    [Fact]
    public async Task A_members_lookup_gets_the_sign_in_letter()
    {
        var order = await OrderAsync(_member);
        await Doors().Lookup(new GuestOrderLookupRequest(order.OrderNumber.ToString(), order.BuyerEmail), default);

        await using var db = await _sqlite.NewContextAsync();
        var letter = Assert.Single(await db.OutboxEmails.ToListAsync());
        Assert.DoesNotContain(order.AccessToken, letter.HtmlBody);
        Assert.Contains("Sign in to see this order", letter.HtmlBody);
    }

    [Fact]
    public async Task My_orders_lists_only_paid_orders_of_mine()
    {
        await OrderAsync(_member);
        await OrderAsync(_member, paid: false);   // an abandoned checkout
        await OrderAsync(_admin);

        var controller = new MyStoreController(_sqlite.Factory)
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, _member.Id.ToString())], "Bearer")),
            } },
        };
        var list = Assert.IsAssignableFrom<IEnumerable<StoreOrderSummaryView>>(Assert.IsType<OkObjectResult>((await controller.Orders(null, null, null, default)).Result).Value);
        Assert.Single(list);
    }
}
