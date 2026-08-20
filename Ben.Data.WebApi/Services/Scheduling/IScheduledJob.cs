namespace Ben.Data.WebApi.Services.Scheduling;

/// <summary>
/// One piece of work the scheduler runs on a timer.
/// </summary>
/// <remarks>
/// <para>Jobs are resolved from a scope on every pass, so a job may depend on scoped services —
/// a <c>DbContext</c>, the email service — exactly as a controller does. Nothing is held between
/// passes, which is what keeps a long-lived background loop from holding a database connection
/// open for the lifetime of the process.</para>
///
/// <para>A job should be safe to run at any time and any number of times. The scheduler makes no
/// promise about how often <see cref="RunAsync"/> is called beyond "not more than once at a time",
/// and offers no way to say "only at 3am" — if a job must not repeat its effects, it records that
/// it did them, as <c>EventReminderJob</c> does.</para>
///
/// <para>Throwing is survivable: the scheduler logs the failure and carries on to the next job and
/// the next pass. It is not a way to signal anything.</para>
/// </remarks>
public interface IScheduledJob
{
    /// <summary>A short stable name, used in log lines. Not a key — nothing looks a job up by it.</summary>
    string Name { get; }

    Task RunAsync(CancellationToken ct);
}
