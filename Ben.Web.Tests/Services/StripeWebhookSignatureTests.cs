using Ben.Data.WebApi.Services.Billing.StripeIntegration;
using Microsoft.Extensions.Options;
using System.Security.Cryptography;
using System.Text;
using Xunit;

namespace Ben.Web.Tests.Services;

/// <summary>
/// The webhook's signature check, exercised through the REAL Stripe parser.
/// </summary>
/// <remarks>
/// <para>The signature is the entire authentication of the one anonymous route that can move a
/// subscription, so these tests do not stub it: they sign payloads with the same HMAC-SHA256
/// scheme Stripe uses (v1 over "timestamp.payload") and hand them to the very
/// <c>EventUtility.ConstructEvent</c> call production runs. A mock here would test the mock.</para>
///
/// <para>The gateway is constructed with a webhook secret but NO API key — which is also a real
/// configuration (verification requires no Stripe round-trip), and what keeps these tests off
/// the network.</para>
/// </remarks>
public sealed class StripeWebhookSignatureTests
{
    private const string Secret = "whsec_test_1234567890abcdef";

    private static StripeGateway Gateway() => new(Options.Create(new StripeOptions
    {
        WebhookSecret = Secret,   // deliberately no SecretKey — see the remarks
    }));

    /// <summary>Signs a payload exactly as Stripe does: v1 = HMACSHA256(secret, "t.payload").</summary>
    private static string Sign(string payload, long? timestamp = null, string secret = Secret)
    {
        var t = timestamp ?? DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
        var v1 = Convert.ToHexString(hmac.ComputeHash(Encoding.UTF8.GetBytes($"{t}.{payload}")))
            .ToLowerInvariant();
        return $"t={t},v1={v1}";
    }

    /// <summary>A checkout.session.completed event, shaped as Stripe ships it.</summary>
    private static string CompletedSessionPayload(string metadataJson =
        """{"ih_org":"11111111-1111-1111-1111-111111111111"}""") =>
        $$"""
        {
          "id": "evt_test_1",
          "object": "event",
          "api_version": "2026-01-01",
          "type": "checkout.session.completed",
          "data": {
            "object": {
              "id": "cs_test_signed",
              "object": "checkout.session",
              "payment_intent": "pi_test_signed",
              "customer": "cus_test_signed",
              "metadata": {{metadataJson}}
            }
          }
        }
        """;

    [Fact]
    public void A_genuinely_signed_completed_checkout_parses_with_its_metadata()
    {
        var payload = CompletedSessionPayload();

        var checkout = Gateway().ParseCompletedCheckout(payload, Sign(payload));

        Assert.NotNull(checkout);
        Assert.Equal("cs_test_signed", checkout.SessionId);
        Assert.Equal("pi_test_signed", checkout.PaymentIntentRef);
        Assert.Equal("cus_test_signed", checkout.CustomerRef);
        Assert.Equal("11111111-1111-1111-1111-111111111111", checkout.Metadata["ih_org"]);
    }

    [Fact]
    public void A_tampered_payload_throws_and_nothing_downstream_runs()
    {
        var payload = CompletedSessionPayload();
        var signature = Sign(payload);
        var tampered = payload.Replace("pi_test_signed", "pi_attacker");

        Assert.ThrowsAny<Stripe.StripeException>(
            () => Gateway().ParseCompletedCheckout(tampered, signature));
    }

    [Fact]
    public void A_signature_from_the_wrong_secret_is_refused()
    {
        // The exact shape of a forged delivery: valid JSON, valid HMAC scheme, wrong key.
        var payload = CompletedSessionPayload();
        Assert.ThrowsAny<Stripe.StripeException>(
            () => Gateway().ParseCompletedCheckout(
                payload, Sign(payload, secret: "whsec_attacker_guess")));
    }

    [Fact]
    public void A_stale_signature_is_refused()
    {
        // Stripe's default tolerance is five minutes; a replayed capture from an hour ago must
        // not fulfill anything, however genuine its signature once was.
        var payload = CompletedSessionPayload();
        var stale = Sign(payload, DateTimeOffset.UtcNow.AddHours(-1).ToUnixTimeSeconds());

        Assert.ThrowsAny<Stripe.StripeException>(
            () => Gateway().ParseCompletedCheckout(payload, stale));
    }

    [Fact]
    public void A_signed_renewal_payment_parses_to_the_same_shape_as_a_checkout()
    {
        // The renewal job's charge has no checkout session — payment_intent.succeeded IS its
        // whole announcement, and it reduces to the same StripeCompletedCheckout so fulfillment
        // has exactly one door.
        var payload = """
        {
          "id": "evt_test_2",
          "object": "event",
          "api_version": "2026-01-01",
          "type": "payment_intent.succeeded",
          "data": {
            "object": {
              "id": "pi_renewal_1",
              "object": "payment_intent",
              "customer": "cus_test_signed",
              "payment_method": "pm_test_signed",
              "metadata": {"ih_org": "11111111-1111-1111-1111-111111111111"}
            }
          }
        }
        """;

        var checkout = Gateway().ParseCompletedCheckout(payload, Sign(payload));

        Assert.NotNull(checkout);
        Assert.Equal("pi_renewal_1", checkout.SessionId);
        Assert.Equal("pi_renewal_1", checkout.PaymentIntentRef);
        Assert.Equal("pm_test_signed", checkout.PaymentMethodRef);
        Assert.Equal("11111111-1111-1111-1111-111111111111", checkout.Metadata["ih_org"]);
    }

    [Fact]
    public void An_event_type_fulfillment_does_not_care_about_is_null_not_an_error()
    {
        var payload = CompletedSessionPayload().Replace(
            "checkout.session.completed", "customer.created");

        Assert.Null(Gateway().ParseCompletedCheckout(payload, Sign(payload)));
    }
}
