using Ben.Data.Source.Context;
using Microsoft.EntityFrameworkCore;

namespace Ben.Data.WebApi.Services;

/// <summary>
/// Gives accounts created before legal names existed a first and last name, derived from their
/// display name.
/// </summary>
/// <remarks>
/// <para>First and last name are required of every new account, but the columns had to be added
/// nullable: 87 accounts already existed. Leaving them null forever would mean "required" was only
/// true of accounts created after a particular Friday, which is the kind of rule that quietly
/// stops being a rule.</para>
///
/// <para><b>Display name is the only evidence available</b>, so it is what this splits. "Sarah
/// Mitchell" becomes Sarah / Mitchell. A single-word display name — "AverageBen" — has no last name
/// to find, so it fills the first name and leaves the last one null rather than inventing one. That
/// is a deliberate half-measure: a guessed surname is worse than an absent one, and the profile
/// asks for it the next time that person visits.</para>
///
/// <para>Runs once at startup and only touches rows where <c>FirstName</c> is null, so it is
/// idempotent and cannot overwrite a name somebody actually typed.</para>
///
/// <para>Mirrors <see cref="UserHandleBackfillService"/>, including resolving nothing scoped into
/// this singleton — a captive dependency there stops the container building at startup.</para>
/// </remarks>
public sealed class UserNameBackfillService : BackgroundService
{
    private readonly IDbContextFactory<BenDataContext> _dbFactory;
    private readonly ILogger<UserNameBackfillService> _logger;

    public UserNameBackfillService(
        IDbContextFactory<BenDataContext> dbFactory,
        ILogger<UserNameBackfillService> logger)
    {
        _dbFactory = dbFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Let startup finish first: this is housekeeping, not something a request waits on.
        try { await Task.Delay(TimeSpan.FromSeconds(20), stoppingToken); }
        catch (OperationCanceledException) { return; }

        try
        {
            await using var db = await _dbFactory.CreateDbContextAsync(stoppingToken);

            var needing = await db.Users
                .Where(u => u.FirstName == null && u.DisplayName != null && u.DisplayName != "")
                .ToListAsync(stoppingToken);

            if (needing.Count == 0) return;

            foreach (var user in needing)
            {
                var (first, last) = SplitDisplayName(user.DisplayName!);
                if (first is null) continue;

                user.FirstName = first;
                user.LastName  = last;
            }

            await db.SaveChangesAsync(stoppingToken);
            _logger.LogInformation(
                "Backfilled first/last names for {Count} accounts from their display names.", needing.Count);
        }
        catch (Exception ex)
        {
            // Housekeeping must never take the API down with it.
            _logger.LogWarning(ex, "User name backfill failed; it will retry on the next start.");
        }
    }

    /// <summary>
    /// Splits a display name on its <b>last</b> space.
    /// </summary>
    /// <remarks>
    /// Last space, not first: "Mary Anne Fletcher" is far more likely to be Mary Anne / Fletcher
    /// than Mary / Anne Fletcher. It gets compound surnames wrong the other way, which is
    /// unavoidable without asking — and asking is what the profile is for.
    /// </remarks>
    internal static (string? First, string? Last) SplitDisplayName(string displayName)
    {
        var trimmed = displayName.Trim();
        if (trimmed.Length == 0) return (null, null);

        var lastSpace = trimmed.LastIndexOf(' ');
        if (lastSpace <= 0) return (trimmed, null);   // one word: a first name and nothing else

        var first = trimmed[..lastSpace].Trim();
        var last  = trimmed[(lastSpace + 1)..].Trim();

        return last.Length == 0 ? (first, null) : (first, last);
    }
}
