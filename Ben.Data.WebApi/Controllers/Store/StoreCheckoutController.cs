using Ben.Data.Common.Enums;
using Ben.Data.Source.Context;
using Ben.Data.WebApi.Services;
using Ben.Data.WebApi.Services.Billing.StripeIntegration;
using Ben.Data.WebApi.Services.Store;
using Ben.Service.Models.Store;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;

namespace Ben.Data.WebApi.Controllers.Store;

/// <summary>
/// The checkout (storefront S4.9): the cart becomes an order and a payment to confirm.
/// </summary>
/// <remarks>
/// <para><b>Behind the store switch; its three POSTs carry the checkout limit</b> (10 a minute,
/// keyed by account or visitor address) — the class does not, so nothing a page merely reads is
/// counted against it.</para>
///
/// <para><b>The two <c>dev/</c> routes exist only in a test checkout</b> — they answer 404 unless the
/// pretend Stripe gateway is registered, which only happens in Development with no key and the flag
/// on. They finish a payment, and run a reservation out, the way Stripe and the clock would.</para>
/// </remarks>
[ApiController]
[Route("api/store/checkout")]
[AllowAnonymous]
[FeatureGated(SiteSettingKeys.FeatureStore)]
public sealed class StoreCheckoutController(
    StoreCheckoutService checkout, StoreOrderPayments payments, IStoreStripeGateway gateway,
    IDbContextFactory<BenDataContext> dbFactory) : BenControllerBase
{
    [HttpPost("payment-intent")]
    [EnableRateLimiting(RateLimiting.StoreCheckoutPolicy)]
    public async Task<ActionResult<StoreCheckoutPrepared>> Prepare([FromBody] StoreCheckoutRequest request, CancellationToken ct)
    {
        var caller = StoreCartCaller.From(await GetCurrentUserIdOrNullAcrossSchemesAsync(), Request.Headers[StoreCartController.CartHeader].ToString());
        var result = await checkout.PrepareAsync(caller, HttpContext.Connection.RemoteIpAddress?.ToString(), request, ct);
        return result.Outcome switch
        {
            StoreCheckoutOutcome.Ok => Ok(result.Prepared),
            StoreCheckoutOutcome.Conflict => Conflict(new StoreCheckoutRefusal(result.Sentence!, result.StillProcessingOrderId)),
            StoreCheckoutOutcome.Unavailable => StatusCode(StatusCodes.Status503ServiceUnavailable, result.Sentence),
            _ => BadRequest(result.Sentence),
        };
    }

    /// <summary>Test checkout only: pays an order as <c>payment_intent.succeeded</c> would.</summary>
    [HttpPost("dev/simulate-payment/{id:guid}")]
    [EnableRateLimiting(RateLimiting.StoreCheckoutPolicy)]
    public async Task<IActionResult> SimulatePayment(Guid id, CancellationToken ct)
    {
        if (gateway is not FakeStoreStripeGateway) return NotFound();
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var order = await db.StoreOrders.AsNoTracking().FirstOrDefaultAsync(o => o.Id == id, ct);
        if (order is null) return NotFound();
        await payments.MarkPaidAsync(order.Id, order.StripePaymentIntentId, $"ch_fake_{order.Id:N}", $"fake-{order.Id:N}",
            StoreMoney.Cents(order.Total), order.StripeTaxCalculationId, ct);
        return NoContent();
    }

    /// <summary>Test checkout only: runs an unpaid order's reservation out, as the clock would.</summary>
    [HttpPost("dev/expire-reservation/{id:guid}")]
    [EnableRateLimiting(RateLimiting.StoreCheckoutPolicy)]
    public async Task<IActionResult> ExpireReservation(Guid id, CancellationToken ct)
    {
        if (gateway is not FakeStoreStripeGateway) return NotFound();
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var past = DateTime.UtcNow.AddMinutes(-1);
        var rows = await db.StoreOrders.Where(o => o.Id == id && o.Status == StoreOrderStatus.PendingPayment)
            .ExecuteUpdateAsync(s => s.SetProperty(o => o.ReservationExpiresUtc, past), ct);
        return rows == 1 ? NoContent() : NotFound();
    }
}
