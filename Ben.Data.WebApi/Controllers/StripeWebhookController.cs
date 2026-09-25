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
    private readonly IStoreStripeGateway? _store;
    private readonly Ben.Data.WebApi.Services.Store.StoreOrderPayments? _storePayments;
    private readonly Ben.Data.WebApi.Services.Store.StoreRefundService? _storeRefunds;

    public StripeWebhookController(
        IStripeGateway stripe, StripeFulfillmentService fulfillment, ILogger<StripeWebhookController> log,
        IStoreStripeGateway? store = null, Ben.Data.WebApi.Services.Store.StoreOrderPayments? storePayments = null,
        Ben.Data.WebApi.Services.Store.StoreRefundService? storeRefunds = null)
    {
        _stripe = stripe;
        _fulfillment = fulfillment;
        _log = log;
        _store = store;
        _storePayments = storePayments;
        _storeRefunds = storeRefunds;
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

        // The store's other payment events (storefront S4.9): a payment going through slowly, a
        // declined card, a cancelled intent. A paid one arrived above, through fulfilment. The
        // signature was verified above; this parse maps the same verified body.
        if (_store?.ParseEvent(payload, Request.Headers["Stripe-Signature"].ToString()) is { } storeEvent)
        {
            var pi = storeEvent.PaymentIntentId;
            switch (storeEvent.Type)
            {
                case "payment_intent.processing" when pi is not null && _storePayments is not null:
                    await _storePayments.RecordProcessingAsync(pi, ct); break;
                case "payment_intent.payment_failed" when pi is not null && _storePayments is not null:
                    await _storePayments.RecordFailureAsync(pi, storeEvent.FailureCode, ct); break;
                case "payment_intent.canceled" when pi is not null && _storePayments is not null:
                    await _storePayments.RecordCancelledAtStripeAsync(pi, ct); break;
                // Refunds (S5.3): ours finish or fail by the id in their metadata; one made in Stripe's
                // dashboard is recorded on the order its payment belongs to.
                case "refund.created" or "refund.updated" or "refund.failed" when _storeRefunds is not null:
                    await _storeRefunds.RecordDashboardRefundAsync(storeEvent, ct); break;
            }
        }

        return Ok();
    }
}
