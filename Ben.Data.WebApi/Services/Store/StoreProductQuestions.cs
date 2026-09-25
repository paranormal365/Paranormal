using Ben.Data.Common.Enums;
using Ben.Data.Source.Context;
using Ben.Data.Source.Entities;
using Ben.Service.Models.Store;
using Microsoft.EntityFrameworkCore;

namespace Ben.Data.WebApi.Services.Store;

/// <summary>
/// Shoppers' questions about a product (store sellers, backlog 251, P12): asking, answering or
/// declining, and promoting an answer into the FAQ. The rules live here so the seller's inbox and the
/// store's say the same thing.
/// </summary>
/// <remarks>
/// <para><b>Who answers.</b> A seller's item: its seller, and the store (a SuperAdmin may answer any).
/// The store's own stock: the store. The question is sent to whoever answers, by the store — the bell
/// never names the asker.</para>
///
/// <para><b>Private until promoted.</b> The answer goes to the asker alone; promoting copies the
/// words into a new FAQ entry, reworded if need be, and names nobody.</para>
/// </remarks>
public static class StoreProductQuestions
{
    /// <summary>Open questions one person may have waiting on one product.</summary>
    public const int MaxOpenPerProduct = 3;

    /// <summary>Questions one person may ask in a day, across the store.</summary>
    public const int MaxPerDay = 10;

    public static async Task<(StoreAskedQuestionRecord? Asked, StoreEditRefusal? Refusal)> AskAsync(
        BenDataContext db, Guid productId, Guid askerId, string? text, DateTime now, CancellationToken ct)
    {
        var question = text?.Trim() ?? "";
        if (question.Length < StoreProductQuestion.MinQuestionLength) return (null, StoreEditRefusal.BadRequest("Write your question — a few words at least."));
        if (question.Length > StoreProductQuestion.MaxQuestionLength)
            return (null, StoreEditRefusal.BadRequest($"A question is {StoreProductQuestion.MaxQuestionLength} characters at most."));

        var product = await StoreCatalogue.LiveProducts(db).Where(p => p.Id == productId)
            .Select(p => new { p.Id, p.Name, p.Slug, p.SellerAppUserId }).FirstOrDefaultAsync(ct);
        if (product is null) return (null, StoreEditRefusal.NotFound);
        if (product.SellerAppUserId == askerId) return (null, StoreEditRefusal.Conflict("This is your own item — add what shoppers should know to its FAQ."));

        if (await db.StoreProductQuestions.CountAsync(q => q.AskerAppUserId == askerId && q.ProductId == productId && q.Status == StoreQuestionStatus.Open, ct)
            >= MaxOpenPerProduct)
            return (null, StoreEditRefusal.Conflict($"You have {MaxOpenPerProduct} questions about this item waiting already — you'll hear as soon as they're answered."));
        var dayAgo = now.AddDays(-1);
        if (await db.StoreProductQuestions.CountAsync(q => q.AskerAppUserId == askerId && q.DateCreated > dayAgo, ct) >= MaxPerDay)
            return (null, StoreEditRefusal.Conflict($"That's {MaxPerDay} questions today — please ask again tomorrow."));

        var row = new StoreProductQuestion
        {
            Id = Guid.NewGuid(), ProductId = productId, AskerAppUserId = askerId, Question = question,
            Status = StoreQuestionStatus.Open, DateCreated = now,
        };
        db.StoreProductQuestions.Add(row);
        await db.SaveChangesAsync(ct);
        return (new StoreAskedQuestionRecord(row.Id, productId, product.Name, product.Slug, question, null, row.Status, now, null), null);
    }

    /// <summary>The asker's own questions, newest first.</summary>
    public static Task<List<StoreAskedQuestionRecord>> AskedAsync(BenDataContext db, Guid askerId, CancellationToken ct)
        => db.StoreProductQuestions.AsNoTracking().Where(q => q.AskerAppUserId == askerId)
            .OrderByDescending(q => q.DateCreated)
            .Select(q => new StoreAskedQuestionRecord(q.Id, q.ProductId, q.Product.Name, q.Product.Slug, q.Question, q.Answer,
                q.Status, q.DateCreated, q.AnsweredUtc))
            .ToListAsync(ct);

