using Ben.Data.Source.Context;
using Microsoft.EntityFrameworkCore;

namespace Ben.Data.WebApi.Services.Store;

/// <summary>
/// The bells between sellers and the store (store sellers, backlog 251, P3): the SuperAdmins hear
/// when a seller asks for an item to go on sale or takes one off; the seller hears the answer.
/// </summary>
/// <remarks>
/// <para><b>Never throws.</b> Each bell rings after the change it is about has been saved; one that
/// cannot be rung is logged, and the change stands.</para>
///
/// <para>The sender is a SuperAdmin, never an invented id — the same rule as <see cref="StoreAlerts"/>.
/// A seller's bells never name the admin who decided: the store answers as the store.</para>
/// </remarks>
public sealed class StoreSellerAlerts(
    IDbContextFactory<BenDataContext> dbFactory, PlatformMessageService messages, ILogger<StoreSellerAlerts> log)
{
    public Task SaleRequestedAsync(Guid productId, CancellationToken ct = default)
        => ToAdminsAsync(productId, (item, seller) => (
            $"{seller} asks to put {item} on sale",
            $"{seller} would like {item} to go on sale. Price it and approve it, or decline with a note: /admin/store/sale-requests"), ct);

    public Task TakenOffSaleAsync(Guid productId, CancellationToken ct = default)
        => ToAdminsAsync(productId, (item, seller) => (
            $"{seller} took {item} off sale",
            $"{seller} took {item} off sale. It stays in the catalogue, hidden: /admin/store/products/{productId}/edit"), ct);

    public Task ApprovedAsync(Guid productId, CancellationToken ct = default)
        => ToSellerAsync(productId, item => (
            $"{item} is on sale",
            $"The store approved {item}, and it's on sale now. See it under Selling: /store/selling/items/{productId}"), ct);

    public Task DeclinedAsync(Guid productId, string note, CancellationToken ct = default)
        => ToSellerAsync(productId, item => (
            $"{item} isn't going on sale yet",
            $"The store didn't put {item} on sale: {note} Make the changes and ask again: /store/selling/items/{productId}"), ct);

    private async Task ToAdminsAsync(Guid productId, Func<string, string, (string Subject, string Body)> say, CancellationToken ct)
    {
        try
        {
            await using var db = await dbFactory.CreateDbContextAsync(ct);
            var item = await db.StoreProducts.AsNoTracking().Where(p => p.Id == productId)
                .Select(p => new { p.Name, Seller = p.SellerAppUser == null ? null : p.SellerAppUser.DisplayName }).FirstOrDefaultAsync(ct);
            if (item is null) return;
            var admins = await StoreAlerts.SuperAdminIdsAsync(db, ct);
            if (admins.Count == 0) { log.LogWarning("A seller alert about {Item} had no SuperAdmin to tell.", item.Name); return; }
            var (subject, body) = say(item.Name, item.Seller ?? "A seller");
            await messages.SendAsync(subject, body, admins, admins[0], ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            log.LogError(ex, "The admins' seller alert for store item {ProductId} could not be sent.", productId);
        }
    }

    private async Task ToSellerAsync(Guid productId, Func<string, (string Subject, string Body)> say, CancellationToken ct)
    {
        try
        {
            await using var db = await dbFactory.CreateDbContextAsync(ct);
            var item = await db.StoreProducts.AsNoTracking().Where(p => p.Id == productId)
                .Select(p => new { p.Name, p.SellerAppUserId }).FirstOrDefaultAsync(ct);
            if (item?.SellerAppUserId is not { } seller) return;
            var admins = await StoreAlerts.SuperAdminIdsAsync(db, ct);
            if (admins.Count == 0) return;
            var (subject, body) = say(item.Name);
            await messages.SendAsync(subject, body, [seller], admins[0], ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            log.LogError(ex, "The seller's alert for store item {ProductId} could not be sent.", productId);
        }
    }
}
