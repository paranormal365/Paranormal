using Ben.Data.Common;
using Ben.Data.Common.Enums;
using Ben.Data.Source.Context;
using Ben.Data.Source.Entities;
using Ben.Data.WebApi.Services;
using Ben.Data.WebApi.Services.Store;
using Ben.Service.Models.Store;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Ben.Data.WebApi.Controllers.Store;

/// <summary>
/// The doors to one order (storefront S4.9): its status while payment settles, the order itself,
/// its invoice, and "find my order" by number and email.
/// </summary>
/// <remarks>
/// <para><b>Never behind the store switch.</b> A buyer who has just been charged must reach the
/// thank-you page and the emailed link whatever the switch says — and the webhook still fulfils
/// with the shop hidden.</para>
///
/// <para><b>Who may open an order</b>: its buyer signed in, anybody holding its token
/// (<c>?t=</c>, from the letter), or — for the status only — the browser whose cart placed it,
/// within the hour. Anyone else gets 404, never 403: "there is an order 100042 you may not see" is
/// itself something a stranger should not learn.</para>
///
/// <para><b>Anonymous per action, never on the class</b> — a class-level [AllowAnonymous] would
/// silence every [Authorize] beside it.</para>
/// </remarks>
[ApiController]
[Route("api/store/orders")]
[EnableRateLimiting(RateLimiting.StoreOrderDoorPolicy)]
public sealed class StoreOrderController(
    IDbContextFactory<BenDataContext> dbFactory, StoreOrderMailer mailer, IOptions<SiteIdentity> site) : BenControllerBase
{
    public static readonly TimeSpan CartOwnershipWindow = TimeSpan.FromHours(1);

    [HttpGet("{id:guid}/status")]
    [AllowAnonymous]
    public async Task<ActionResult<StoreOrderStatusView>> Status(Guid id, [FromQuery] string? t, CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var order = await db.StoreOrders.AsNoTracking().FirstOrDefaultAsync(o => o.Id == id, ct);
        if (order is null) return NotFound();

        var (mayRead, owner) = await AccessAsync(db, order, t, allowCart: true, ct);
        if (!mayRead) return NotFound();

        Response.Headers.CacheControl = "no-store";
        var problem = order.Status == StoreOrderStatus.Cancelled && order.PaidUtc is null
            ? order.CancellationReason ?? "This checkout was cancelled."
            : null;
        return Ok(new StoreOrderStatusView(order.Id, order.OrderNumber, order.Status, StoreOrderViews.IsFinal(order), problem,
            owner ? StoreOrderMailer.ViewPath(order) : null, order.ReservationExpiresUtc));
    }

    [HttpGet("{id:guid}")]
    [AllowAnonymous]
    public async Task<ActionResult<StoreOrderView>> Get(Guid id, [FromQuery] string? t, CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var order = await LoadAsync(db, id, ct);
        if (order is null || !(await AccessAsync(db, order, t, allowCart: false, ct)).MayRead) return NotFound();

        var settings = await StoreSettingsReader.ReadAsync(db, ct);
        var slugs = await ProductSlugsAsync(db, order, ct);
        Response.Headers.CacheControl = "no-store";
        return Ok(StoreOrderViews.View(order, slugs, settings.ReturnsWindowDays, await SupportAsync(db, settings, ct)));
    }

    [HttpGet("{id:guid}/invoice")]
    [AllowAnonymous]
    public async Task<ActionResult<StoreInvoiceRecord>> Invoice(Guid id, [FromQuery] string? t, CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var order = await LoadAsync(db, id, ct);
        if (order is null || order.PaidUtc is null || !(await AccessAsync(db, order, t, allowCart: false, ct)).MayRead) return NotFound();

        var settings = await StoreSettingsReader.ReadAsync(db, ct);
        Response.Headers.CacheControl = "no-store";
        return Ok(StoreOrderViews.Invoice(order, settings, site.Value.Name, await SupportAsync(db, settings, ct)));
    }

    /// <summary>
    /// "Find my order": always 204, whether or not anything matched — the answer must not tell a
    /// stranger which email bought what. A match gets its link by email (at most one per 10 minutes).
    /// </summary>
    [HttpPost("lookup")]
    [AllowAnonymous]
    public async Task<IActionResult> Lookup([FromBody] GuestOrderLookupRequest request, CancellationToken ct)
    {
        var digits = new string((request.OrderNumber ?? "").Where(char.IsAsciiDigit).ToArray());
        if (!int.TryParse(digits, out var number) || string.IsNullOrWhiteSpace(request.Email)) return NoContent();

        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var email = StoreEmail.Normalize(request.Email);
        var order = await db.StoreOrders.FirstOrDefaultAsync(o => o.OrderNumber == number && o.BuyerEmailNormalized == email && o.PaidUtc != null, ct);
        if (order is not null && await mailer.QueueOrderLinkAsync(db, order, DateTime.UtcNow, ct))
            await db.SaveChangesAsync(ct);
        return NoContent();
    }

    private async Task<(bool MayRead, bool Owner)> AccessAsync(BenDataContext db, StoreOrder order, string? token, bool allowCart, CancellationToken ct)
    {
        if (await GetCurrentUserIdOrNullAcrossSchemesAsync() is { } me && order.BuyerAppUserId == me) return (true, true);
        if (!string.IsNullOrEmpty(token) && CryptographicEquals(token, order.AccessToken)) return (true, true);
        if (allowCart && order.StoreCartId is { } cartId && order.PlacedUtc > DateTime.UtcNow - CartOwnershipWindow)
        {
            var header = Request.Headers[StoreCartController.CartHeader].ToString();
            if (StoreCartCaller.IsWellFormed(header))
            {
                var hash = StoreCartCaller.Hash(header);
                if (await db.StoreCarts.AnyAsync(c => c.Id == cartId && c.GuestTokenHash == hash, ct)) return (true, true);
            }
        }
        return (false, false);
    }

    private static bool CryptographicEquals(string a, string b)
        => System.Security.Cryptography.CryptographicOperations.FixedTimeEquals(
            System.Text.Encoding.UTF8.GetBytes(a), System.Text.Encoding.UTF8.GetBytes(b));

    private static Task<StoreOrder?> LoadAsync(BenDataContext db, Guid id, CancellationToken ct)
        => db.StoreOrders.AsNoTracking().Include(o => o.Items).Include(o => o.Refunds).Include(o => o.Events)
            .AsSplitQuery().FirstOrDefaultAsync(o => o.Id == id, ct);

    private static async Task<IReadOnlyDictionary<Guid, string>> ProductSlugsAsync(BenDataContext db, StoreOrder order, CancellationToken ct)
    {
        var ids = order.Items.Select(i => i.ProductId).Distinct().ToList();
        return await db.StoreProducts.AsNoTracking().Where(p => ids.Contains(p.Id) && p.IsActive && p.Category.IsActive
                && (p.Category.ParentCategoryId == null || p.Category.ParentCategory!.IsActive))
            .ToDictionaryAsync(p => p.Id, p => p.Slug, ct);
    }

    private static async Task<string?> SupportAsync(BenDataContext db, StoreSettingsSnapshot s, CancellationToken ct)
        => s.SupportEmail ?? await SiteSettingsService.GetAsync(db, SiteSettingKeys.PublicContactEmail, ct);
}

