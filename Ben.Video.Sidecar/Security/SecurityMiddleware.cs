using Ben.Video.Core.SidecarContracts;
using Microsoft.Extensions.Options;

namespace Ben.Video.Sidecar.Security;

/// <summary>
/// The full request gate — every request passes through this before reaching an endpoint. Order
/// matters and mirrors the threat model in DESIGN-item38-long-form-memory.md §5.4:
///
///   1. Host header — must be 127.0.0.1/localhost on our own port. Defeats DNS rebinding (T2):
///      a rebound page's Origin can look legitimate, but the Host header reveals where the
///      request actually thinks it's going.
///   2. CORS preflight (OPTIONS) — answered here directly, including the Private Network Access
///      header Chromium requires for an https page to reach http://127.0.0.1.
///   3. Origin — every request that carries one must match the allowlist (not just preflight;
///      this is the actual enforcement). GET /v1/health with NO Origin header is allowed through
///      (bare curl/local debugging) — everything else requires a valid Origin.
///   4. Pairing token — required on everything except GET /v1/health. Constant-time compare via
///      PairingTokenStore; failures are rate-limited (T1/T6).
///
/// This is the sidecar's single most important file from a "can this be hacked" standpoint —
/// every defense described in the design doc's threat model is enforced here or nowhere.
/// </summary>
public sealed class SecurityMiddleware(
    RequestDelegate next,
    IOptions<SidecarOptions> options,
    PairingTokenStore tokenStore,
    AuthFailureThrottle throttle,
    ILogger<SecurityMiddleware> logger)
{
    private readonly SidecarOptions _options = options.Value;

    public async Task InvokeAsync(HttpContext context)
    {
        context.Response.Headers.CacheControl = "no-store";
        context.Response.Headers["X-Content-Type-Options"] = "nosniff";

        if (!IsAllowedHost(context.Request.Host))
        {
            logger.LogWarning("Rejected request with disallowed Host header: {Host}", context.Request.Host);
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            return;
        }

        var origin = context.Request.Headers.Origin.FirstOrDefault();

        if (HttpMethods.IsOptions(context.Request.Method))
        {
            HandlePreflight(context, origin);
            return;
        }

        var isBareHealthCheck = context.Request.Path.StartsWithSegments("/v1/health") && origin is null;

        if (!isBareHealthCheck)
        {
            if (origin is null || !_options.AllowedOrigins.Contains(origin))
            {
                logger.LogWarning("Rejected request with disallowed/missing Origin: {Origin}", origin);
                context.Response.StatusCode = StatusCodes.Status403Forbidden;
                return;
            }

            context.Response.Headers.AccessControlAllowOrigin = origin;
            context.Response.Headers.Append("Vary", "Origin");
        }

        var isHealthEndpoint = context.Request.Path.StartsWithSegments("/v1/health");
        if (!isHealthEndpoint)
        {
            if (throttle.IsThrottled())
            {
                context.Response.StatusCode = StatusCodes.Status429TooManyRequests;
                context.Response.Headers.RetryAfter = "30";
                return;
            }

            var presented = context.Request.Headers[SidecarProtocol.TokenHeaderName].FirstOrDefault();
            if (!tokenStore.Matches(presented))
            {
                throttle.RecordFailure();
                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                return;
            }
        }

        await next(context);
    }

    private void HandlePreflight(HttpContext context, string? origin)
    {
        if (origin is null || !_options.AllowedOrigins.Contains(origin))
        {
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            return;
        }

        context.Response.Headers.AccessControlAllowOrigin = origin;
        context.Response.Headers.Append("Vary", "Origin");
        context.Response.Headers.AccessControlAllowMethods = "GET, PUT, POST, DELETE, OPTIONS";
        context.Response.Headers.AccessControlAllowHeaders = $"{SidecarProtocol.TokenHeaderName}, Content-Type";
        context.Response.Headers.AccessControlMaxAge = "600";
        // Chromium Private Network Access: required for an https (or even http, in newer Chrome)
        // page to be allowed to reach a loopback-bound server at all. Without this header the
        // browser silently blocks every real request after the preflight, which reads as "the
        // sidecar isn't working" with no error surfaced anywhere obvious — get this wrong and the
        // whole feature looks broken for a completely different reason than security.
        context.Response.Headers["Access-Control-Allow-Private-Network"] = "true";
        context.Response.StatusCode = StatusCodes.Status204NoContent;
    }

    private bool IsAllowedHost(HostString host)
    {
        var value = host.Host;
        return value.Equals("127.0.0.1", StringComparison.Ordinal)
            || value.Equals("localhost", StringComparison.OrdinalIgnoreCase)
            || value.Equals("[::1]", StringComparison.Ordinal);
    }
}
