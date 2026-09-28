using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Ben.Data.Common.Enums;
using Ben.Data.WebApi.Services.Push;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Ben.Web.Tests.Services;

/// <summary>
/// Pushes to Apple (item 252): the request Apple requires, and what each of Apple's answers means
/// for the phone we sent it to.
/// </summary>
public class ApnsPushSenderTests
{
    private const string Token = "a1b2c3d4e5f60718293a4b5c6d7e8f90a1b2c3d4e5f60718293a4b5c6d7e8f90";

    /// <summary>Answers a scripted response and remembers what was sent.</summary>
    private sealed class Stub : HttpMessageHandler
    {
        public HttpStatusCode Status = HttpStatusCode.OK;
        public string Body = "";
        public bool Throw;
        public readonly List<(HttpRequestMessage Request, string Body)> Sent = new();

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Sent.Add((request, await request.Content!.ReadAsStringAsync(ct)));
            if (Throw) throw new HttpRequestException("network down");
            return new HttpResponseMessage(Status) { Content = new StringContent(Body, Encoding.UTF8, "application/json") };
        }
    }

    private sealed class Clock(DateTimeOffset now) : TimeProvider
    {
        public DateTimeOffset Now = now;
        public override DateTimeOffset GetUtcNow() => Now;
    }

    internal static ApnsOptions Configured()
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        return new ApnsOptions("TEAM123456", "KEY1234567", key.ExportPkcs8PrivateKeyPem(), "com.ishaunted.ios");
    }

    private static (ApnsPushSender Sender, Stub Stub, Clock Clock) Build(ApnsOptions? options = null)
    {
        var stub = new Stub();
        var clock = new Clock(new DateTimeOffset(2026, 9, 28, 20, 0, 0, TimeSpan.Zero));
        return (new ApnsPushSender(new HttpClient(stub), options ?? Configured(), NullLogger<ApnsPushSender>.Instance, clock), stub, clock);
    }

    private static readonly PushMessage Message = new(
        "The walk is starting", "Tap to open Field Kit.",
        new Dictionary<string, string> { ["link"] = "ishaunted://field-kit/launch/1" },
        CollapseId: "launch-1", ExpiresAt: new DateTimeOffset(2026, 9, 29, 4, 0, 0, TimeSpan.Zero));

    private static JsonElement Segment(string jwt, int index)
    {
        var part = jwt.Split('.')[index].Replace('-', '+').Replace('_', '/');
        part = part.PadRight(part.Length + (4 - part.Length % 4) % 4, '=');
        return JsonDocument.Parse(Convert.FromBase64String(part)).RootElement;
    }

    [Fact]
    public async Task APushIsSentToTheProductionServiceAsAppleRequires()
    {
        var (sender, stub, _) = Build();

        var outcome = await sender.SendAsync(Token, PushEnvironment.Production, Message, default);

        Assert.Equal(PushOutcome.Delivered, outcome);
        var (request, body) = Assert.Single(stub.Sent);
        Assert.Equal($"https://api.push.apple.com/3/device/{Token}", request.RequestUri!.ToString());
        Assert.Equal(HttpVersion.Version20, request.Version);
        Assert.Equal("com.ishaunted.ios", request.Headers.GetValues("apns-topic").Single());
        Assert.Equal("alert", request.Headers.GetValues("apns-push-type").Single());
        Assert.Equal("10", request.Headers.GetValues("apns-priority").Single());
        Assert.Equal("launch-1", request.Headers.GetValues("apns-collapse-id").Single());
        Assert.Equal(Message.ExpiresAt!.Value.ToUnixTimeSeconds().ToString(), request.Headers.GetValues("apns-expiration").Single());

        // The provider token: ES256, our key's id, our team.
        Assert.Equal("bearer", request.Headers.Authorization!.Scheme);
        var jwt = request.Headers.Authorization.Parameter!;
        Assert.Equal("ES256", Segment(jwt, 0).GetProperty("alg").GetString());
        Assert.Equal("KEY1234567", Segment(jwt, 0).GetProperty("kid").GetString());
        Assert.Equal("TEAM123456", Segment(jwt, 1).GetProperty("iss").GetString());

        // The alert under aps; the app's link beside it, where the app reads it.
        using var payload = JsonDocument.Parse(body);
        Assert.Equal("The walk is starting", payload.RootElement.GetProperty("aps").GetProperty("alert").GetProperty("title").GetString());
        Assert.Equal("Tap to open Field Kit.", payload.RootElement.GetProperty("aps").GetProperty("alert").GetProperty("body").GetString());
        Assert.Equal("ishaunted://field-kit/launch/1", payload.RootElement.GetProperty("link").GetString());
    }

    [Fact]
    public async Task ASandboxTokenGoesToTheSandbox()
    {
        var (sender, stub, _) = Build();
        await sender.SendAsync(Token, PushEnvironment.Sandbox, Message, default);
        Assert.StartsWith("https://api.sandbox.push.apple.com/3/device/", stub.Sent.Single().Request.RequestUri!.ToString());
    }

    [Theory]
    [InlineData(HttpStatusCode.Gone, "{\"reason\":\"Unregistered\"}", PushOutcome.Unregistered)]
    [InlineData(HttpStatusCode.BadRequest, "{\"reason\":\"BadDeviceToken\"}", PushOutcome.Unregistered)]
    [InlineData(HttpStatusCode.BadRequest, "{\"reason\":\"DeviceTokenNotForTopic\"}", PushOutcome.Unregistered)]
    [InlineData(HttpStatusCode.BadRequest, "{\"reason\":\"PayloadTooLarge\"}", PushOutcome.Failed)]
    [InlineData(HttpStatusCode.TooManyRequests, "{\"reason\":\"TooManyRequests\"}", PushOutcome.Failed)]
    [InlineData(HttpStatusCode.InternalServerError, "", PushOutcome.Failed)]
    public async Task ApplesAnswerSaysWhetherThePhoneIsStillThere(HttpStatusCode status, string body, PushOutcome expected)
    {
        var (sender, stub, _) = Build();
        stub.Status = status;
        stub.Body = body;
        Assert.Equal(expected, await sender.SendAsync(Token, PushEnvironment.Production, Message, default));
    }

    [Fact]
    public async Task NoNetworkIsAFailureNotAnException()
    {
        var (sender, stub, _) = Build();
        stub.Throw = true;
        Assert.Equal(PushOutcome.Failed, await sender.SendAsync(Token, PushEnvironment.Production, Message, default));
    }

    [Fact]
    public async Task WithNoKeyNothingIsSent()
    {
        var (sender, stub, _) = Build(ApnsOptions.Unconfigured);
        Assert.False(sender.IsConfigured);
        Assert.Equal(PushOutcome.NotConfigured, await sender.SendAsync(Token, PushEnvironment.Production, Message, default));
        Assert.Empty(stub.Sent);
    }

    [Fact]
    public async Task TheProviderTokenIsReusedThenRenewedBeforeAppleRefusesIt()
    {
        // Apple refuses a token refreshed more often than every 20 minutes, and one over an hour old.
        var (sender, stub, clock) = Build();
        await sender.SendAsync(Token, PushEnvironment.Production, Message, default);
        clock.Now = clock.Now.AddMinutes(25);
        await sender.SendAsync(Token, PushEnvironment.Production, Message, default);
        clock.Now = clock.Now.AddMinutes(20);
        await sender.SendAsync(Token, PushEnvironment.Production, Message, default);

        var tokens = stub.Sent.Select(s => s.Request.Headers.Authorization!.Parameter).ToList();
        Assert.Equal(tokens[0], tokens[1]);
        Assert.NotEqual(tokens[1], tokens[2]);
    }

    [Fact]
    public async Task AnExpiredProviderTokenIsMintedAfreshNextTime()
    {
        var (sender, stub, clock) = Build();
        stub.Status = HttpStatusCode.Forbidden;
        stub.Body = "{\"reason\":\"ExpiredProviderToken\"}";
        Assert.Equal(PushOutcome.Failed, await sender.SendAsync(Token, PushEnvironment.Production, Message, default));
        stub.Status = HttpStatusCode.OK;
        clock.Now = clock.Now.AddSeconds(1);
        await sender.SendAsync(Token, PushEnvironment.Production, Message, default);

        Assert.NotEqual(stub.Sent[0].Request.Headers.Authorization!.Parameter, stub.Sent[1].Request.Headers.Authorization!.Parameter);
    }
}
