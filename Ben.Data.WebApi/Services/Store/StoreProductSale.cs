using Ben.Data.Common.Enums;
using Ben.Data.Source.Context;
using Ben.Data.Source.Entities;
using Ben.Service.Models.Store;
using Microsoft.EntityFrameworkCore;

namespace Ben.Data.WebApi.Services.Store;

/// <summary>
/// Going on sale (store sellers, backlog 251, P3): what an item needs before it can, and a seller's
/// request for the store to put it there — asked, withdrawn, approved or declined.
/// </summary>
/// <remarks>
/// <para><b>The same checks everywhere.</b> The admin's Put on sale, the sale-request queue's
/// readiness list and approval all read <see cref="ProblemsAsync"/>, so the queue never shows an
/// item as ready that approval would then refuse.</para>
///
/// <para><b>A decision is claimed, not assumed.</b> Approve and decline move the request out of
/// Open with a conditional update; the second of two admins deciding at once is told it has been
/// decided, rather than both "winning".</para>
/// </remarks>
public static class StoreProductSale
{
    public const string AlreadyAsked = "There's already a request waiting for this item.";
    public const string AlreadyDecided = "That request has already been decided.";

    /// <summary>Everything standing between the item and going on sale, in the order to fix them. Empty when ready.</summary>
    public static async Task<List<string>> ProblemsAsync(BenDataContext db, Guid productId, CancellationToken ct)
    {
        var product = await db.StoreProducts.AsNoTracking()
            .Include(p => p.Category).ThenInclude(c => c.ParentCategory).Include(p => p.Variants)
            .FirstOrDefaultAsync(p => p.Id == productId, ct);
        if (product is null) return ["That item no longer exists."];

        var problems = new List<string>();
        var live = product.Variants.Where(v => v.IsActive).OrderBy(v => v.SortOrder).ToList();
        if (live.FirstOrDefault(v => v.Price <= 0m) is { } unpriced)
            problems.Add(unpriced.IsDefault && product.Variants.Count == 1
                ? "Price the default variant first — it's still $0.00."
                : $"Price {StorePriceCaches.Label(unpriced.Name)} first — it's still $0.00.");
        if (!await db.StoreProductImages.AnyAsync(i => i.ProductId == productId, ct))
            problems.Add("Add at least one picture before showing it.");
        if (live.Count == 0) problems.Add("At least one variant must be active.");
        if (!product.Category.IsActive) problems.Add("Its category is hidden — show the category first.");
        else if (product.Category.ParentCategory is { IsActive: false } parent)
            problems.Add($"Its category sits under {parent.Name}, which is hidden — show {parent.Name} first.");
        return problems;
    }

    public static async Task<string?> FirstProblemAsync(BenDataContext db, Guid productId, CancellationToken ct)
        => (await ProblemsAsync(db, productId, ct)).FirstOrDefault();

    // ── the seller's side ────────────────────────────────────────────────────

