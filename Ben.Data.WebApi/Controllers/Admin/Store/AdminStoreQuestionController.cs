using Ben.Data.Common.Constants;
using Ben.Data.Common.Enums;
using Ben.Data.Source.Context;
using Ben.Data.WebApi.Services.Store;
using Ben.Service.Models.Store;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Ben.Data.WebApi.Controllers.Admin.Store;

/// <summary>
/// Shoppers' questions, for the store (store sellers, backlog 251, P12): the ones about its own stock
/// are the store's to answer; a seller's are the seller's, and the store can step in on any.
/// </summary>
[ApiController]
[Authorize(Policy = RoleNames.SuperAdmin)]
[Route("api/admin/store/questions")]
public sealed class AdminStoreQuestionController(
    IDbContextFactory<BenDataContext> dbFactory, StoreSellerAlerts alerts, TimeProvider? clock = null) : BenControllerBase
{
    private DateTime Now => (clock ?? TimeProvider.System).GetUtcNow().UtcDateTime;
    private StoreEditActor Me => new(GetCurrentUserIdOrThrow(), StoreChangeActor.Store);

    /// <summary>
    /// Questions, the waiting ones first. <paramref name="open"/> keeps only those; <paramref name="siteStock"/>
    /// keeps only the store's own stock — the ones nobody else will answer.
    /// </summary>
    [HttpGet]
    public async Task<ActionResult<IEnumerable<StoreReceivedQuestionRecord>>> GetAll([FromQuery] bool? open, [FromQuery] bool? siteStock, CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var query = db.StoreProductQuestions.AsQueryable();
        if (open == true) query = query.Where(q => q.Status == StoreQuestionStatus.Open);
        if (siteStock == true) query = query.Where(q => q.Product.SellerAppUserId == null);
        return Ok(await StoreProductQuestions.ReceivedAsync(db, query, ct));
    }

    [HttpPut("{questionId:guid}/answer")]
    public async Task<ActionResult<StoreReceivedQuestionRecord>> Answer(Guid questionId, [FromBody] AnswerStoreQuestionRequest request, CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var question = await db.StoreProductQuestions.FirstOrDefaultAsync(q => q.Id == questionId, ct);
        if (question is null) return NotFound();
        if (await StoreProductQuestions.AnswerAsync(db, question, request, Me.UserId, Now, ct) is { } refusal) return this.Refused(refusal);
        await alerts.QuestionAnsweredAsync(questionId, ct);
        return Ok((await StoreProductQuestions.ReceivedAsync(db, db.StoreProductQuestions.Where(q => q.Id == questionId), ct)).Single());
    }

    [HttpPost("{questionId:guid}/promote")]
    public async Task<ActionResult<StoreReceivedQuestionRecord>> Promote(Guid questionId, [FromBody] PromoteStoreQuestionRequest request, CancellationToken ct)
    {
        var me = Me;
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var question = await db.StoreProductQuestions.FirstOrDefaultAsync(q => q.Id == questionId, ct);
        if (question is null) return NotFound();
        if (await StoreProductQuestions.PromoteAsync(db, question, request, me, Now, ct) is { } refusal) return this.Refused(refusal);
        return Ok((await StoreProductQuestions.ReceivedAsync(db, db.StoreProductQuestions.Where(q => q.Id == questionId), ct)).Single());
    }
}
