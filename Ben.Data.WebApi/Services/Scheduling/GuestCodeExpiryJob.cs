using Ben.Data.Source.Context;
using Microsoft.EntityFrameworkCore;

namespace Ben.Data.WebApi.Services.Scheduling;

/// <summary>
/// Clears out a guest code, and the passes it minted, a month after the night it was for.
/// </summary>
/// <remarks>
/// <para><b>Why this exists</b> (crawl C3, 2026-09-21). A code lives 24 hours and nothing ever
/// removed one, so every code a guide issued stayed for good — and its required
/// <c>CreatedByAppUserId</c> is one of the rows that keeps a deleted guide's account row alive
/// (<see cref="Admin.AppUserPurge"/> strips the person and leaves the row while anything they
/// authored points at it). A code that admits nobody to anything is not the group's record of its
/// own work; it is a leftover.</para>
///
/// <para><b>A month, not a day.</b> The guide's "who is working tonight" list
/// (<c>InvestigationJoinCodeController.Holders</c>) reads the passes a code minted, expired or not,
/// beside what each guest contributed — which is what a group looks at while it writes the night
/// up. So the passes are kept while that is plausibly still going on, and not for ever.</para>
///
/// <para><b>What survives.</b> Passes go with their code (<c>OnDelete(Cascade)</c>), but nothing
/// a guest contributed points at a pass: their uploads are keyed to their account and the
/// investigation, and stay exactly where they were. A scan of a swept code is then "not
/// recognised" rather than "ran out" — after a month, the same thing to the person holding it.</para>
///
/// <para>Modelled on <see cref="PendingClientRequestExpiryJob"/>: a bounded batch and a count in
/// the log, because this is a deletion nobody asked for and nobody is told about.</para>
/// </remarks>
public sealed class GuestCodeExpiryJob : IScheduledJob
{
    public string Name => "guest-code-expiry";

    /// <summary>How many codes one pass deletes, so a backlog is worked through, not swallowed.</summary>
    internal const int BatchSize = 500;

    /// <summary>How long after a code expires it, and its passes, go.</summary>
    internal static readonly TimeSpan Grace = TimeSpan.FromDays(30);

    private readonly IDbContextFactory<BenDataContext> _dbFactory;
    private readonly ILogger<GuestCodeExpiryJob> _logger;

    public GuestCodeExpiryJob(IDbContextFactory<BenDataContext> dbFactory, ILogger<GuestCodeExpiryJob> logger)
    {
        _dbFactory = dbFactory;
        _logger = logger;
    }

    public async Task RunAsync(CancellationToken ct)
    {
        var cutoff = DateTime.UtcNow - Grace;

        await using var db = await _dbFactory.CreateDbContextAsync(ct);

        var doomed = await db.InvestigationJoinCodes
            .Where(c => c.ExpiresUtc < cutoff)
            .OrderBy(c => c.ExpiresUtc)
            .Take(BatchSize)
            .Select(c => c.Id)
            .ToListAsync(ct);

        if (doomed.Count == 0) return;

        // Passes first, explicitly, rather than trusting the database's cascade: ExecuteDelete
        // issues its own statement, and a provider that does not cascade would refuse the codes.
        await db.InvestigationGuestPasses
            .Where(p => doomed.Contains(p.InvestigationJoinCodeId))
            .ExecuteDeleteAsync(ct);

        var deleted = await db.InvestigationJoinCodes
            .Where(c => doomed.Contains(c.Id))
            .ExecuteDeleteAsync(ct);

        _logger.LogInformation(
            "Cleared {Count} guest codes that ran out more than {Days} days ago, with their passes.",
            deleted, (int)Grace.TotalDays);
    }
}
