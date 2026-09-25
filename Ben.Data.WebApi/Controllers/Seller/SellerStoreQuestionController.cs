using Ben.Data.Common.Constants;
using Ben.Data.Common.Enums;
using Ben.Data.Source.Context;
using Ben.Data.WebApi.Services.Store;
using Ben.Service.Models.Store;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Ben.Data.WebApi.Controllers.Seller;

/// <summary>
/// A seller's questions inbox (store sellers, backlog 251, P12): what shoppers have asked about their
/// own items. Answer, decline, and copy a good answer into the item's FAQ. Nobody who asked is named.
/// </summary>
[ApiController]
[Authorize(Policy = AuthPolicyNames.Seller)]
[Route("api/seller/store/questions")]
public sealed class SellerStoreQuestionController(
    IDbContextFactory<BenDataContext> dbFactory, StoreSellerAlerts alerts, TimeProvider? clock = null) : SellerStoreControllerBase
{
    private DateTime Now => (clock ?? TimeProvider.System).GetUtcNow().UtcDateTime;

    /// <summary>Questions about the caller's items, the waiting ones first. <paramref name="open"/> keeps only those.</summary>
    [HttpGet]
    public async Task<ActionResult<IEnumerable<StoreReceivedQuestionRecord>>> GetAll([FromQuery] bool? open, CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var mine = Mine(db).Select(p => p.Id);
        var query = db.StoreProductQuestions.Where(q => mine.Contains(q.ProductId));
        if (open == true) query = query.Where(q => q.Status == StoreQuestionStatus.Open);
        return Ok(await StoreProductQuestions.ReceivedAsync(db, query, ct));
    }

    [HttpPut("{questionId:guid}/answer")]
    public async Task<ActionResult<StoreReceivedQuestionRecord>> Answer(Guid questionId, [FromBody] AnswerStoreQuestionRequest request, CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var mine = Mine(db).Select(p => p.Id);
        var question = await db.StoreProductQuestions.FirstOrDefaultAsync(q => q.Id == questionId && mine.Contains(q.ProductId), ct);
        if (question is null) return NotFound();
        if (await StoreProductQuestions.AnswerAsync(db, question, request, Seller.UserId, Now, ct) is { } refusal) return this.Refused(refusal);
        await alerts.QuestionAnsweredAsync(questionId, ct);
        return Ok((await StoreProductQuestions.ReceivedAsync(db, db.StoreProductQuestions.Where(q => q.Id == questionId), ct)).Single());
    }

    [HttpPost("{questionId:guid}/promote")]
    public async Task<ActionResult<StoreReceivedQuestionRecord>> Promote(Guid questionId, [FromBody] PromoteStoreQuestionRequest request, CancellationToken ct)
    {
        var me = Seller;
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var mine = Mine(db).Select(p => p.Id);
        var question = await db.StoreProductQuestions.FirstOrDefaultAsync(q => q.Id == questionId && mine.Contains(q.ProductId), ct);
        if (question is null) return NotFound();
        if (await StoreProductQuestions.PromoteAsync(db, question, request, me, Now, ct) is { } refusal) return this.Refused(refusal);
        return Ok((await StoreProductQuestions.ReceivedAsync(db, db.StoreProductQuestions.Where(q => q.Id == questionId), ct)).Single());
    }
}