    /// <summary>
    /// The questions <paramref name="query"/> selects, open first then newest — shaped with nowhere to
    /// put who asked.
    /// </summary>
    public static Task<List<StoreReceivedQuestionRecord>> ReceivedAsync(BenDataContext db, IQueryable<StoreProductQuestion> query, CancellationToken ct)
        => query.AsNoTracking()
            .OrderBy(q => q.Status == StoreQuestionStatus.Open ? 0 : 1).ThenByDescending(q => q.DateCreated)
            .Select(q => new StoreReceivedQuestionRecord(q.Id, q.ProductId, q.Product.Name, q.Question, q.Answer, q.Status,
                q.DateCreated, q.AnsweredUtc, q.PromotedFaqId != null, q.Product.SellerAppUserId == null,
                q.Product.SellerAppUser == null ? null : q.Product.SellerAppUser.DisplayName))
            .Take(500)
            .ToListAsync(ct);

    /// <summary>
    /// Answers or declines an open question. <paramref name="question"/> is already known to be one the
    /// caller may answer; answering twice would send the asker a second, different answer.
    /// </summary>
    public static async Task<StoreEditRefusal?> AnswerAsync(BenDataContext db, StoreProductQuestion question, AnswerStoreQuestionRequest request,
        Guid answererId, DateTime now, CancellationToken ct)
    {
        var answer = request.Answer?.Trim();
        if (!request.Decline && string.IsNullOrEmpty(answer)) return StoreEditRefusal.BadRequest("Write an answer, or decline the question.");
        if (answer?.Length > StoreProductQuestion.MaxAnswerLength)
            return StoreEditRefusal.BadRequest($"An answer is {StoreProductQuestion.MaxAnswerLength} characters at most.");

        var claimed = await db.StoreProductQuestions
            .Where(q => q.Id == question.Id && q.Status == StoreQuestionStatus.Open)
            .ExecuteUpdateAsync(u => u
                .SetProperty(q => q.Status, request.Decline ? StoreQuestionStatus.Declined : StoreQuestionStatus.Answered)
                .SetProperty(q => q.Answer, string.IsNullOrEmpty(answer) ? null : answer)
                .SetProperty(q => q.AnsweredByAppUserId, answererId)
                .SetProperty(q => q.AnsweredUtc, now), ct);
        return claimed == 1 ? null : StoreEditRefusal.Conflict("That question has already been answered.");
    }

    /// <summary>Copies an answered question into the product's FAQ — once — and records it in the item's history.</summary>
    public static async Task<StoreEditRefusal?> PromoteAsync(BenDataContext db, StoreProductQuestion question, PromoteStoreQuestionRequest request,
        StoreEditActor actor, DateTime now, CancellationToken ct)
    {
        if (question.Status != StoreQuestionStatus.Answered) return StoreEditRefusal.Conflict("Only an answered question can go in the FAQ.");
        if (question.PromotedFaqId is not null) return StoreEditRefusal.Conflict("That question is in the FAQ already.");
        var (q, a) = (request.Question?.Trim() ?? "", request.Answer?.Trim() ?? "");
        if (q.Length == 0 || a.Length == 0) return StoreEditRefusal.BadRequest("An FAQ entry needs both a question and an answer.");
        if (q.Length > StoreProductFaq.MaxQuestionLength) return StoreEditRefusal.BadRequest($"An FAQ question is {StoreProductFaq.MaxQuestionLength} characters at most.");
        if (a.Length > StoreProductFaq.MaxAnswerLength) return StoreEditRefusal.BadRequest($"An FAQ answer is {StoreProductFaq.MaxAnswerLength} characters at most.");
        if (await db.StoreProductFaqs.CountAsync(f => f.ProductId == question.ProductId, ct) >= StoreProductEditor.MaxFaqs)
            return StoreEditRefusal.Conflict($"The FAQ holds {StoreProductEditor.MaxFaqs} entries at most — remove one first.");

        var faq = new StoreProductFaq
        {
            Id = Guid.NewGuid(), ProductId = question.ProductId, Question = q, Answer = a, DateCreated = now, CreatedByAppUserId = actor.UserId,
            SortOrder = (await db.StoreProductFaqs.Where(f => f.ProductId == question.ProductId).MaxAsync(f => (int?)f.SortOrder, ct) ?? -1) + 1,
        };
        db.StoreProductFaqs.Add(faq);
        question.PromotedFaqId = faq.Id;
        StoreProductHistory.Record(db, question.ProductId, StoreProductChangeArea.Faq, $"Added a shopper's question to the FAQ: “{q}”", actor.UserId, actor.Role, now);
        await db.SaveChangesAsync(ct);
        return null;
    }
}