/// <summary>My Orders (storefront S4.9): a signed-in person's paid orders. Never behind the store switch.</summary>
/// <remarks>Checkouts that were never paid are not orders a person placed, and are left out.</remarks>
[ApiController]
[Route("api/me/store")]
[Authorize]
public sealed class MyStoreController(IDbContextFactory<BenDataContext> dbFactory) : BenControllerBase
{
    [HttpGet("orders")]
    public async Task<ActionResult<IEnumerable<StoreOrderSummaryView>>> Orders(
        [FromQuery] int? page, [FromQuery] int? pageSize, [FromQuery] int? months, CancellationToken ct)
    {
        var me = GetCurrentUserIdOrThrow();
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        await StoreGuestOrders.AttachAsync(db, me, ct);   // orders placed as a guest with this confirmed email (S6.1)
        var query = db.StoreOrders.AsNoTracking().Include(o => o.Items).Where(o => o.BuyerAppUserId == me && o.PaidUtc != null);
        if (months is > 0) { var since = DateTime.UtcNow.AddMonths(-months.Value); query = query.Where(o => o.PlacedUtc >= since); }
        var orders = await query.OrderByDescending(o => o.PlacedUtc).ToListAsync(ct);
        return Ok(ListPaging.Apply(orders.Select(StoreOrderViews.Summary).ToList(), page, pageSize, Response));
    }
}
