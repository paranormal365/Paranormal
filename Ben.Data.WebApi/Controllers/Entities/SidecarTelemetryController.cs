using Ben.Data.Source.Context;
using Ben.Data.Source.Entities;
using Ben.Data.WebApi.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;

namespace Ben.Data.WebApi.Controllers.Entities;

/// <summary>Event kinds recorded in <see cref="SidecarInstallLog"/>.</summary>
public static class SidecarTelemetryEventTypes
{
    /// <summary>The installer reporting a sidecar was put on a machine. No account involved.</summary>
    public const string Install = "Install";

    /// <summary>A signed-in browser successfully pairing with a sidecar.</summary>
    public const string Pair = "Pair";
}

/// <summary>
/// Records that a native sidecar was installed, and that a browser paired with one.
/// </summary>
/// <remarks>
/// <para>The sidecar lives on the user's machine and talks only to their own browser over loopback,
/// so the site never otherwise learns it exists. That becomes a problem the first time the protocol
/// changes and the question is "how many old builds are still out there, and whose".</para>
///
/// <para>Two events rather than one, because they can answer different things. The install ping is
/// anonymous by necessity — it happens before anyone signs in — and carries the version and
/// platform. The pair event is authenticated, so it is the one that attaches a person to an
/// installation.</para>
///
/// <para>The IP is read from the connection in both cases and never from the body. Behind a reverse
/// proxy that address is the proxy's until <c>ForwardedHeaders</c> is configured — the same caveat
/// that applies to rate limiting.</para>
/// </remarks>
[ApiController]
[Route("api/sidecar-telemetry")]
public sealed class SidecarTelemetryController : BenControllerBase
{
    private readonly IDbContextFactory<BenDataContext> _dbContextFactory;
    private readonly ILogger<SidecarTelemetryController> _logger;

    public SidecarTelemetryController(
        IDbContextFactory<BenDataContext> dbContextFactory,
        ILogger<SidecarTelemetryController> logger)
    {
        _dbContextFactory = dbContextFactory;
        _logger = logger;
    }

    /// <summary>
    /// Records an installation. Anonymous: the installer runs before any sign-in, and refusing it
    /// would mean recording nothing at all for the case this exists to measure.
    /// </summary>
    /// <remarks>
    /// Rate-limited on the anonymous policy — an unauthenticated write endpoint is exactly the
    /// shape that gets used to fill a table.
    /// </remarks>
    [HttpPost("installs")]
    [AllowAnonymous]
    [EnableRateLimiting(RateLimiting.AuthPolicy)]
    public Task<IActionResult> RecordInstall(
        [FromBody] SidecarInstallRequest request, CancellationToken ct)
        => RecordAsync(SidecarTelemetryEventTypes.Install, request.InstallId,
                       request.Version, request.Platform, appUserId: null, ct);

    /// <summary>
    /// Records a successful pairing, attributed to the signed-in caller. This is the event that
    /// answers "whose machine is this".
    /// </summary>
    [HttpPost("pairings")]
    [Authorize]
    public Task<IActionResult> RecordPairing(
        [FromBody] SidecarInstallRequest request, CancellationToken ct)
        => RecordAsync(SidecarTelemetryEventTypes.Pair, request.InstallId,
                       request.Version, request.Platform, GetCurrentUserIdOrNull(), ct);

    /// <summary>
    /// The recorded events, newest first — what makes this table worth writing to.
    /// </summary>
    /// <remarks>
    /// SuperAdmin only: these rows name accounts and carry addresses. Capped rather than paged
    /// because the question it answers ("what is out there now") is about recent activity, and an
    /// unbounded query over a table only ever appended to is a slow page waiting to happen.
    /// </remarks>
    [HttpGet]
    [Authorize(Policy = RoleNames.SuperAdmin)]
    public async Task<ActionResult<IReadOnlyList<SidecarInstallLogRecord>>> GetAll(
        [FromQuery] int take = 200, CancellationToken ct = default)
    {
        take = Math.Clamp(take, 1, 1000);

        await using var db = await _dbContextFactory.CreateDbContextAsync(ct);
        var rows = await db.SidecarInstallLogs.AsNoTracking()
            .OrderByDescending(l => l.DateCreated)
            .Take(take)
            .Select(l => new SidecarInstallLogRecord(
                l.Id, l.InstallId, l.EventType, l.Version, l.Platform,
                l.AppUserId,
                l.AppUser == null ? null : (l.AppUser.DisplayName ?? l.AppUser.Email),
                l.IpAddress, l.DateCreated))
            .ToListAsync(ct);

        return Ok(rows);
    }