    /// <summary>A seller asks for their hidden item to go on sale, at <paramref name="askingPrice"/> a unit to them.</summary>
    public static async Task<(StoreProductSaleRequest? Request, StoreEditRefusal? Refusal)> RequestAsync(
        BenDataContext db, StoreProduct product, decimal askingPrice, string? note, StoreEditActor seller, CancellationToken ct)
    {
        if (product.IsActive) return (null, StoreEditRefusal.BadRequest("It's already on sale."));
        if (askingPrice <= 0m) return (null, StoreEditRefusal.BadRequest("Say what you'd like to be paid for each one — more than $0.00."));
        if (askingPrice != StoreMoney.Round(askingPrice)) return (null, StoreEditRefusal.BadRequest("Prices are dollars and cents — two decimal places at most."));
        if (askingPrice > 100_000m) return (null, StoreEditRefusal.BadRequest("That asking price is more than the store can take."));
        var text = string.IsNullOrWhiteSpace(note) ? null : note.Trim();
        if (text?.Length > StoreProductSaleRequest.MaxNoteLength)
            return (null, StoreEditRefusal.BadRequest($"A note is {StoreProductSaleRequest.MaxNoteLength:N0} characters at most."));
        if (await db.StoreProductSaleRequests.AnyAsync(r => r.ProductId == product.Id && r.Status == StoreSaleRequestStatus.Open, ct))
            return (null, StoreEditRefusal.Conflict(AlreadyAsked));

        var now = DateTime.UtcNow;
        var request = new StoreProductSaleRequest
        {
            Id = Guid.NewGuid(), ProductId = product.Id, SellerAppUserId = seller.UserId, SellerAskingPrice = askingPrice,
            SellerNote = text, Status = StoreSaleRequestStatus.Open, RequestedUtc = now,
        };
        db.StoreProductSaleRequests.Add(request);
        StoreProductHistory.Record(db, product.Id, StoreProductChangeArea.Sale,
            $"Asked the store to put it on sale, for {StoreMoney.Format(askingPrice)} a unit to the seller.", seller.UserId, seller.Role, now);
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException)
        {
            // Two clicks at once: the index kept the second out.
            return (null, StoreEditRefusal.Conflict(AlreadyAsked));
        }
        return (request, null);
    }

    /// <summary>The seller takes back their open request.</summary>
    public static async Task<StoreEditRefusal?> WithdrawAsync(BenDataContext db, Guid productId, StoreEditActor seller, CancellationToken ct)
    {
        var now = DateTime.UtcNow;
        var claimed = await db.StoreProductSaleRequests
            .Where(r => r.ProductId == productId && r.Status == StoreSaleRequestStatus.Open)
            .ExecuteUpdateAsync(s => s.SetProperty(r => r.Status, StoreSaleRequestStatus.Withdrawn)
                                      .SetProperty(r => r.DecidedUtc, now)
                                      .SetProperty(r => r.DecidedByAppUserId, seller.UserId), ct);
        if (claimed == 0) return StoreEditRefusal.BadRequest("There's no request waiting to take back.");

        StoreProductHistory.Record(db, productId, StoreProductChangeArea.Sale, "Took back the request to put it on sale.", seller.UserId, seller.Role, now);
        await db.SaveChangesAsync(ct);
        return null;
    }

    // ── the store's side ─────────────────────────────────────────────────────

    /// <summary>
    /// The store approves: the item goes on sale at the prices it has set, and the seller's asking
    /// price becomes what they earn a unit on top of cost. Refused, and left open, when the item is
    /// not ready — the sentence says what to fix.
    /// </summary>
    public static async Task<(StoreProductSaleRequest? Request, StoreEditRefusal? Refusal)> ApproveAsync(
        BenDataContext db, StoreProductEditor editor, Guid requestId, StoreEditActor admin, CancellationToken ct)
    {
        var request = await db.StoreProductSaleRequests.AsNoTracking().FirstOrDefaultAsync(r => r.Id == requestId, ct);
        if (request is null) return (null, StoreEditRefusal.NotFound);
        if (request.Status != StoreSaleRequestStatus.Open) return (null, StoreEditRefusal.Conflict(AlreadyDecided));
        if (await FirstProblemAsync(db, request.ProductId, ct) is { } problem) return (null, StoreEditRefusal.BadRequest(problem));

        var relational = db.Database.IsRelational();
        await using var tx = relational ? await db.Database.BeginTransactionAsync(ct) : null;

        var now = DateTime.UtcNow;
        var claimed = await db.StoreProductSaleRequests
            .Where(r => r.Id == requestId && r.Status == StoreSaleRequestStatus.Open)
            .ExecuteUpdateAsync(s => s.SetProperty(r => r.Status, StoreSaleRequestStatus.Approved)
                                      .SetProperty(r => r.DecidedUtc, now)
                                      .SetProperty(r => r.DecidedByAppUserId, admin.UserId), ct);
        if (claimed == 0) return (null, StoreEditRefusal.Conflict(AlreadyDecided));

        var product = await db.StoreProducts.FirstAsync(p => p.Id == request.ProductId, ct);
        product.SellerAskPerUnit = request.SellerAskingPrice;
        StoreProductHistory.Record(db, product.Id, StoreProductChangeArea.Sale,
            $"Approved the request to put it on sale, at {StoreMoney.Format(request.SellerAskingPrice)} a unit to the seller.",
            admin.UserId, admin.Role, now);
        if (await editor.SwitchAsync(db, product, on: true, admin, ct) is { } refused) return (null, refused);
        // SwitchAsync saves only when the item was off; an item somebody put on sale meanwhile still takes the ask.
        await db.SaveChangesAsync(ct);
        if (tx is not null) await tx.CommitAsync(ct);

        return (request, null);
    }

    /// <summary>The store says no, and says why; the item stays hidden and the seller can ask again.</summary>
    public static async Task<(StoreProductSaleRequest? Request, StoreEditRefusal? Refusal)> DeclineAsync(
        BenDataContext db, Guid requestId, string? note, StoreEditActor admin, CancellationToken ct)
    {
        var text = string.IsNullOrWhiteSpace(note) ? null : note.Trim();
        if (text is null) return (null, StoreEditRefusal.BadRequest("Say why, so the seller knows what to change."));
        if (text.Length > StoreProductSaleRequest.MaxNoteLength)
            return (null, StoreEditRefusal.BadRequest($"A note is {StoreProductSaleRequest.MaxNoteLength:N0} characters at most."));

        var request = await db.StoreProductSaleRequests.AsNoTracking().FirstOrDefaultAsync(r => r.Id == requestId, ct);
        if (request is null) return (null, StoreEditRefusal.NotFound);

        var now = DateTime.UtcNow;
        var claimed = await db.StoreProductSaleRequests
            .Where(r => r.Id == requestId && r.Status == StoreSaleRequestStatus.Open)
            .ExecuteUpdateAsync(s => s.SetProperty(r => r.Status, StoreSaleRequestStatus.Declined)
                                      .SetProperty(r => r.DecidedUtc, now)
                                      .SetProperty(r => r.DecidedByAppUserId, admin.UserId)
                                      .SetProperty(r => r.DecisionNote, text), ct);
        if (claimed == 0) return (null, StoreEditRefusal.Conflict(AlreadyDecided));

        StoreProductHistory.Record(db, request.ProductId, StoreProductChangeArea.Sale, $"Declined the request to put it on sale: {text}",
            admin.UserId, admin.Role, now);
        await db.SaveChangesAsync(ct);
        return (request, null);
    }
}
