using Ben.Data.Source.Context;
using Microsoft.EntityFrameworkCore;

namespace Ben.Data.WebApi.Services.Scheduling;

/// <summary>
/// Forgets a request for help that nobody ever claimed.
/// </summary>
/// <remarks>
/// <para><b>Why this exists.</b> <c>PendingClientRequest</c>'s own doc says rows are "deleted on
/// adoption or discard and ignored after <c>DateExpires</c>". The first half was true — the adopt
/// and discard endpoints both remove the row. The second half was not implemented anywhere: the
/// 2026-09-17 audit found no sweep, so a row nobody ever claimed simply stayed.</para>
///
/// <para><b>What was being kept.</b> A pending request is submitted by an <b>anonymous</b> visitor
/// about a property that is usually not theirs, and it holds a street address, a city, a postcode,
/// latitude and longitude, a gender, a birth year and a free-text description of what has been
/// happening in somebody's home. It is owned by nobody — that is the point of the table — and is
/// only ever claimed by the account holder clicking an emailed link. If they never click, nothing
/// deleted it. "Ignored after DateExpires" meant ignored by readers, not gone.</para>
///
/// <para><b>Deleted rather than anonymised.</b> Everything in the row is the identifying detail;
/// there is nothing left worth keeping once the link is dead. The row's whole value was the chance
/// to adopt it, and after the deadline that chance has gone.</para>
///
/// <para><b>Nothing is emailed.</b> The visitor was never told this row exists — only the account
/// holder was, once, with the link. A letter now would be telling somebody about a record of their
/// home that they never knew we held, on the occasion of deleting it.</para>
///
/// <para>Modelled on <see cref="HoldExpiryJob"/>'s handling of picks made by email: bounded batch,
/// its own try, and a count in the log so the sweep is visible when it does something.</para>
/// </remarks>
public sealed class PendingClientRequestExpiryJob : IScheduledJob
{
    public string Name => "pending-client-request-expiry";

    /// <summary>
    /// How many one pass deletes.
    /// </summary>
    /// <remarks>
    /// A bound rather than "all of them", so the first pass after this ships works through
    /// whatever has accumulated over several passes instead of one very large transaction.
    /// </remarks>
    internal const int BatchSize = 500;

    /// <summary>
    /// How long after expiry a row goes.
    /// </summary>
    /// <remarks>
    /// A short grace period rather than deleting on the stroke of the deadline: an account holder
    /// who clicks a link an hour late gets their request rather than a dead end, and the point of
    /// the sweep is that nothing is kept indefinitely, not that it is kept to the second.
    /// </remarks>
    internal static readonly TimeSpan Grace = TimeSpan.FromDays(1);

    private readonly IDbContextFactory<BenDataContext> _dbFactory;
    private readonly ILogger<PendingClientRequestExpiryJob> _logger;

    public PendingClientRequestExpiryJob(
        IDbContextFactory<BenDataContext> dbFactory, ILogger<PendingClientRequestExpiryJob> logger)
    {
        _dbFactory = dbFactory;
        _logger = logger;
    }

    public async Task RunAsync(CancellationToken ct)
    {
        var cutoff = DateTime.UtcNow - Grace;

        await using var db = await _dbFactory.CreateDbContextAsync(ct);

        var doomed = await db.PendingClientRequests
            .Where(r => r.DateExpires < cutoff)
            .OrderBy(r => r.DateExpires)
            .Take(BatchSize)
            .Select(r => r.Id)
            .ToListAsync(ct);

        if (doomed.Count == 0) return;

        var deleted = await db.PendingClientRequests
            .Where(r => doomed.Contains(r.Id))
            .ExecuteDeleteAsync(ct);

        // Logged with a count because this is a deletion nobody asked for and nobody is told
        // about; the log is the only record that it happened.
        _logger.LogInformation(
            "Deleted {Count} requests for help that expired without being claimed.", deleted);
    }
}
