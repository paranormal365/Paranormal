using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using NUnit.Framework;

namespace Ben.Web.Playwright.Tests;

/// <summary>
/// The webhook takes an event signed with the site's secret and refuses anything else (storefront
/// S4 exit, the plan's "self-signed webhook 200 / forged 400").
/// </summary>
/// <remarks>
/// <para><b>Real mode only</b> — <c>BEN_STRIPE_E2E=1</c>, when the API runs with the developer's
/// Stripe test keys and webhook secret. In the ordinary run there is no secret, so a refusal would
/// prove nothing.</para>
///
/// <para>The event is signed here the way Stripe signs one — <c>t=…,v1=HMAC-SHA256(secret,
/// "t.payload")</c> — with the secret read from the API's own gitignored development settings, the
/// same file the API read it from. Its type is a harmless one about a payment the store never made,
/// so the accepted copy changes nothing.</para>
/// </remarks>
[TestFixture]
[Category("Store")]
public class StoreStripeWebhookTests : BenTestBase
{
    private static string? WebhookSecret()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Ben.slnx"))) dir = dir.Parent;
        var settings = Path.Combine(dir!.FullName, "Ben.Data.WebApi", "appsettings.Development.json");
        if (!File.Exists(settings)) return null;
        using var json = JsonDocument.Parse(File.ReadAllText(settings), new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
        return json.RootElement.TryGetProperty("Stripe", out var stripe) && stripe.TryGetProperty("WebhookSecret", out var secret)
            ? secret.GetString() : null;
    }

    private static string Sign(string secret, string payload, long timestamp)
    {
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
        var hash = hmac.ComputeHash(Encoding.UTF8.GetBytes($"{timestamp}.{payload}"));
        return $"t={timestamp},v1={Convert.ToHexString(hash).ToLowerInvariant()}";
    }

    private static string Event(long timestamp) => JsonSerializer.Serialize(new
    {
        id = $"evt_e2e_{Guid.NewGuid():N}",
        @object = "event",
        api_version = "2025-08-27.basil",
        created = timestamp,
        livemode = false,
        pending_webhooks = 0,
        type = "payment_intent.canceled",
        data = new { @object = new { id = $"pi_e2e_{Guid.NewGuid():N}", @object = "payment_intent", status = "canceled", metadata = new { } } },
    });

    private static async Task<HttpStatusCode> PostAsync(string payload, string? signature)
    {
        using var http = new HttpClient { BaseAddress = new Uri(ApiUrl) };
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/stripe/webhook")
        {
            Content = new StringContent(payload, Encoding.UTF8, "application/json"),
        };
        if (signature is not null) request.Headers.TryAddWithoutValidation("Stripe-Signature", signature);
        using var response = await http.SendAsync(request);
        return response.StatusCode;
    }

    [Test]
    [Description("A signed event is taken (200); the same event altered, or unsigned, is refused (400).")]
    public async Task A_signed_event_is_taken_and_a_forged_one_refused()
    {
        if (Environment.GetEnvironmentVariable("BEN_STRIPE_E2E") != "1")
            Assert.Ignore("Real Stripe only: run with BEN_STRIPE_E2E=1 and Stripe test keys.");
        var secret = WebhookSecret();
        if (string.IsNullOrWhiteSpace(secret))
            Assert.Ignore("No Stripe:WebhookSecret in the API's development settings — put `stripe listen --print-secret`'s whsec_ there.");

        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var payload = Event(now);

        Assert.That(await PostAsync(payload, Sign(secret!, payload, now)), Is.EqualTo(HttpStatusCode.OK), "the genuinely signed event");
        Assert.That(await PostAsync(payload.Replace("canceled", "succeeded"), Sign(secret!, payload, now)), Is.EqualTo(HttpStatusCode.BadRequest), "an altered body under the original signature");
        Assert.That(await PostAsync(payload, Sign("whsec_forged", payload, now)), Is.EqualTo(HttpStatusCode.BadRequest), "a signature made with another secret");
        Assert.That(await PostAsync(payload, null), Is.EqualTo(HttpStatusCode.BadRequest), "no signature at all");
    }
}
