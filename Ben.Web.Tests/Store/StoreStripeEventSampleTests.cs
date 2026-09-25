using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Ben.Data.WebApi.Services.Billing.StripeIntegration;
using Microsoft.Extensions.Options;
using Xunit;

namespace Ben.Web.Tests.Store;

/// <summary>
/// The site reads Stripe's real events the way it means to (storefront S4 exit): four captured from
/// the sandbox during the first real test-mode purchase and refund, 09/24/2026, with
/// <c>stripe events retrieve</c> — never typed.
/// </summary>
/// <remarks>
/// <para><b>Why real ones.</b> Stripe sent them in API version <c>2026-08-26.dahlia</c>, newer than
/// the SDK's own; the webhook reads them with the version check off. A hand-written event proves
/// only that the parser reads what its author imagined. These fail the day Stripe's shape and the
/// SDK stop agreeing about a field the store depends on.</para>
///
/// <para>Each is signed here with a secret that exists only in this test, as Stripe signs with the
/// endpoint's. The one edit to the captured bodies: <c>client_secret</c> is blanked (the repository
/// is public). Expected values are read from the sample itself, not restated.</para>
/// </remarks>
public sealed class StoreStripeEventSampleTests
{
    private const string Secret = "whsec_sample_tests_only";

    private static readonly StripeGateway Gateway = new(Options.Create(new StripeOptions { WebhookSecret = Secret }));

    private static string Sample(string type)
        => File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "Stripe", $"{type}.json"));

    private static string Sign(string payload, string secret = Secret)
    {
        var t = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
        return $"t={t},v1={Convert.ToHexString(hmac.ComputeHash(Encoding.UTF8.GetBytes($"{t}.{payload}"))).ToLowerInvariant()}";
    }

    private static JsonElement Object(string payload) => JsonDocument.Parse(payload).RootElement.GetProperty("data").GetProperty("object");

    [Fact]
    public void A_paid_store_payment_reaches_fulfilment_with_its_order_and_amount()
    {
        var payload = Sample("payment_intent.succeeded");
        var pi = Object(payload);

        var paid = Gateway.ParseCompletedCheckout(payload, Sign(payload));
        Assert.NotNull(paid);
        Assert.Equal(pi.GetProperty("id").GetString(), paid!.PaymentIntentRef);
        Assert.Equal(pi.GetProperty("amount_received").GetInt64(), paid.AmountReceivedCents);
        Assert.Equal(pi.GetProperty("metadata").GetProperty("ih_store_order").GetString(), paid.Metadata["ih_store_order"]);

        var mapped = Gateway.ParseEvent(payload, Sign(payload));
        Assert.Equal(("payment_intent.succeeded", pi.GetProperty("id").GetString()), (mapped!.Type, mapped.PaymentIntentId));
        Assert.Equal(pi.GetProperty("amount_received").GetInt64(), mapped.AmountReceivedCents);
    }

    [Theory]
    [InlineData("refund.created")]
    [InlineData("refund.updated")]
    public void A_refund_event_names_the_refund_its_payment_and_our_refund_row(string type)
    {
        var payload = Sample(type);
        var refund = Object(payload);

        var mapped = Gateway.ParseEvent(payload, Sign(payload));
        Assert.NotNull(mapped);
        Assert.Equal(type, mapped!.Type);
        Assert.Equal(refund.GetProperty("id").GetString(), mapped.RefundId);
        Assert.Equal(refund.GetProperty("payment_intent").GetString(), mapped.PaymentIntentId);
        Assert.Equal(refund.GetProperty("status").GetString(), mapped.RefundStatus);
        Assert.Equal(refund.GetProperty("metadata").GetProperty("ih_store_refund").GetString(), mapped.RefundMetadata["ih_store_refund"]);
    }

    [Fact]
    public void A_cancelled_payment_is_read_as_one()
    {
        var payload = Sample("payment_intent.canceled");
        var mapped = Gateway.ParseEvent(payload, Sign(payload));
        Assert.Equal(("payment_intent.canceled", Object(payload).GetProperty("id").GetString()), (mapped!.Type, mapped.PaymentIntentId));
    }

    [Fact]
    public void A_real_body_signed_with_another_secret_is_refused()
    {
        var payload = Sample("payment_intent.succeeded");
        Assert.ThrowsAny<Exception>(() => Gateway.ParseCompletedCheckout(payload, Sign(payload, "whsec_somebody_else")));
        Assert.ThrowsAny<Exception>(() => Gateway.ParseEvent(payload.Replace("\"succeeded\"", "\"canceled\""), Sign(payload)));
    }
}
