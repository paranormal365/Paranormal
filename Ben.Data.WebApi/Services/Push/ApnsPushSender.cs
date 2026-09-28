using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Ben.Data.Common.Enums;
using Ben.Data.Common.Helpers;

namespace Ben.Data.WebApi.Services.Push;

/// <summary>
/// The APNs key and the provider token signed with it — ONE per process (item 252).
/// </summary>
/// <remarks>
/// <para><b>Why this is its own singleton.</b> The sender is a typed HTTP client, and those are
/// made fresh for every use. When the token lived on the sender, every launch minted a new one,
/// and Apple refused the pushes with <c>429 TooManyProviderTokenUpdates</c> — found in the local
/// API's log on 2026-09-28, after an afternoon of test launches. Apple allows a new token at most
/// every 20 minutes and accepts one for an hour, so the one token is reused for 40.</para>
/// </remarks>
public sealed class ApnsCredentials : IDisposable
{
    public static readonly TimeSpan ProviderTokenLifetime = TimeSpan.FromMinutes(40);

    private readonly ApnsOptions _options;
    private readonly TimeProvider _clock;
    private readonly ECDsa? _key;
    private readonly object _lock = new();
    private string? _token;
    private DateTimeOffset _issued;

    public ApnsCredentials(ApnsOptions options, TimeProvider? clock = null)
    {
        _options = options;
        _clock = clock ?? TimeProvider.System;
        if (options.IsConfigured) _key = Es256Jwt.ImportP256(options.PrivateKeyPem, "Apns:PrivateKeyPath");
    }

    public bool IsConfigured => _key is not null;
    public string BundleId => _options.BundleId;

    /// <summary>The provider token, minted afresh only when the last one is 40 minutes old or was refused.</summary>
    public string ProviderToken()
    {
        if (_key is null) throw new InvalidOperationException("APNs is not configured.");
        var now = _clock.GetUtcNow();
        lock (_lock)
        {
            if (_token is not null && now - _issued < ProviderTokenLifetime) return _token;
            var header = JsonSerializer.Serialize(new { alg = "ES256", kid = _options.KeyId });
            var claims = JsonSerializer.Serialize(new { iss = _options.TeamId, iat = now.ToUnixTimeSeconds() });
            _token = Es256Jwt.Sign(_key, header, claims);
            _issued = now;
            return _token;
        }
    }

    /// <summary>Apple said the token was expired or invalid: the next push mints a new one.</summary>
    public void Invalidate()
    {
        lock (_lock) _token = null;
    }

    public void Dispose() => _key?.Dispose();
}

/// <summary>
/// Apple Push Notification service, spoken directly: HTTP/2 to Apple with a token signed by our
/// APNs key (item 252).
/// </summary>
/// <remarks>
/// <para><b>Token-based, not certificate-based.</b> The provider token is an ES256 JWT — the same
/// signing Sign in with Apple and MapKit already do through <see cref="Es256Jwt"/> — so there is
/// no certificate to renew every year. The token lives in <see cref="ApnsCredentials"/>, one per
/// process, because this sender is made fresh for every use.</para>
///
/// <para><b>Two services.</b> A build run from Xcode registers with the sandbox; TestFlight and the
/// App Store with production. Each token is sent to the one it came from, as the app reported.</para>
///
/// <para><b>Unconfigured is not an error.</b> With no key, every send answers
/// <see cref="PushOutcome.NotConfigured"/> and the caller says so; a development machine without
/// the key still runs every other part of a launch.</para>
/// </remarks>
public sealed class ApnsPushSender : IPushSender
{
    public const string ProductionHost = "https://api.push.apple.com";
    public const string SandboxHost = "https://api.sandbox.push.apple.com";

    private readonly HttpClient _http;
    private readonly ApnsCredentials _credentials;
    private readonly ILogger<ApnsPushSender> _log;

    public ApnsPushSender(HttpClient http, ApnsCredentials credentials, ILogger<ApnsPushSender> log)
    {
        _http = http;
        _credentials = credentials;
        _log = log;
    }

    public bool IsConfigured => _credentials.IsConfigured;

    public async Task<PushOutcome> SendAsync(string token, PushEnvironment environment, PushMessage message, CancellationToken ct)
    {
        if (!_credentials.IsConfigured) return PushOutcome.NotConfigured;

        var host = environment == PushEnvironment.Sandbox ? SandboxHost : ProductionHost;
        using var request = new HttpRequestMessage(HttpMethod.Post, $"{host}/3/device/{token}")
        {
            Version = HttpVersion.Version20,
            VersionPolicy = HttpVersionPolicy.RequestVersionExact,
            Content = new StringContent(Payload(message), Encoding.UTF8, "application/json"),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("bearer", _credentials.ProviderToken());
        request.Headers.TryAddWithoutValidation("apns-topic", _credentials.BundleId);
        request.Headers.TryAddWithoutValidation("apns-push-type", "alert");
        request.Headers.TryAddWithoutValidation("apns-priority", "10");
        request.Headers.TryAddWithoutValidation("apns-expiration",
            (message.ExpiresAt?.ToUnixTimeSeconds() ?? 0).ToString(System.Globalization.CultureInfo.InvariantCulture));
        if (!string.IsNullOrEmpty(message.CollapseId))
            request.Headers.TryAddWithoutValidation("apns-collapse-id", message.CollapseId);

        HttpResponseMessage response;
        try
        {
            response = await _http.SendAsync(request, ct);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException && !ct.IsCancellationRequested)
        {
            _log.LogWarning(ex, "APNs could not be reached ({Environment})", environment);
            return PushOutcome.Failed;
        }

        using (response)
        {
            if (response.IsSuccessStatusCode) return PushOutcome.Delivered;

            var reason = await ReasonAsync(response, ct);
            if (response.StatusCode == HttpStatusCode.Gone
                || reason is "BadDeviceToken" or "DeviceTokenNotForTopic" or "Unregistered")
                return PushOutcome.Unregistered;

            if (reason is "ExpiredProviderToken" or "InvalidProviderToken")
                _credentials.Invalidate();   // mint afresh next time

            _log.LogWarning("APNs refused a push: {Status} {Reason} ({Environment})",
                (int)response.StatusCode, reason ?? "no reason given", environment);
            return PushOutcome.Failed;
        }
    }

    /// <summary>The body Apple expects: the alert under <c>aps</c>, the app's own keys beside it.</summary>
    public static string Payload(PushMessage message)
    {
        var body = new Dictionary<string, object>
        {
            ["aps"] = new Dictionary<string, object>
            {
                ["alert"] = new Dictionary<string, string> { ["title"] = message.Title, ["body"] = message.Body },
                ["sound"] = "default",
            },
        };
        foreach (var (key, value) in message.Data)
            if (key != "aps") body[key] = value;
        return JsonSerializer.Serialize(body);
    }

    private static async Task<string?> ReasonAsync(HttpResponseMessage response, CancellationToken ct)
    {
        try
        {
            var text = await response.Content.ReadAsStringAsync(ct);
            if (string.IsNullOrWhiteSpace(text)) return null;
            using var doc = JsonDocument.Parse(text);
            return doc.RootElement.TryGetProperty("reason", out var r) ? r.GetString() : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