    /// <summary>
    /// How many distinct installations have been seen, and how many are paired to an account —
    /// the two numbers the raw list is usually read to work out.
    /// </summary>
    [HttpGet("summary")]
    [Authorize(Policy = RoleNames.SuperAdmin)]
    public async Task<ActionResult<SidecarTelemetrySummary>> GetSummary(CancellationToken ct)
    {
        await using var db = await _dbContextFactory.CreateDbContextAsync(ct);

        var installs = await db.SidecarInstallLogs.AsNoTracking()
            .Select(l => l.InstallId).Distinct().CountAsync(ct);
        var pairedInstalls = await db.SidecarInstallLogs.AsNoTracking()
            .Where(l => l.AppUserId != null)
            .Select(l => l.InstallId).Distinct().CountAsync(ct);
        var people = await db.SidecarInstallLogs.AsNoTracking()
            .Where(l => l.AppUserId != null)
            .Select(l => l.AppUserId).Distinct().CountAsync(ct);
        // Distinct (version, install) pairs first, then count per version. Counting distinct
        // InstallIds inside the GroupBy reads better but SQL Server cannot express it through EF,
        // and it failed at runtime rather than at compile time — the pairs are few enough that
        // grouping them in memory costs nothing.
        var versionPairs = await db.SidecarInstallLogs.AsNoTracking()
            .Where(l => l.Version != null)
            .Select(l => new { l.Version, l.InstallId })
            .Distinct()
            .ToListAsync(ct);

        var versions = versionPairs
            .GroupBy(p => p.Version!)
            .Select(g => new SidecarVersionCount(g.Key, g.Count()))
            .OrderByDescending(v => v.Installs)
            .ToList();

        return Ok(new SidecarTelemetrySummary(installs, pairedInstalls, people, versions));
    }

    private async Task<IActionResult> RecordAsync(
        string eventType, Guid installId, string? version, string? platform, Guid? appUserId,
        CancellationToken ct)
    {
        if (installId == Guid.Empty) return BadRequest("installId is required.");

        try
        {
            await using var db = await _dbContextFactory.CreateDbContextAsync(ct);
            db.SidecarInstallLogs.Add(new SidecarInstallLog
            {
                Id          = Guid.NewGuid(),
                InstallId   = installId,
                EventType   = eventType,
                Version     = Truncate(version, 50),
                Platform    = Truncate(platform, 50),
                AppUserId   = appUserId,
                IpAddress   = HttpContext.Connection.RemoteIpAddress?.ToString(),
                DateCreated = DateTime.UtcNow,
            });
            await db.SaveChangesAsync(ct);
            return NoContent();
        }
        catch (Exception ex)
        {
            // Telemetry must never be the reason an install or a pairing fails: both have already
            // happened by the time this is called, and the caller can do nothing useful with an
            // error. Logged rather than swallowed, for the same reason the audit pipeline is.
            _logger.LogWarning(ex, "Could not record sidecar {EventType} for install {InstallId}",
                eventType, installId);
            return NoContent();
        }
    }

    /// <summary>
    /// Clamps client-supplied strings to the column width. The values come from a program on
    /// someone else's machine, so their length is not this application's assumption to make.
    /// </summary>
    private static string? Truncate(string? value, int max)
        => string.IsNullOrWhiteSpace(value) ? null
         : value.Length <= max ? value
         : value[..max];
}

/// <summary>One recorded telemetry event, as shown to a SuperAdmin.</summary>
public sealed record SidecarInstallLogRecord(
    Guid Id,
    Guid InstallId,
    string EventType,
    string? Version,
    string? Platform,
    Guid? AppUserId,
    string? AppUserDisplay,
    string? IpAddress,
    DateTime DateCreated);

/// <summary>Counts worth having without reading the whole list.</summary>
public sealed record SidecarTelemetrySummary(
    int DistinctInstalls,
    int InstallsPairedToAnAccount,
    int DistinctPeople,
    IReadOnlyList<SidecarVersionCount> ByVersion);

/// <summary>Distinct installations reporting a given sidecar version.</summary>
public sealed record SidecarVersionCount(string Version, int Installs);

/// <summary>Body for both telemetry endpoints.</summary>
/// <param name="InstallId">Stable identifier for this installation, generated by the sidecar.</param>
/// <param name="Version">Sidecar assembly version.</param>
/// <param name="Platform">Runtime identifier the sidecar was built for, e.g. "osx-arm64".</param>
public sealed record SidecarInstallRequest(Guid InstallId, string? Version, string? Platform);
