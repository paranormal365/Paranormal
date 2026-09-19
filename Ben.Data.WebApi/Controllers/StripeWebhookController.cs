using Ben.Data.WebApi.Services.Billing.StripeIntegration;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Ben.Data.WebApi.Controllers;

/// <summary>
/// Where Stripe reports back. The only anonymous route that can move a subscription.
/// </summary>
/// <remarks>
/// <para><b>The signature is the authentication.</b> Anyone can POST here; only Stripe holds the
/// signing secret, and <see cref="IStripeGateway.ParseCompletedCheckout"/> throws on anything it
/// did not sign. An unverified body is answered 400 and never parsed further — a webhook that
/// trusts its payload is an open admin endpoint with extra steps.</para>
///
/// <para><b>200 means "stop retrying", nothing more.</b> Stripe redelivers until it hears 200, so
/// events we do not care about are acknowledged, not erred — and fulfillment is idempotent
/// precisely because acknowledgement can be lost after work is done.</para>
/// </remarks>
[ApiController]
[Route("api/stripe/webhook")]
public sealed class StripeWebhookController : ControllerBase
{
    private readonly IStripeGateway _stripe;
    private readonly StripeFulfillmentService _fulfillment;
    private readonly ILogger<StripeWebhookController> _log;

    public StripeWebhookController(
        IStripeGateway stripe, StripeFulfillmentService fulfillment, ILogger<StripeWebhookController> log)
    {
        _stripe = stripe;
        _fulfillment = fulfillment;
        _log = log;
    }

    [HttpPost]
    [AllowAnonymous]
    public async Task<IActionResult> Receive(CancellationToken ct)
    {
        using var reader = new StreamReader(Request.Body);
        var payload = await reader.ReadToEndAsync(ct);

        StripeCompletedCheckout? checkout;
        try
        {
            checkout = _stripe.ParseCompletedCheckout(
                payload, Request.Headers["Stripe-Signature"].ToString());
        }
        catch (Exception ex)
        {
            // Bad signature, torn body, wrong secret — all the same refusal. Logged with the
            // exception type only: the payload is untrusted and does not belong in the log.
            _log.LogWarning("Stripe webhook refused: {Kind}.", ex.GetType().Name);
            return BadRequest();
        }

        if (checkout is not null)
            await _fulfillment.FulfillAsync(checkout, ct);

        return Ok();
    }
}
