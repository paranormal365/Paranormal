using Ben.Data.Source.Context;
using Microsoft.EntityFrameworkCore;

namespace Ben.Data.WebApi.Services;

/// <summary>
/// Gives an <c>@name</c> to any account created before handles existed.
/// </summary>
/// <remarks>
/// <para>Runs once at startup and does nothing at all on every subsequent start, because after the
/// first pass there is nothing without a handle. Modelled on <c>FileMigrationService</c>, the
/// existing one-time fixup in this project.</para>
///
/// <para>Not done in the migration itself: deriving a handle means <c>UserHandle.Suggest</c>'s
/// rules and a uniquifying loop, which is C# rather than SQL, and duplicating those rules in a
/// migration would guarantee they drifted from the ones the rest of the app enforces.</para>
///
/// <para>An account left without a handle would be invisible to the feed — unmentionable, and
/// with no profile anyone can link to — so this failing is worth a loud log line rather than a
/// silent skip.</para>
/// </remarks>
public sealed class UserHandleBackfillService : BackgroundService
{
    private readonly IDbContextFactory<BenDataContext> _dbFactory;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<UserHandleBackfillService> _logger;

    /// <remarks>
    /// <see cref="UserHandleService"/> is resolved from a scope rather than injected, because a
    /// hosted service is a singleton and holding a scoped service in one is a captive dependency —
    /// the container refuses to build it, at startup, before anything else runs.
    /// </remarks>
    public UserHandleBackfillService(
        IDbContextFactory<BenDataContext> dbFactory,
        IServiceScopeFactory scopeFactory,
        ILogger<UserHandleBackfillService> logger)
    {
        _dbFactory = dbFactory;
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await using var scope = _scopeFactory.CreateAsyncScope();
            var handles = scope.ServiceProvider.GetRequiredService<UserHandleService>();

            await using var db = await _dbFactory.CreateDbContextAsync(stoppingToken);

            var pending = await db.AppUsers
                .Where(u => u.Handle == null)
                .Select(u => new { u.Id, u.DisplayName, u.Email })
                .ToListAsync(stoppingToken);

            if (pending.Count == 0) return;

            _logger.LogInformation("Backfilling @names for {Count} account(s).", pending.Count);

            foreach (var account in pending)
            {
                if (stoppingToken.IsCancellationRequested) return;

                // One at a time, saved as we go. AllocateAsync reads what is already taken, so a
                // batch saved at the end would let two accounts in the same batch be allocated the
                // same name — the unique index would then reject the whole save and none of them
                // would get one.
                var handle = await handles.AllocateAsync(account.DisplayName, account.Email, stoppingToken);

                var user = await db.AppUsers.FirstAsync(u => u.Id == account.Id, stoppingToken);
                user.Handle = handle;
                await db.SaveChangesAsync(stoppingToken);
            }

            _logger.LogInformation("Backfilled {Count} @name(s).", pending.Count);
        }
        catch (OperationCanceledException)
        {
            // Shutting down mid-backfill. The next start picks up whatever is still null.
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Could not backfill @names. Accounts without one cannot be mentioned in the feed "
                + "and have no profile link. This runs again on the next start.");
        }
    }
}
