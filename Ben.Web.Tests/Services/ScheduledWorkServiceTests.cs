using Ben.Data.WebApi.Services.Scheduling;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Ben.Web.Tests.Services;

/// <summary>
/// The scheduler loop: that one broken job cannot silence the others.
/// </summary>
/// <remarks>
/// This is the platform's first background worker, and the failure it is designed against is a
/// specific one. A loop that lets an exception escape ends — permanently, for the lifetime of the
/// process, with no restart and usually no log line anyone reads. Every job therefore gets its own
/// try/catch, and this fixture holds that property still by running a pass with a job that always
/// throws sitting in front of one that must still run.
/// </remarks>
public sealed class ScheduledWorkServiceTests
{
    private sealed class RecordingJob : IScheduledJob
    {
        public RecordingJob(string name, bool throws = false) { Name = name; _throws = throws; }

        private readonly bool _throws;
        public string Name { get; }
        public int Runs { get; private set; }

        public Task RunAsync(CancellationToken ct)
        {
            Runs++;
            if (_throws) throw new InvalidOperationException($"{Name} is broken.");
            return Task.CompletedTask;
        }
    }

    private static ScheduledWorkService Build(params IScheduledJob[] jobs)
    {
        var services = new ServiceCollection();
        foreach (var job in jobs) services.AddSingleton(job);

        return new ScheduledWorkService(
            services.BuildServiceProvider().GetRequiredService<IServiceScopeFactory>(),
            NullLogger<ScheduledWorkService>.Instance);
    }

    [Fact]
    public async Task Every_registered_job_runs_on_a_pass()
    {
        var first  = new RecordingJob("first");
        var second = new RecordingJob("second");

        await Build(first, second).RunOnceAsync(default);

        Assert.Equal(1, first.Runs);
        Assert.Equal(1, second.Runs);
    }

    [Fact]
    public async Task A_job_that_throws_does_not_stop_the_ones_after_it()
    {
        var broken = new RecordingJob("broken", throws: true);
        var after  = new RecordingJob("after");

        await Build(broken, after).RunOnceAsync(default);

        Assert.Equal(1, after.Runs);
    }

    [Fact]
    public async Task A_job_that_throws_every_time_is_still_tried_next_pass()
    {
        // The alternative — quarantining a failing job — would mean a transient database blip at
        // startup silently disabling reminders until somebody restarted the process.
        var broken = new RecordingJob("broken", throws: true);
        var service = Build(broken);

        await service.RunOnceAsync(default);
        await service.RunOnceAsync(default);

        Assert.Equal(2, broken.Runs);
    }

    [Fact]
    public async Task A_cancelled_pass_stops_before_running_anything()
    {
        var job = new RecordingJob("job");
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();

        await Build(job).RunOnceAsync(cancelled.Token);

        Assert.Equal(0, job.Runs);
    }

    /// <summary>
    /// A job that cannot even be constructed does not take the API down with it.
    /// </summary>
    /// <remarks>
    /// An exception escaping <c>ExecuteAsync</c> stops the whole host by default — so a mistyped
    /// registration or a dependency that fails to build would turn "reminders are broken" into
    /// "the API is down". Resolution therefore happens inside the guard, not before it.
    /// </remarks>
    [Fact]
    public async Task A_job_that_cannot_be_constructed_does_not_bring_down_the_host()
    {
        var services = new ServiceCollection();
        services.AddScoped<IScheduledJob, UnconstructableJob>();

        var service = new ScheduledWorkService(
            services.BuildServiceProvider().GetRequiredService<IServiceScopeFactory>(),
            NullLogger<ScheduledWorkService>.Instance);

        // The assertion is that this returns at all rather than throwing.
        await service.RunOnceAsync(default);
    }

    private sealed class UnconstructableJob : IScheduledJob
    {
        public UnconstructableJob() => throw new InvalidOperationException("Cannot be built.");
        public string Name => "unconstructable";
        public Task RunAsync(CancellationToken ct) => Task.CompletedTask;
    }

    [Fact]
    public void The_first_pass_does_not_run_during_startup()
    {
        // Jobs that fire the instant the process starts run while migrations may still be applying,
        // and turn a crash-restart loop into a job loop. Nothing scheduled here is urgent to the
        // minute, so the delay costs nothing.
        Assert.True(ScheduledWorkService.StartupDelay > TimeSpan.Zero);
        Assert.True(ScheduledWorkService.Interval >= TimeSpan.FromMinutes(1),
            "A sub-minute interval means the loop is being used as a poller. Reconsider the job.");
    }
}
