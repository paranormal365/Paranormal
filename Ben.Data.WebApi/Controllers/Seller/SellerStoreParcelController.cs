using Ben.Data.Common.Constants;
using Ben.Data.Common.Enums;
using Ben.Data.Source.Context;
using Ben.Data.Source.Entities;
using Ben.Data.WebApi.Services.Store;
using Ben.Service.Models.Store;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Ben.Data.WebApi.Controllers.Seller;

/// <summary>
/// The packages a seller sends (store sellers, backlog 251, P7): the list, one package whole, and
/// packing, shipping, correcting tracking and marking it delivered.
/// </summary>
/// <remarks>
/// <para>Ben, 09/24/2026: "Each seller contributes their own status of shipped." A seller moves
/// only their own packages — another seller's, or the store's, is "not found" — through the same
/// <see cref="StoreParcelTransitions"/> the order desk uses, so the order's roll-up to "Partially
/// shipped" and on is one rule.</para>
///
/// <para><b>The buyer's address</b> is shown until the package is delivered, and not after: the
/// seller needs it to send the package, and has no need of it once it has arrived.</para>
/// </remarks>
[ApiController]
[Authorize(Policy = AuthPolicyNames.Seller)]
[Route("api/seller/store/parcels")]
public sealed class SellerStoreParcelController(IDbContextFactory<BenDataContext> dbFactory, StoreParcelTransitions parcels)
    : SellerStoreControllerBase
{
    private static readonly StoreOrderStatus[] Fulfilling =
        [StoreOrderStatus.Paid, StoreOrderStatus.Packed, StoreOrderStatus.PartiallyShipped, StoreOrderStatus.Shipped];

    /// <summary>The caller's packages from paid orders, the ones still to send first, then newest.</summary>
    [HttpGet]
    public async Task<ActionResult<IEnumerable<SellerParcelRecord>>> GetMine([FromQuery] bool? open, CancellationToken ct)
    {
        var me = GetCurrentUserIdOrThrow();
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var query = db.StoreOrderParcels.AsNoTracking().Where(x => x.SellerAppUserId == me && x.Order.PaidUtc != null);
        if (open == true) query = query.Where(x => x.Status == StoreParcelStatus.Waiting || x.Status == StoreParcelStatus.Packed);
        var ids = await query.OrderBy(x => x.Status == StoreParcelStatus.Waiting || x.Status == StoreParcelStatus.Packed ? 0 : 1)
            .ThenByDescending(x => x.Order.PaidUtc).Select(x => x.Id).Take(200).ToListAsync(ct);
        return Ok(await RecordsAsync(db, ids, ct));
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<SellerParcelRecord>> GetById(Guid id, CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        return await OneAsync(db, id, ct) is { } record ? Ok(record) : NotFound();
    }

    [HttpPost("{id:guid}/pack")]
    public async Task<ActionResult<SellerParcelRecord>> Pack(Guid id, CancellationToken ct)
        => await AnswerAsync(id, (orderId, me) => parcels.PackAsync(orderId, id, me, seller: me, ct), ct);

    [HttpPost("{id:guid}/ship")]
    public async Task<ActionResult<SellerParcelRecord>> Ship(Guid id, [FromBody] StoreShipmentInfo info, CancellationToken ct)
        => await AnswerAsync(id, (orderId, me) => parcels.ShipAsync(orderId, id, info, me, seller: me, ct), ct);

    [HttpPut("{id:guid}/tracking")]
    public async Task<ActionResult<SellerParcelRecord>> Tracking(Guid id, [FromBody] StoreShipmentInfo info, CancellationToken ct)
        => await AnswerAsync(id, (orderId, me) => parcels.CorrectTrackingAsync(orderId, id, info, me, seller: me, ct), ct);

    [HttpPost("{id:guid}/deliver")]
    public async Task<ActionResult<SellerParcelRecord>> Deliver(Guid id, CancellationToken ct)
        => await AnswerAsync(id, (orderId, me) => parcels.DeliverAsync(orderId, id, me, seller: me, ct), ct);

    private async Task<ActionResult<SellerParcelRecord>> AnswerAsync(Guid id, Func<Guid, Guid, Task<StoreDeskResult>> move, CancellationToken ct)
    {
        var me = GetCurrentUserIdOrThrow();
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var orderId = await db.StoreOrderParcels.AsNoTracking().Where(x => x.Id == id && x.SellerAppUserId == me)
            .Select(x => (Guid?)x.OrderId).FirstOrDefaultAsync(ct);
        if (orderId is null) return NotFound();
        var result = await move(orderId.Value, me);
        if (result.NotFound) return NotFound();
        if (result.Refusal is { } no) return BadRequest(no);
        await using var fresh = await dbFactory.CreateDbContextAsync(ct);
        return Ok(await OneAsync(fresh, id, ct));
    }

    private async Task<SellerParcelRecord?> OneAsync(BenDataContext db, Guid id, CancellationToken ct)
    {
        var me = GetCurrentUserIdOrThrow();
        if (!await db.StoreOrderParcels.AnyAsync(x => x.Id == id && x.SellerAppUserId == me && x.Order.PaidUtc != null, ct)) return null;
        return (await RecordsAsync(db, [id], ct)).FirstOrDefault();
    }

    private static async Task<List<SellerParcelRecord>> RecordsAsync(BenDataContext db, IReadOnlyList<Guid> ids, CancellationToken ct)
    {
        var rows = await db.StoreOrderParcels.AsNoTracking().Include(x => x.Order).Include(x => x.Items)
            .Where(x => ids.Contains(x.Id)).AsSplitQuery().ToListAsync(ct);
        var orderIds = rows.Select(x => x.OrderId).Distinct().ToList();
        var counts = await db.StoreOrderParcels.AsNoTracking().Where(x => orderIds.Contains(x.OrderId) && x.Status != StoreParcelStatus.Cancelled)
            .GroupBy(x => x.OrderId).Select(g => new { g.Key, Count = g.Count() }).ToDictionaryAsync(g => g.Key, g => g.Count, ct);

        return ids.Select(id => rows.First(x => x.Id == id)).Select(x =>
        {
            var o = x.Order;
            var sendable = Fulfilling.Contains(o.Status) && !o.NeedsAttention;
            var addressed = x.Status is StoreParcelStatus.Waiting or StoreParcelStatus.Packed or StoreParcelStatus.Shipped;
            return new SellerParcelRecord(
                x.Id, o.Id, o.OrderNumber, x.Number, counts.GetValueOrDefault(o.Id, 1), o.PaidUtc, x.Status,
                x.Items.OrderBy(i => i.DateCreated).Select(i => new SellerParcelLineRecord(i.ProductName, i.VariantName, i.Sku,
                    i.Quantity - i.QuantityRefunded, i.ImageUploadFileId)).Where(l => l.Quantity > 0).ToList(),
                addressed ? StoreOrderViews.Shipping(o) : null, x.SellerShippingCredit, x.Carrier, x.TrackingNumber, x.TrackingUrl,
                x.ShippedUtc, x.DeliveredUtc, o.NeedsAttention,
                CanPack: sendable && x.Status == StoreParcelStatus.Waiting,
                CanShip: sendable && x.Status is StoreParcelStatus.Waiting or StoreParcelStatus.Packed,
                CanCorrectTracking: x.Status == StoreParcelStatus.Shipped,
                CanDeliver: x.Status == StoreParcelStatus.Shipped);
        }).ToList();
    }
}
