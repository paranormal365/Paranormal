using System.Threading.RateLimiting;

namespace Ben.Data.WebApi.Services.LinkUnfurl;

/// <summary>
/// One ceiling on the link-unfurl image proxy shared by every caller together (canvas plan review R21).
/// </summary>
/// <remarks>
/// <para><b>Why a per-person limit is not enough.</b> The <c>link-unfurl-image</c> policy allows each
/// signed-in person 120 pictures a minute, which is what opening a board with many link cards needs.
/// But the proxy makes this server fetch from a stranger's server, and accounts are cheap: a thousand
/// accounts at 120 a minute is a fetch cannon pointed wherever somebody likes, fired from our
/// address. This ceiling bounds the whole server's outbound picture fetching regardless of how many
/// people ask.</para>
///
/// <para><b>Sized for real use.</b> <see cref="DefaultPerMinute"/> is twenty pictures a second across
/// the site. The browser caches each answer for a week (<c>Cache-Control: private, max-age=604800</c>),
/// so a team re-opening the same boards does not spend it twice. A request over the ceiling gets 429
/// and the card falls back to its title and description.</para>
///
/// <para>In-process and per instance, like the rest of this API's rate limiting.</para>
/// </remarks>
public sealed class LinkUnfurlImageCeiling : IDisposable
{
    /// <summary>Picture fetches per minute for the whole server.</summary>
    public const int DefaultPerMinute = 1200;

    private readonly FixedWindowRateLimiter _limiter;

    public LinkUnfurlImageCeiling() : this(DefaultPerMinute) { }

    /// <summary>For tests: a ceiling of <paramref name="perMinute"/>.</summary>
    public LinkUnfurlImageCeiling(int perMinute)
        => _limiter = new FixedWindowRateLimiter(new FixedWindowRateLimiterOptions
        {
            PermitLimit = perMinute,
            Window = TimeSpan.FromMinutes(1),
            QueueLimit = 0,
            AutoReplenishment = true,
        });

    /// <summary>Takes one fetch from this minute's allowance; false when it is spent.</summary>
    public bool TryTake()
    {
        using var lease = _limiter.AttemptAcquire();
        return lease.IsAcquired;
    }

    public void Dispose() => _limiter.Dispose();
}
