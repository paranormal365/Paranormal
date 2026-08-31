using System.Text.RegularExpressions;
using Ben.Data.Source.Context;
using Microsoft.EntityFrameworkCore;

namespace Ben.Data.WebApi.Services.Scheduling;

/// <summary>
/// Drops error-log rows older than the retention window. Nothing else is touched.
/// </summary>
/// <remarks>
/// <para><b>Why this table deletes while the audit log will not.</b> Item 191 settled that the
/// audit trail is archived and never deleted, because "who did what, when" is part of what is
/// being sold and is worth keeping for years. This is the opposite kind of record: Serilog's
/// Error sink, whose diagnostic value decays in days. Nobody has ever wanted an unhandled
/// exception from three months ago, and rolling these to compressed files would spend the disk
/// space the platform is trying to preserve on the least valuable bytes it holds.</para>
///
/// <para><b>Why it exists at all.</b> Nothing pruned this table. It was the largest in the
/// database (36.5 MB against a 272 MB total) and 96% of it was one avoidable message — see item
/// 202. That message is fixed; without a window, the next unbounded thing simply takes its
/// place.</para>
///
/// <para><b>The safety properties, in the order they matter.</b> A window of zero or less
/// disables the job entirely. A window shorter than <see cref="MinimumDays"/> is refused and
/// clamped rather than obeyed, so a mistyped configuration value cannot empty the table. The
/// table name is validated against a plain-identifier pattern before it can reach a command,
/// because it is the one part of this statement that comes from configuration. And deletes run
/// in bounded batches with a per-pass ceiling, so a first run against years of rows cannot hold
/// a lock or fill the transaction log.</para>
///
/// <para><b>On first deployment this deletes nothing</b>, and that is deliberate: the oldest row
/// present when it was written was four days old, well inside any sane window. The job ships
/// inert and starts working when there is genuinely something old, which is the cheapest possible
/// way to roll out a statement that removes rows.</para>
/// </remarks>
public sealed class LogRetentionJob : IScheduledJob
{
    /// <summary>The shortest window that will be honoured.</summary>
    /// <remarks>
    /// A retention job is one typo away from being a delete-everything job. Seven days is well
    /// below anything anyone would choose on purpose and far above the zero that a truncated or
    /// half-edited setting produces, so it separates intent from accident.
    /// </remarks>
    public const int MinimumDays = 7;

    /// <summary>Default window when nothing is configured.</summary>
    public const int DefaultDays = 30;

    /// <summary>Rows per DELETE, and the most rows one pass will remove.</summary>
    /// <remarks>
    /// Batched because a single DELETE over a very large backlog takes a lock and a transaction
    /// log entry proportional to the backlog. The per-pass ceiling means even a first run against
    /// years of history is many small passes over hours rather than one long one — this is
    /// housekeeping, and it should never be the most expensive thing the database is doing.
    /// </remarks>
    public const int BatchSize = 5_000;
    public const int MaximumPerPass = 50_000;

    /// <summary>How long between actual sweeps.</summary>
    /// <remarks>
    /// The scheduler wakes every five minutes, which is far more often than this needs. The gate
    /// is static because jobs are resolved fresh from a scope on every pass — there is no instance
    /// to remember anything. Losing it on restart is harmless: the worst case is one extra sweep
    /// that finds nothing.
    /// </remarks>
    public static readonly TimeSpan MinimumSweepAge = TimeSpan.FromHours(6);
    private static DateTime _lastSweptUtc = DateTime.MinValue;

    /// <summary>A bare SQL identifier. The table name is the only configured part of the command.</summary>
    private static readonly Regex PlainIdentifierPattern =
        new(@"^[A-Za-z_][A-Za-z0-9_]*$", RegexOptions.Compiled);

    /// <summary>Whether a configured table name may be put into a command.</summary>
    /// <remarks>
    /// Public because it is the guard standing between a configuration file and a DELETE, and a
    /// guard nothing can test is a guard nobody should trust.
    /// </remarks>
    public static bool IsPlainIdentifier(string? name)
        => !string.IsNullOrWhiteSpace(name) && PlainIdentifierPattern.IsMatch(name);

