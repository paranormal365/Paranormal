using Ben.Data.Common.Constants;
using Ben.Data.Common.Enums;
using Ben.Data.Source.Context;
using Ben.Data.WebApi.Services.Store;
using Ben.Service.Models.Store;
using Ben.Data.WebApi.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Ben.Data.WebApi.Controllers.Admin.Store;

/// <summary>
/// Sellers asking for their items to go on sale (store sellers, backlog 251, P3): the queue, and
/// the store's yes or no.
/// </summary>
/// <remarks>
/// <para><b>Price, then approve.</b> The store sets the selling price in the product editor; approval
/// then puts the item on sale and fixes the seller's asking price onto it as what they earn a unit
/// on top of cost (Ben, 09/24/2026). Approval runs the same checks as Put on sale, and each open
/// request carries them, so the queue shows what is still missing before anybody presses a button.</para>
///
/// <para><b>A no says why.</b> Declining needs a note; the seller reads it on their item and in
/// their bell.</para>
/// </remarks>
[ApiController]
[Authorize(Policy = RoleNames.SuperAdmin)]
[Route("api/admin/store/sale-requests")]
public sealed class AdminStoreSaleRequestController(
    IDbContextFactory<BenDataContext> dbFactory, StoreImageStorage images, ICmsMarkupSanitizer sanitizer, StoreSellerAlerts alerts)
    : BenControllerBase
{
    private StoreEditActor Me => new(GetCurrentUserIdOrThrow(), StoreChangeActor.Store);

    /// <summary>The requests waiting, oldest first — or, with <paramref name="decided"/>, the ones answered, newest first.</summary>
    [HttpGet]
    public async Task<ActionResult<IEnumerable<StoreSaleRequestRecord>>> GetAll([FromQuery] bool? decided, CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var rows = decided == true
            ? await StoreProductRecords.RequestsAsync(db, db.StoreProductSaleRequests.Where(r => r.Status != StoreSaleRequestStatus.Open), ct)
            : (await StoreProductRecords.RequestsAsync(db, db.StoreProductSaleRequests.Where(r => r.Status == StoreSaleRequestStatus.Open), ct))
                .OrderBy(r => r.RequestedUtc).ToList();
        return Ok(rows);
    }

    [HttpPost("{id:guid}/approve")]
    public async Task<ActionResult<StoreSaleRequestRecord>> Approve(Guid id, CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var (request, refusal) = await StoreProductSale.ApproveAsync(db, new StoreProductEditor(sanitizer, images), id, Me, ct);
        if (refusal is not null) return this.Refused(refusal);
        await alerts.ApprovedAsync(request!.ProductId, ct);
        return Ok(await OneAsync(db, id, ct));
    }

    [HttpPost("{id:guid}/decline")]
    public async Task<ActionResult<StoreSaleRequestRecord>> Decline(Guid id, [FromBody] DeclineSaleRequest request, CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var (declined, refusal) = await StoreProductSale.DeclineAsync(db, id, request.Note, Me, ct);
        if (refusal is not null) return this.Refused(refusal);
        await alerts.DeclinedAsync(declined!.ProductId, request.Note.Trim(), ct);
        return Ok(await OneAsync(db, id, ct));
    }

    private static async Task<StoreSaleRequestRecord?> OneAsync(BenDataContext db, Guid id, CancellationToken ct)
        => (await StoreProductRecords.RequestsAsync(db, db.StoreProductSaleRequests.Where(r => r.Id == id), ct)).FirstOrDefault();
}
