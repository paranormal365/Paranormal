using Ben.Data.Common.Constants;
using Ben.Data.Source.Context;
using Ben.Data.WebApi.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Ben.Data.WebApi.Controllers.Admin;

/// <summary>
/// What the rate limits have turned away, and how much of it.
/// </summary>
/// <remarks>
/// <para>Ben's rule for the alerting is one message per limit, ever — so this is the place the
/// message points at, and where the magnitude lives: <i>"650 takes less of a look than
/// 6,500."</i> A tally nobody can read would leave the single message as the only evidence, which
/// is the write-only pattern this codebase keeps finding.</para>
///
/// <para>Reads a tally, not a log. Counts accumulate in memory on the refusal path and are
/// flushed on a timer, so what this returns can be up to one flush interval behind — stated on
/// the page rather than hidden, because a number that lags without saying so gets read as a
/// number that stopped moving.</para>
/// </remarks>
[ApiController]
[Authorize(Policy = RoleNames.SuperAdmin)]
[Route("api/admin/rate-limits")]
public sealed class AdminRateLimitController : BenControllerBase
{
    private readonly IDbContextFactory<BenDataContext> _db;
    private readonly RateLimitAlerting _alerting;
    private readonly RateLimitSettingsProvider _limits;

    public AdminRateLimitController(
        IDbContextFactory<BenDataContext> db,
        RateLimitAlerting alerting,
        RateLimitSettingsProvider limits)
    {
        _db = db;
        _alerting = alerting;
        _limits = limits;
    }

    /// <summary>Every limit that has ever refused anything, worst first.</summary>
    [HttpGet]
    public async Task<ActionResult<IEnumerable<RateLimitRefusalRecord>>> GetAll(CancellationToken ct)
    {
        // Bring the in-memory counts up to date first, so a SuperAdmin who loads this page right
        // after being told about a limit does not see a number smaller than the message quoted.
        await _alerting.FlushAsync(ct);

        await using var db = await _db.CreateDbContextAsync(ct);
        var current = _limits.Current;

        var rows = await db.RateLimitRefusals.AsNoTracking()
            .OrderByDescending(r => r.Refusals)
            .ToListAsync(ct);

        return Ok(rows.Select(r => new RateLimitRefusalRecord(
            PolicyName: r.PolicyName,
            FriendlyName: new RateLimitAlert(r.PolicyName, r.Refusals, r.DistinctCallers).FriendlyName,
            Refusals: r.Refusals,
            DistinctCallers: r.DistinctCallers,
            PeakDistinctCallers: r.PeakDistinctCallers,
            LimitPerMinute: LimitFor(r.PolicyName, current),
            DateFirstSeen: r.DateFirstSeen,
            DateLastSeen: r.DateLastSeen,
            DateNotified: r.DateNotified)));
    }

    /// <summary>
    /// Re-arms the one-time notice for a limit, so the next burst sends a fresh message.
    /// </summary>
    /// <remarks>
    /// The only way a second message is ever sent about the same limit, and a deliberate act
    /// rather than a timer — after raising a limit, this is how somebody asks to be told whether
    /// raising it was enough.
    /// </remarks>
    [HttpPost("{policyName}/notify-again")]
    public async Task<ActionResult<bool>> NotifyAgain(
        string policyName, [FromBody] object? _, CancellationToken ct)
    {
        await using var db = await _db.CreateDbContextAsync(ct);

        var row = await db.RateLimitRefusals.FirstOrDefaultAsync(r => r.PolicyName == policyName, ct);
        if (row is null) return NotFound();

        row.DateNotified = null;
        await db.SaveChangesAsync(ct);

        // A body rather than 204: the web client deserializes the response, and an empty one
        // reads back as the default — the "Ok(null) becomes an empty 204" trap this codebase has
        // already been caught by once.
        return Ok(true);
    }

    /// <summary>The limit currently in force for a policy, so the count can be read against it.</summary>
    private static int LimitFor(string policyName, RateLimitSnapshot limits) => policyName switch
    {
        RateLimiting.EventAttendancePolicy => limits.EventAttendance,
        RateLimiting.GeocodingPolicy       => limits.Geocoding,
        RateLimiting.AuthPolicy            => limits.Auth,
        _                                  => limits.Global,
    };
}

/// <summary>One limit's tally, as the admin page reads it.</summary>
/// <remarks>
/// <c>DistinctCallers</c> is the most recent window rather than a lifetime figure — it answers
/// "is this a crowd or one script", which a lifetime total would not.
/// </remarks>
public sealed record RateLimitRefusalRecord(
    string PolicyName,
    string FriendlyName,
    long Refusals,
    int DistinctCallers,
    int PeakDistinctCallers,
    int LimitPerMinute,
    DateTime DateFirstSeen,
    DateTime DateLastSeen,
    DateTime? DateNotified);
