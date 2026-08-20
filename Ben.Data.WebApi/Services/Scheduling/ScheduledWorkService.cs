namespace Ben.Data.WebApi.Services.Scheduling;

/// <summary>
/// The platform's background worker: wakes on a timer and runs every registered
/// <see cref="IScheduledJob"/> in turn.
/// </summary>
/// <remarks>
/// <para><b>Why not Hangfire or Quartz.</b> Both are good, and both are a great deal more than
/// this needs. The work here is a handful of jobs that want running every few minutes, with no
/// cron expressions, no retries with backoff, no dashboard, and no persisted queue — and the one
/// guarantee that actually matters, not sending the same person the same email twice, is a unique
/// index in the job's own table rather than anything the scheduler could provide. A dependency
/// that brings its own storage schema and its own operational surface is worth taking on when
/// there is work it does that we would otherwise write; there is not, yet.</para>
///
/// <para><b>The loop deliberately does very little.</b> Each job gets its own scope and its own
/// try/catch, so a job that throws cannot take down the loop, and a job that throws on every pass
/// cannot stop the others running. Jobs run sequentially rather than in parallel: they are all
/// short, the database is the shared resource, and sequential failure is far easier to read in a
/// log than interleaved failure.</para>
///
/// <para><b>The first pass waits.</b> Running jobs the instant the process starts means running
/// them during startup, when migrations may still be applying and the app is at its busiest, and
/// it means a restart loop becomes a job loop. A short initial delay costs nothing — nothing here
/// is urgent to the minute.</para>
/// </remarks>
public sealed class ScheduledWorkService : BackgroundService
{
    /// <summary>How long between passes.</summary>
    /// <remarks>
    /// Five minutes is chosen against the reminder job, the only job today: it looks for events
    /// starting within a day, so the difference between reminding somebody 24 hours before and
    /// 23 hours 55 minutes before is not a difference. Shortening this is a decision about the
    /// most impatient job, not about the scheduler.
    /// </remarks>
    public static readonly TimeSpan Interval = TimeSpan.FromMinutes(5);

    /// <summary>How long to wait before the first pass. See the remarks on the class.</summary>
    public static readonly TimeSpan StartupDelay = TimeSpan.FromSeconds(30);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<ScheduledWorkService> _logger;

    public ScheduledWorkService(IServiceScopeFactory scopeFactory, ILogger<ScheduledWorkService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation(
            "Scheduled work starting — first pass in {Delay}, every {Interval} after that.",
            StartupDelay, Interval);

        try
        {
            await Task.Delay(StartupDelay, stoppingToken);

            using var timer = new PeriodicTimer(Interval);
            do
            {
                await RunOnceAsync(stoppingToken);
            }
            while (await timer.WaitForNextTickAsync(stoppingToken));
        }
        catch (OperationCanceledException)
        {
            // Shutdown. Not a failure, and not worth a log line at any level above debug.
        }
    }

    /// <summary>
    /// One pass over every job.
    /// </summary>
    /// <remarks>
    /// Internal rather than private so a test can drive a single pass without waiting on a timer.
    /// The alternative — a test that sleeps for the interval — would be slow and would still not
    /// prove the loop ran the jobs rather than something else having.
    /// </remarks>
    internal async Task RunOnceAsync(CancellationToken ct)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();

        // Resolution is inside the guard, not outside it. A job whose constructor throws — a
        // missing registration, a dependency that fails to build — would otherwise escape this
        // method, and an exception escaping ExecuteAsync stops the entire host by default. The API
        // going down because a reminder job could not be constructed is not a proportionate
        // response to a reminder job that could not be constructed.
        IScheduledJob[] jobs;
        try
        {
            jobs = scope.ServiceProvider.GetServices<IScheduledJob>().ToArray();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Could not resolve the scheduled jobs. Skipping this pass.");
            return;
        }

        foreach (var job in jobs)
        {
            if (ct.IsCancellationRequested) return;

            try
            {
                await job.RunAsync(ct);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                // Logged and swallowed on purpose: one broken job must not stop the others, and
                // must not end the loop for the lifetime of the process. The next pass tries again.
                _logger.LogError(ex, "Scheduled job {Job} failed. Continuing.", job.Name);
            }
        }
    }
}
