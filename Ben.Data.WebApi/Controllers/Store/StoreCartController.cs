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
/// The shopper's cart (storefront S3.3), for a signed-in member or a visitor alike.
/// </summary>
/// <remarks>
/// <para><b>Who the cart belongs to.</b> The account when there is one — asked across both sign-in
/// schemes, because on an anonymous endpoint a Microsoft sign-in is otherwise invisible — and the
/// browser's <c>X-Ben-Cart</c> token, which the website copies from its HttpOnly <c>ben.cart</c>
/// cookie. A token that is not exactly 43 URL-safe characters is ignored. Both together fold the
/// browser's cart into the account's (<see cref="StoreCartService.ResolveAsync"/>).</para>
///
/// <para><b>Behind the store switch.</b> Every write carries the cart limit, keyed by account or
/// visitor address — never by the token, which the caller chooses.</para>
///
/// <para><b>A 409 carries the cart.</b> Asking for more than is left adds what is left and answers
/// Conflict with the whole cart and its <see cref="StoreCartView.Notice"/>, so every surface can
/// redraw and say "Only 3 left." in one step.</para>
/// </remarks>
[ApiController]
[Route("api/store/cart")]
[AllowAnonymous]
[FeatureGated(SiteSettingKeys.FeatureStore)]
public sealed class StoreCartController(IDbContextFactory<BenDataContext> dbFactory) : BenControllerBase
{
    public const string CartHeader = "X-Ben-Cart";

    [HttpGet]
    [EnableRateLimiting(RateLimiting.StoreBrowsePolicy)]
    public async Task<ActionResult<StoreCartView>> Get(CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        Response.Headers.CacheControl = "no-store";
        return Ok(await new StoreCartService(db).ViewAsync(await CallerAsync(), ct));
    }

    [HttpGet("count")]
    [EnableRateLimiting(RateLimiting.StoreBrowsePolicy)]
    public async Task<ActionResult<StoreCartCount>> Count(CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        Response.Headers.CacheControl = "no-store";
        return Ok(new StoreCartCount(await new StoreCartService(db).CountAsync(await CallerAsync(), ct)));
    }

    [HttpPost("items")]
    [EnableRateLimiting(RateLimiting.StoreCartPolicy)]
    public async Task<ActionResult<StoreCartView>> Add([FromBody] AddToCartRequest request, CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        return Answer(await new StoreCartService(db).AddAsync(await CallerAsync(), request.VariantId, request.Quantity, ct));
    }

    [HttpPut("items/{variantId:guid}")]
    [EnableRateLimiting(RateLimiting.StoreCartPolicy)]
    public async Task<ActionResult<StoreCartView>> SetQuantity(Guid variantId, [FromBody] SetCartQuantityRequest request, CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        return Answer(await new StoreCartService(db).SetQuantityAsync(await CallerAsync(), variantId, request.Quantity, ct));
    }

    [HttpDelete("items/{variantId:guid}")]
    [EnableRateLimiting(RateLimiting.StoreCartPolicy)]
    public async Task<ActionResult<StoreCartView>> Remove(Guid variantId, CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        return Answer(await new StoreCartService(db).RemoveAsync(await CallerAsync(), variantId, ct));
    }

    [HttpPost("coupon")]
    [EnableRateLimiting(RateLimiting.StoreCartPolicy)]
    public async Task<ActionResult<StoreCartView>> ApplyCoupon([FromBody] ApplyCartCouponRequest request, CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        return Answer(await new StoreCartService(db).ApplyCouponAsync(await CallerAsync(), request.Code, ct));
    }

    [HttpDelete("coupon")]
    [EnableRateLimiting(RateLimiting.StoreCartPolicy)]
    public async Task<ActionResult<StoreCartView>> RemoveCoupon(CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        return Answer(await new StoreCartService(db).ClearCouponAsync(await CallerAsync(), ct));
    }

    private async Task<StoreCartCaller> CallerAsync()
        => StoreCartCaller.From(await GetCurrentUserIdOrNullAcrossSchemesAsync(), Request.Headers[CartHeader].ToString());

    private ActionResult<StoreCartView> Answer(StoreCartWrite write) => write.Outcome switch
    {
        StoreCartOutcome.Ok => Ok(write.View),
        StoreCartOutcome.Conflict => Conflict(write.View),
        StoreCartOutcome.NotFound => NotFound(write.Sentence),
        _ => BadRequest(write.Sentence),
    };
}
