using Ben.Data.Source.Context;
using Microsoft.EntityFrameworkCore;

namespace Ben.Data.WebApi.Services;

/// <summary>
/// Supplies the current rate limits to the limiter, reading them from the SuperAdmin settings page
/// with <c>appsettings</c> as the fallback.
/// </summary>
/// <remarks>
/// <para>This exists because of where it is called from. The limiter's partition factory runs on
/// <i>every request</i> and is synchronous, while <see cref="SiteSettingsService"/> opens a
/// <c>DbContext</c> and queries on every call with no caching. Wiring one directly to the other
/// would put a database round-trip in front of every request in the application — a worse problem
/// than the one rate limiting is here to solve.</para>
///
/// <para>So reads are served from a snapshot held in memory, and refreshing it never blocks the
/// caller: a request that notices the snapshot is stale kicks off a background refresh and uses the
/// values it already has. The cost of that is a change taking up to <see cref="RefreshInterval"/>
/// to be picked up, which is the right trade for a setting nobody edits twice in a minute.</para>
///
/// <para>A database that is unreachable or a value that is missing or nonsense leaves the previous
/// snapshot in place rather than failing the request or dropping the limit to zero — an admin
/// typing "twenty" into the box should not be able to lock everyone out of the site.</para>
/// </remarks>
public sealed class RateLimitSettingsProvider
{
    /// <summary>How long a snapshot is used before a refresh is started behind the next request.</summary>
    public static readonly TimeSpan RefreshInterval = TimeSpan.FromSeconds(30);

    private readonly IDbContextFactory<BenDataContext> _dbContextFactory;
    private readonly ILogger<RateLimitSettingsProvider> _logger;

    private volatile RateLimitSnapshot _snapshot;
    private long _nextRefreshTicks;
    private int _refreshing;

    public RateLimitSettingsProvider(
        IDbContextFactory<BenDataContext> dbContextFactory,
        IConfiguration configuration,
        ILogger<RateLimitSettingsProvider> logger)
    {
        _dbContextFactory = dbContextFactory;
        _logger = logger;

        // Configuration is the floor: it is what the app runs on before anything is saved in the
        // admin page, and what it falls back to if that value is later cleared.
        _snapshot = new RateLimitSnapshot(
            Geocoding: configuration.GetValue("RateLimits:GeocodingPerMinute", RateLimiting.DefaultGeocodingPerMinute),
            Auth:      configuration.GetValue("RateLimits:AuthPerMinute",      RateLimiting.DefaultAuthPerMinute),
            Global:    configuration.GetValue("RateLimits:GlobalPerMinute",    RateLimiting.DefaultGlobalPerMinute),
            EventAttendance: configuration.GetValue("RateLimits:EventAttendancePerMinute", RateLimiting.DefaultEventAttendancePerMinute));

        _nextRefreshTicks = DateTime.UtcNow.Ticks; // refresh on first use
    }

    /// <summary>The limits in force right now. Never blocks; may be up to one refresh interval old.</summary>
    public RateLimitSnapshot Current
    {
        get
        {
            EnsureFresh();
            return _snapshot;
        }
    }

    private void EnsureFresh()
    {
        if (DateTime.UtcNow.Ticks < Interlocked.Read(ref _nextRefreshTicks)) return;

        // One refresh at a time; everyone else keeps using the current snapshot meanwhile.
        if (Interlocked.Exchange(ref _refreshing, 1) == 1) return;

        Interlocked.Exchange(ref _nextRefreshTicks, DateTime.UtcNow.Add(RefreshInterval).Ticks);
        _ = Task.Run(RefreshAsync);
    }

    private async Task RefreshAsync()
    {
        try
        {
            await using var db = await _dbContextFactory.CreateDbContextAsync(CancellationToken.None);
            var current = _snapshot;

            _snapshot = new RateLimitSnapshot(
                Geocoding: await ReadAsync(db, SiteSettingKeys.RateLimitGeocodingPerMinute, current.Geocoding),
                Auth:      await ReadAsync(db, SiteSettingKeys.RateLimitAuthPerMinute,      current.Auth),
                Global:    await ReadAsync(db, SiteSettingKeys.RateLimitGlobalPerMinute,    current.Global),
                EventAttendance: await ReadAsync(db, SiteSettingKeys.RateLimitEventAttendancePerMinute, current.EventAttendance));
        }
        catch (Exception ex)
        {
            // Keep serving the snapshot we have. Rate limits are not worth failing requests over,
            // but a database this code cannot reach is worth saying out loud.
            _logger.LogWarning(ex, "Could not refresh rate limits from site settings; keeping the previous values.");
        }
        finally
        {
            Interlocked.Exchange(ref _refreshing, 0);
        }
    }

    /// <summary>
    /// Reads one limit, keeping <paramref name="fallback"/> when the setting is unset, unparseable,
    /// or not a positive number.
    /// </summary>
    private static async Task<int> ReadAsync(BenDataContext db, string key, int fallback)
    {
        var raw = await SiteSettingsService.GetAsync(db, key, CancellationToken.None);
        return int.TryParse(raw, out var value) && value > 0 ? value : fallback;
    }
}

/// <summary>An immutable set of limits, swapped in wholesale so a reader never sees a half-update.</summary>
public sealed record RateLimitSnapshot(int Geocoding, int Auth, int Global, int EventAttendance);
