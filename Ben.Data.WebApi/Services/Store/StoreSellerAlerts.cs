using Ben.Data.Common.Enums;
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

    /// <summary>
    /// A shopper asked about an item (P12): its seller hears, or — for the store's own stock — the
    /// SuperAdmins. The bell comes from the store and says nothing of who asked.
    /// </summary>
    public async Task QuestionAskedAsync(Guid productId, CancellationToken ct = default)
    {
        try
        {
            await using var db = await dbFactory.CreateDbContextAsync(ct);
            var item = await db.StoreProducts.AsNoTracking().Where(p => p.Id == productId)
                .Select(p => new { p.Name, p.SellerAppUserId }).FirstOrDefaultAsync(ct);
            if (item is null) return;
            var admins = await StoreAlerts.SuperAdminIdsAsync(db, ct);
            if (admins.Count == 0) { log.LogWarning("A question about {Item} had no SuperAdmin to send it.", item.Name); return; }
            if (item.SellerAppUserId is { } seller)
                await messages.SendAsync($"A question about {item.Name}", $"A shopper asked a question about {item.Name}. Answer it — or decline it — under Selling → Questions: /store/selling/questions",
                    [seller], admins[0], ct);
            else
                await messages.SendAsync($"A question about {item.Name}", $"A shopper asked a question about {item.Name}. Answer it — or decline it: /admin/store/questions",
                    admins, admins[0], ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            log.LogError(ex, "The question alert for store item {ProductId} could not be sent.", productId);
        }
    }

    /// <summary>The asker hears their question was answered — or declined (P12). From the store, naming nobody.</summary>
    public async Task QuestionAnsweredAsync(Guid questionId, CancellationToken ct = default)
    {
        try
        {
            await using var db = await dbFactory.CreateDbContextAsync(ct);
            var q = await db.StoreProductQuestions.AsNoTracking().Where(x => x.Id == questionId)
                .Select(x => new { x.AskerAppUserId, x.Status, x.Product.Name }).FirstOrDefaultAsync(ct);
            if (q is null || q.Status == StoreQuestionStatus.Open) return;
            var admins = await StoreAlerts.SuperAdminIdsAsync(db, ct);
            if (admins.Count == 0) return;
            var (subject, body) = q.Status == StoreQuestionStatus.Answered
                ? ($"Your question about {q.Name} was answered", $"Your question about {q.Name} has an answer. Read it under My questions: /store/questions")
                : ($"Your question about {q.Name}", $"Your question about {q.Name} couldn't be answered. See why under My questions: /store/questions");
            await messages.SendAsync(subject, body, [q.AskerAppUserId], admins[0], ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            log.LogError(ex, "The answer alert for store question {QuestionId} could not be sent.", questionId);
        }
    }

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