    /// <summary>
    /// The window a configured value actually produces: null when switched off, clamped up to
    /// <see cref="MinimumDays"/> when it is below the floor.
    /// </summary>
    /// <remarks>
    /// Separated from the job so the decision can be tested without a database. The clamp is the
    /// interesting case — obeying a 1 would delete almost everything, and refusing outright would
    /// leave a misconfigured site with no retention at all, so it does the safe thing and says so.
    /// </remarks>
    public static int? WindowDays(int? configured)
    {
        var days = configured ?? DefaultDays;
        if (days <= 0) return null;
        return days < MinimumDays ? MinimumDays : days;
    }

    private readonly IDbContextFactory<BenDataContext> _dbFactory;
    private readonly IConfiguration _configuration;
    private readonly ILogger<LogRetentionJob> _logger;

    public LogRetentionJob(IDbContextFactory<BenDataContext> dbFactory,
                           IConfiguration configuration,
                           ILogger<LogRetentionJob> logger)
    {
        _dbFactory = dbFactory;
        _configuration = configuration;
        _logger = logger;
    }

    public string Name => "log-retention";

    public async Task RunAsync(CancellationToken ct)
    {
        // The sweep gate comes FIRST, and the ordering is the whole point. Resolving the window
        // first would run its clamp warning on every scheduler pass — a log line every five
        // minutes, for ever, from the job written to stop exactly that. A warning worth reading
        // is one written when something is actually done.
        if (DateTime.UtcNow - _lastSweptUtc < MinimumSweepAge) return;

        var days = ResolveWindowDays();
        if (days is null) return;

        var table = _configuration["Logging:Retention:TableName"] ?? "Logs";
        if (!IsPlainIdentifier(table))
        {
            // Refused rather than escaped: a table name that is not a plain identifier is a
            // configuration mistake, and guessing what was meant is how a delete hits the wrong
            // thing.
            _logger.LogError(
                "Log retention is not running: Logging:Retention:TableName is {Table}, which is not a "
              + "plain SQL identifier.", table);
            return;
        }

        // Marked BEFORE the work, deliberately: a sweep that throws waits its full interval
        // rather than retrying every five minutes. The scheduler already logs the failure, and a
        // job that fails repeatedly should not also be the loudest thing in the log.
        _lastSweptUtc = DateTime.UtcNow;

        // LOCAL time, not UTC, and the difference is not cosmetic. The two log tables in this
        // database keep time differently: `AuditLogs.OccurredAt` is UTC, while Serilog's sink
        // writes `Logs.TimeStamp` in the logging process's LOCAL time (ColumnOptions.TimeStamp
        // .ConvertToUtc defaults to false). Measured 2026-08-31: the newest row in Logs read
        // 14:30 while AuditLogs read 19:31, five hours apart on the same instant.
        //
        // A UTC cutoff against a local column silently shifts the window by the offset — five
        // hours here, and in the wrong direction in half the world. The cutoff is therefore built
        // from the same clock the sink writes with, which is this process's.
        var cutoff = DateTime.Now.AddDays(-days.Value);

        await using var db = await _dbFactory.CreateDbContextAsync(ct);

        var removed = 0;
        while (removed < MaximumPerPass && !ct.IsCancellationRequested)
        {
            // The cutoff is a parameter. The batch size and table name are interpolated — the
            // first is a compile-time constant, the second has been checked against
            // IsPlainIdentifier above, and neither can carry a value from outside.
            var batch = await db.Database.ExecuteSqlRawAsync(
                $"DELETE TOP ({BatchSize}) FROM [{table}] WHERE [TimeStamp] < {{0}}",
                new object[] { cutoff }, ct);

            removed += batch;
            if (batch < BatchSize) break;               // caught up
        }

        // Silent when there was nothing to do. A housekeeping job that announces its own
        // inactivity every six hours is just a slower version of the noise this was written to
        // remove.
        if (removed > 0)
            _logger.LogInformation(
                "Log retention removed {Removed} row(s) from {Table} older than {Days} days.",
                removed, table, days.Value);
    }

    /// <summary>The configured window, or null when the job is switched off. Says so when clamped.</summary>
    private int? ResolveWindowDays()
    {
        var configured = _configuration.GetValue<int?>("Logging:Retention:Days");
        var window = WindowDays(configured);

        if (window is not null && configured is not null && configured < MinimumDays)
            _logger.LogWarning(
                "Logging:Retention:Days is {Configured}, below the {Minimum}-day floor. Using {Minimum} — "
              + "set it to 0 to switch log retention off deliberately.",
                configured, MinimumDays, MinimumDays);

        return window;
    }
}
