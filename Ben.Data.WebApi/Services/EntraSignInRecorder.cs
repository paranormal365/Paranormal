using Ben.Data.Source.Context;
using Ben.Data.Source.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

namespace Ben.Data.WebApi.Services;

/// <summary>
/// Writes one <see cref="SignInEvent"/> per Entra visit.
/// </summary>
/// <remarks>
/// <para><b>Called from the claims transformation</b>, because that is where an Entra token
/// actually lands and is resolved to a local account — and it is the only place that has already
/// asked whether the account may sign in at all. <c>RecordingSignInManager</c> cannot do it: an
/// Entra session never touches it, which its own remarks have said since Apple was added.</para>
///
/// <para><b>It runs on every request, so the cheap check comes first.</b> A memory entry per
/// person answers almost every call without touching the database. Only a miss — the first
/// request of a visit, or the first after a restart — asks the database, and only a genuine
/// arrival writes.</para>
///
/// <para><b>The database is asked as well as the cache, and that is not belt and braces.</b> The
/// cache is per process and empty after every deploy; without the second check a restart would
/// record a fresh arrival for everybody still holding a token, and a busy week of deploys would
/// read as a busy week of visitors.</para>
///
/// <para><b>Recording never breaks a request</b>, the same rule <c>RecordingSignInManager</c>
/// follows: a failure here is a lost data point, and the alternative is a database hiccup
/// refusing somebody's perfectly good token.</para>
/// </remarks>
public sealed class EntraSignInRecorder
{
    private readonly IDbContextFactory<BenDataContext> _dbContextFactory;
    private readonly IMemoryCache _cache;
    private readonly ILogger<EntraSignInRecorder> _log;

    public EntraSignInRecorder(
        IDbContextFactory<BenDataContext> dbContextFactory,
        IMemoryCache cache,
        ILogger<EntraSignInRecorder> log)
    {
        _dbContextFactory = dbContextFactory;
        _cache = cache;
        _log = log;
    }

    private static string KeyFor(Guid appUserId) => $"entra-visit:{appUserId}";

    /// <summary>Records an arrival for this person, if this request begins one.</summary>
    public async Task NoteRequestAsync(Guid appUserId, CancellationToken ct = default)
    {
        try
        {
            var now = DateTime.UtcNow;

            if (_cache.TryGetValue<DateTime>(KeyFor(appUserId), out var seen)
                && !EntraSignInSessions.IsANewVisit(seen, now))
                return;

            await using var db = await _dbContextFactory.CreateDbContextAsync(ct);

            // Indexed by (AppUserId, Utc), so this is a seek to the end of one person's rows.
            var last = await db.SignInEvents.AsNoTracking()
                .Where(e => e.AppUserId == appUserId
                         && e.Succeeded
                         && e.Method == RecordingSignInManager.EntraMethod)
                .OrderByDescending(e => e.Utc)
                .Select(e => (DateTime?)e.Utc)
                .FirstOrDefaultAsync(ct);

            if (!EntraSignInSessions.IsANewVisit(last, now))
            {
                Remember(appUserId, last!.Value);
                return;
            }

            db.SignInEvents.Add(new SignInEvent
            {
                Id        = Guid.NewGuid(),
                AppUserId = appUserId,
                Utc       = now,
                Succeeded = true,
                Method    = RecordingSignInManager.EntraMethod,
            });

            await db.SaveChangesAsync(ct);
            Remember(appUserId, now);
        }
        catch (Exception ex)
        {
            // See the class remarks: a lost count is a smaller problem than a refused request.
            _log.LogWarning(ex, "Could not record an Entra visit for {UserId}.", appUserId);
        }
    }

    /// <summary>
    /// Holds the last arrival for as long as it could still suppress another one.
    /// </summary>
    /// <remarks>
    /// The entry expires when the visit does, so a stale one can never hide a genuine arrival —
    /// the worst a lost entry costs is one database read.
    /// </remarks>
    private void Remember(Guid appUserId, DateTime whenUtc)
    {
        var expires = whenUtc + EntraSignInSessions.Visit;
        if (expires <= DateTime.UtcNow) return;
        _cache.Set(KeyFor(appUserId), whenUtc, expires);
    }
}
