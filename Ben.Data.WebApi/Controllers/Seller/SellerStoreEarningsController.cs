using Ben.Data.Common.Constants;
using Ben.Data.Source.Context;
using Ben.Data.WebApi.Services.Store;
using Ben.Service.Models.Store;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Ben.Data.WebApi.Controllers.Seller;

/// <summary>
/// A seller's own earnings (store sellers, backlog 251, P10) — Ben's point 8: what they have earned,
/// by item and by order, what has been paid and what is still owed, and each year's totals for their
/// tax forms. Read-only: the store records payments.
/// </summary>
[ApiController]
[Authorize(Policy = AuthPolicyNames.Seller)]
[Route("api/seller/store/earnings")]
public sealed class SellerStoreEarningsController(IDbContextFactory<BenDataContext> dbFactory, TimeProvider? clock = null) : SellerStoreControllerBase
{
    [HttpGet]
    public async Task<ActionResult<SellerEarningsRecord>> GetMine(CancellationToken ct)
    {
        var me = GetCurrentUserIdOrThrow();
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var settings = await StoreSettingsReader.ReadAsync(db, ct);
        var now = (clock ?? TimeProvider.System).GetUtcNow().UtcDateTime;
        return Ok(await StoreSellerLedger.SummaryAsync(db, me, StoreSellerLedger.DefaultCutoff(now, settings.ReturnsWindowDays), ct));
    }
}
