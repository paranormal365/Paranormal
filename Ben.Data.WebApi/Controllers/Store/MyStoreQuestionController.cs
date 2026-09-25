using Ben.Data.Common.Enums;
using Ben.Data.Source.Context;
using Ben.Data.WebApi.Services;
using Ben.Data.WebApi.Services.Store;
using Ben.Service.Models.Store;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;

namespace Ben.Data.WebApi.Controllers.Store;

/// <summary>
/// A signed-in shopper's questions about the store's items (store sellers, backlog 251, P12): asking
/// one from a product page, and reading the answers.
/// </summary>
/// <remarks>
/// <para><b>Behind the store switch</b>, like the rest of the shelves; asking counts against the cart's
/// limit. A question needs an account so its answer has somewhere to go — and so it can be taken away
/// with the account.</para>
///
/// <para><b>The answer is the asker's alone</b> unless whoever answered puts it in the FAQ.</para>
/// </remarks>
[ApiController]
[Authorize]
[FeatureGated(SiteSettingKeys.FeatureStore)]
[Route("api/me/store/questions")]
public sealed class MyStoreQuestionController(
    IDbContextFactory<BenDataContext> dbFactory, StoreSellerAlerts alerts, TimeProvider? clock = null) : BenControllerBase
{
    private DateTime Now => (clock ?? TimeProvider.System).GetUtcNow().UtcDateTime;

    [HttpGet]
    public async Task<ActionResult<IEnumerable<StoreAskedQuestionRecord>>> Mine(CancellationToken ct)
    {
        var me = GetCurrentUserIdOrThrow();
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        return Ok(await StoreProductQuestions.AskedAsync(db, me, ct));
    }

    [HttpPost("products/{productId:guid}")]
    [EnableRateLimiting(RateLimiting.StoreCartPolicy)]
    public async Task<ActionResult<StoreAskedQuestionRecord>> Ask(Guid productId, [FromBody] AskStoreQuestionRequest request, CancellationToken ct)
    {
        var me = GetCurrentUserIdOrThrow();
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var (asked, refusal) = await StoreProductQuestions.AskAsync(db, productId, me, request.Question, Now, ct);
        if (refusal is not null) return this.Refused(refusal);
        await alerts.QuestionAskedAsync(productId, ct);
        return Ok(asked);
    }
}
