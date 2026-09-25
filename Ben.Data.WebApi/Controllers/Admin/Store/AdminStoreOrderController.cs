using Ben.Data.Common.Constants;
using Ben.Data.Common.Enums;
using Ben.Data.Source.Context;
using Ben.Data.WebApi.Services;
using Ben.Data.WebApi.Services.Billing.StripeIntegration;
using Ben.Data.WebApi.Services.Store;
using Ben.Service.Models.Store;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Ben.Data.WebApi.Controllers.Admin.Store;

/// <summary>
/// The order desk (storefront S5.4): the list and its CSV, one order, and everything done to it —
/// pack, ship, deliver, cancel, refund, the address, notes, attention, letters again, releasing a
/// checkout still waiting for payment.
/// </summary>
/// <remarks>
/// <para><b>SuperAdmin only, and never behind the store switch</b> — orders taken while the shop was
/// open are still shipped and refunded after it closes.</para>
///
/// <para><b>Every action answers with the order as it now stands</b> (or the refusal, in words), so
/// the page never guesses what a press did.</para>
/// </remarks>
[ApiController]
[Authorize(Policy = RoleNames.SuperAdmin)]
[Route("api/admin/store/orders")]
public sealed class AdminStoreOrderController(
    IDbContextFactory<BenDataContext> dbFactory, StoreOrderTransitions desk, StoreRefundService refunds,
    IOptions<StripeOptions> stripe, StoreParcelTransitions parcels, TimeProvider? clock = null) : BenControllerBase
{
    private DateTime Now => (clock ?? TimeProvider.System).GetUtcNow().UtcDateTime;

    private static StoreOrderFilter Filter(StoreOrderStatus? status, string? q, string? coupon, DateTime? from, DateTime? to,
        bool? needsAction, string? refunds, bool? unpaid, bool? attention)
        => new(status, q, coupon, from, to, needsAction == true, refunds == "attention", unpaid == true, attention == true);

    [HttpGet]
    public async Task<ActionResult<IEnumerable<StoreOrderListRecord>>> List(
        [FromQuery] StoreOrderStatus? status, [FromQuery] string? q, [FromQuery] string? coupon,
        [FromQuery] DateTime? from, [FromQuery] DateTime? to, [FromQuery] bool? needsAction, [FromQuery] string? refunds,
        [FromQuery] bool? unpaid, [FromQuery] bool? attention, [FromQuery] int? page, [FromQuery] int? pageSize, CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var rows = await StoreOrderDesk.ListAsync(db, Filter(status, q, coupon, from, to, needsAction, refunds, unpaid, attention), Now, ct);
        return Ok(ListPaging.Apply(rows, page, pageSize, Response));
    }

    /// <summary>One row per order item, filtered like the list.</summary>
    [HttpGet("export.csv")]
    public async Task<IActionResult> Export(
        [FromQuery] StoreOrderStatus? status, [FromQuery] string? q, [FromQuery] string? coupon,
        [FromQuery] DateTime? from, [FromQuery] DateTime? to, [FromQuery] bool? needsAction, [FromQuery] string? refunds,
        [FromQuery] bool? unpaid, [FromQuery] bool? attention, CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var lines = await StoreOrderDesk.ExportAsync(db, Filter(status, q, coupon, from, to, needsAction, refunds, unpaid, attention), Now, ct);
        return File(StoreCsv.Bytes(lines), "text/csv", $"store-orders-{Now:yyyy-MM-dd}.csv");
    }

    [HttpGet("refunds/export.csv")]
    public async Task<IActionResult> ExportRefunds([FromQuery] DateTime? from, [FromQuery] DateTime? to, CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        return File(StoreCsv.Bytes(await StoreOrderDesk.RefundsExportAsync(db, from, to, ct)), "text/csv", $"store-refunds-{Now:yyyy-MM-dd}.csv");
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<StoreOrderDetailAdminRecord>> Get(Guid id, CancellationToken ct)
        => await DetailAsync(id, ct) is { } detail ? Ok(detail) : NotFound();

    [HttpGet("{id:guid}/invoice")]
    public async Task<ActionResult<StoreInvoiceRecord>> Invoice(Guid id, [FromServices] Microsoft.Extensions.Options.IOptions<Ben.Data.Common.SiteIdentity> site, CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var order = await db.StoreOrders.AsNoTracking().Include(o => o.Items).Include(o => o.Parcels).Include(o => o.Refunds)
            .AsSplitQuery().FirstOrDefaultAsync(o => o.Id == id, ct);
        if (order is null || order.PaidUtc is null) return NotFound();
        var settings = await StoreSettingsReader.ReadAsync(db, ct);
        var support = settings.SupportEmail ?? await Ben.Data.WebApi.Services.SiteSettingsService.GetAsync(db, SiteSettingKeys.PublicContactEmail, ct);
        return Ok(StoreOrderViews.Invoice(order, settings, site.Value.Name, support));
    }

    // ── packages (store sellers P7): each is packed, shipped and delivered on its own ──

    [HttpPost("{id:guid}/parcels/{parcelId:guid}/pack")]
    public async Task<IActionResult> Pack(Guid id, Guid parcelId, CancellationToken ct)
        => await AnswerAsync(id, await parcels.PackAsync(id, parcelId, GetCurrentUserIdOrThrow(), ct: ct), ct);

    [HttpPost("{id:guid}/parcels/{parcelId:guid}/ship")]
    public async Task<IActionResult> Ship(Guid id, Guid parcelId, [FromBody] StoreShipmentInfo info, CancellationToken ct)
        => await AnswerAsync(id, await parcels.ShipAsync(id, parcelId, info, GetCurrentUserIdOrThrow(), ct: ct), ct);

    [HttpPut("{id:guid}/parcels/{parcelId:guid}/tracking")]
    public async Task<IActionResult> Tracking(Guid id, Guid parcelId, [FromBody] StoreShipmentInfo info, CancellationToken ct)
        => await AnswerAsync(id, await parcels.CorrectTrackingAsync(id, parcelId, info, GetCurrentUserIdOrThrow(), ct: ct), ct);

    [HttpPost("{id:guid}/parcels/{parcelId:guid}/deliver")]
    public async Task<IActionResult> Deliver(Guid id, Guid parcelId, CancellationToken ct)
        => await AnswerAsync(id, await parcels.DeliverAsync(id, parcelId, GetCurrentUserIdOrThrow(), ct: ct), ct);

    [HttpPost("{id:guid}/cancel")]
    public async Task<IActionResult> Cancel(Guid id, [FromBody] CancelStoreOrderRequest request, CancellationToken ct)
    {
        var (result, refund) = await desk.CancelAsync(id, request, GetCurrentUserIdOrThrow(), ct);
        if (refund?.Kind == StoreRefundOutcomeKind.Unavailable) return StatusCode(StatusCodes.Status503ServiceUnavailable, refund.Sentence);
        if (refund?.Kind == StoreRefundOutcomeKind.StripeRefused) return StatusCode(StatusCodes.Status502BadGateway, refund.Sentence);
        if (refund?.Kind == StoreRefundOutcomeKind.Pending) return Accepted(await DetailAsync(id, ct));
        return await AnswerAsync(id, result, ct);
    }

    [HttpPost("{id:guid}/notes")]
    public async Task<IActionResult> Note(Guid id, [FromBody] AddStoreOrderNoteRequest request, CancellationToken ct)
        => await AnswerAsync(id, await desk.AddNoteAsync(id, request.Note, GetCurrentUserIdOrThrow(), ct), ct);

    [HttpPut("{id:guid}/address")]
    public async Task<IActionResult> Address(Guid id, [FromBody] ChangeStoreOrderAddressRequest request, CancellationToken ct)
        => await AnswerAsync(id, await desk.ChangeAddressAsync(id, request, GetCurrentUserIdOrThrow(), ct), ct);

    [HttpPost("{id:guid}/attention/clear")]
    public async Task<IActionResult> ClearAttention(Guid id, CancellationToken ct)
        => await AnswerAsync(id, await desk.ClearAttentionAsync(id, GetCurrentUserIdOrThrow(), ct), ct);

    [HttpPost("{id:guid}/resend")]
    public async Task<IActionResult> Resend(Guid id, [FromBody] ResendStoreOrderLetterRequest request, CancellationToken ct)
    {
        var me = GetCurrentUserIdOrThrow();
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var name = await db.AppUsers.AsNoTracking().Where(u => u.Id == me).Select(u => u.DisplayName ?? u.Email).FirstOrDefaultAsync(ct);
        return await AnswerAsync(id, await desk.ResendLetterAsync(id, request.Kind, me, name, ct), ct);
    }

    [HttpPost("{id:guid}/release")]
    public async Task<IActionResult> Release(Guid id, CancellationToken ct)
        => await AnswerAsync(id, await desk.ReleaseAsync(id, ct), ct);

    // ── Refunds ──────────────────────────────────────────────────────────────

    [HttpGet("{id:guid}/refunds")]
    public async Task<ActionResult<IEnumerable<StoreRefundRecord>>> Refunds(Guid id, CancellationToken ct)
        => await DetailAsync(id, ct) is { } detail ? Ok(detail.Refunds) : NotFound();

    [HttpPost("{id:guid}/refunds")]
    public async Task<IActionResult> Refund(Guid id, [FromBody] StoreRefundRequest request, CancellationToken ct)
        => await RefundAnswerAsync(id, await refunds.RefundAsync(id, request, GetCurrentUserIdOrThrow(), ct: ct), ct);

    [HttpPost("{id:guid}/refunds/{refundId:guid}/retry")]
    public async Task<IActionResult> Retry(Guid id, Guid refundId, CancellationToken ct)
    {
        await using (var db = await dbFactory.CreateDbContextAsync(ct))
            if (!await db.StoreRefunds.AnyAsync(r => r.Id == refundId && r.OrderId == id, ct)) return NotFound();
        return await RefundAnswerAsync(id, await refunds.RetryAsync(refundId, GetCurrentUserIdOrThrow(), ct), ct);
    }

    [HttpPost("{id:guid}/refunds/{refundId:guid}/retry-tax")]
    public async Task<IActionResult> RetryTax(Guid id, Guid refundId, CancellationToken ct)
    {
        await using (var db = await dbFactory.CreateDbContextAsync(ct))
            if (!await db.StoreRefunds.AnyAsync(r => r.Id == refundId && r.OrderId == id, ct)) return NotFound();
        return await refunds.RetryTaxAsync(refundId, ct)
            ? Ok(await DetailAsync(id, ct))
            : BadRequest("The tax reversal couldn't be filed — there is no tax filing yet, it is already reversed, or Stripe Tax refused; the order's history says which.");
    }

    // ── plumbing ─────────────────────────────────────────────────────────────

    private async Task<StoreOrderDetailAdminRecord?> DetailAsync(Guid id, CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        return await StoreOrderDesk.DetailAsync(db, id, stripe.Value.SecretKey, Now, ct);
    }

    private async Task<IActionResult> AnswerAsync(Guid id, StoreDeskResult result, CancellationToken ct)
        => result.NotFound ? NotFound()
         : result.Refusal is { } why ? Conflict(why)
         : Ok(await DetailAsync(id, ct));

    private async Task<IActionResult> RefundAnswerAsync(Guid id, StoreRefundAttempt attempt, CancellationToken ct) => attempt.Kind switch
    {
        StoreRefundOutcomeKind.NotFound => NotFound(),
        StoreRefundOutcomeKind.Refused => BadRequest(attempt.Sentence),
        StoreRefundOutcomeKind.Unavailable => StatusCode(StatusCodes.Status503ServiceUnavailable, attempt.Sentence),
        StoreRefundOutcomeKind.StripeRefused => StatusCode(StatusCodes.Status502BadGateway, attempt.Sentence),
        StoreRefundOutcomeKind.Pending => Accepted(await DetailAsync(id, ct)),
        _ => Ok(await DetailAsync(id, ct)),
    };
}
