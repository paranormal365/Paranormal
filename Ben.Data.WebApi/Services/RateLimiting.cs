using Microsoft.AspNetCore.RateLimiting;
using System.Globalization;
using System.Threading.RateLimiting;

namespace Ben.Data.WebApi.Services;

/// <summary>
/// Request rate limits for the WebApi.
/// </summary>
/// <remarks>
/// <para>There were none before this, which mattered in three specific places rather than as a
/// general principle:</para>
/// <list type="bullet">
/// <item><description><b>Geocoding.</b> The address search endpoint is anonymous and proxies
/// geocod.io, which is <i>metered and paid</i>. Without a limit, anyone with a shell loop spends
/// the account's quota.</description></item>
/// <item><description><b>Identity.</b> <c>/login</c> is an unthrottled password oracle and
/// <c>/register</c> creates accounts. Identity's own lockout protects a single account from
/// guessing; neither protects the endpoint from being hammered across many accounts.</description></item>
/// <item><description><b>Everything else.</b> A generous global ceiling so a runaway client or a
/// crawler cannot saturate the server, without getting in the way of normal use.</description></item>
/// </list>
///
/// <para>Partitioning is per client IP. Behind a reverse proxy every request appears to come from
/// the proxy, which would collapse all callers into one partition — so a deployment that terminates
/// TLS upstream must also configure <c>ForwardedHeaders</c> for these limits to mean anything. Noted
/// here rather than assumed, because the failure is silent: the limits still "work", just against
/// the wrong identity.</para>
///
/// <para>Each limit is editable by a SuperAdmin on the site settings page, falling back to
/// <c>RateLimits:*</c> in configuration and then to the constants here. The defaults are set to be
/// invisible to a real person and obvious to a script — see <see cref="SupportFormGuard"/>, which
/// applies the same reasoning to the public contact form.</para>
/// </remarks>
public static class RateLimiting
{
    /// <summary>Anonymous geocoding proxy — guards a paid third-party quota.</summary>
    public const string GeocodingPolicy = "geocoding";

    /// <summary>Identity endpoints — login, registration, password reset.</summary>
    public const string AuthPolicy = "auth";

    // Defaults, all per caller per minute. A SuperAdmin can override each one from the site
    // settings page; configuration (RateLimits:*) is the fallback, and these are the last resort.
    // See RateLimitSettingsProvider for how the current values reach the partition factory without
    // a database round-trip per request.
    internal const int DefaultGeocodingPerMinute = 20;
    internal const int DefaultAuthPerMinute      = 20;
    internal const int DefaultGlobalPerMinute    = 600;

    public static IServiceCollection AddBenRateLimiting(
        this IServiceCollection services, IConfiguration configuration)
    {
        services.AddSingleton<RateLimitSettingsProvider>();

        services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

            // Tell the caller when to come back. A client that respects this recovers on its own;
            // one that doesn't keeps getting 429s, which is the point.
            options.OnRejected = async (context, ct) =>
            {
                if (context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter))
                {
                    context.HttpContext.Response.Headers.RetryAfter =
                        ((int)retryAfter.TotalSeconds).ToString(NumberFormatInfo.InvariantInfo);
                }

                context.HttpContext.Response.ContentType = "application/json";
                await context.HttpContext.Response.WriteAsync(
                    "{\"error\":\"Too many requests. Please retry shortly.\"}", ct);
            };

            // Limits are read per request from the provider's in-memory snapshot, so a change made
            // in the admin page applies to a running server.
            options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(
                context => FixedWindowByClient(context, Limits(context).Global));

            options.AddPolicy(GeocodingPolicy, context => FixedWindowByClient(context, Limits(context).Geocoding));
            options.AddPolicy(AuthPolicy,      context => FixedWindowByClient(context, Limits(context).Auth));
        });

        return services;
    }

    private static RateLimitSnapshot Limits(HttpContext context)
        => context.RequestServices.GetRequiredService<RateLimitSettingsProvider>().Current;

    /// <summary>
    /// One fixed one-minute window per client, <paramref name="permitLimit"/> requests wide.
    /// </summary>
    /// <remarks>
    /// <para>Queueing is deliberately off (<c>QueueLimit = 0</c>): holding excess requests open
    /// would tie up server resources on exactly the traffic being rejected, so over-limit callers
    /// are refused immediately instead.</para>
    ///
    /// <para>The limit is part of the partition key, which looks redundant and is not. The
    /// <c>factory</c> only runs for a key that has no limiter yet — an existing partition keeps the
    /// options it was built with. Without the limit in the key, editing the value in the admin page
    /// would appear to do nothing for every caller already inside a window, and the change would
    /// land at some unpredictable later point. Including it means a new limit is simply a new
    /// partition, and the stale one is evicted once idle.</para>
    /// </remarks>
    private static RateLimitPartition<string> FixedWindowByClient(HttpContext context, int permitLimit)
        => RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: $"{ClientKey(context)}|{permitLimit}",
            factory: _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = permitLimit,
                Window      = TimeSpan.FromMinutes(1),
                QueueLimit  = 0,
            });

    /// <summary>
    /// Identifies the caller for partitioning: the authenticated user when there is one, otherwise
    /// the remote IP.
    /// </summary>
    /// <remarks>
    /// Keying signed-in callers by user id rather than address means several people behind one
    /// office NAT do not consume each other's budget. Anonymous traffic has nothing better to key
    /// on than the address — see the proxy caveat on the class.
    /// </remarks>
    private static string ClientKey(HttpContext context)
    {
        var userId = context.User.FindFirst(EntraClaimsTransformation.AppUserIdClaimType)?.Value
                  ?? context.User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;

        if (!string.IsNullOrEmpty(userId)) return $"user:{userId}";

        return $"ip:{context.Connection.RemoteIpAddress?.ToString() ?? "unknown"}";
    }
}
