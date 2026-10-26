using System.Collections.Concurrent;
using System.Threading.Channels;

namespace Ben.Data.WebApi.Services.Audio.Scanning;

/// <summary>
/// Wakes the scan worker when a job is queued, and stops a running one when it is canceled
/// (phase 6, 10/26/2026).
/// </summary>
/// <remarks>
/// The database is the queue; this is only the doorbell. A wake-up for a job that has already run,
/// or one lost to a restart, costs nothing: the worker always takes the oldest queued job from the
/// table, and looks again every 30 seconds whether or not anything rang.
/// </remarks>
public sealed class EvpScanQueue
{
    private readonly Channel<Guid> _wakeUps = Channel.CreateUnbounded<Guid>();
    private readonly ConcurrentDictionary<Guid, CancellationTokenSource> _running = new();

    /// <summary>Tells the worker there is a job to take.</summary>
    public void Signal(Guid jobId) => _wakeUps.Writer.TryWrite(jobId);

    /// <summary>Waits for a wake-up, or for <paramref name="atMost"/> to pass.</summary>
    public async Task WaitAsync(TimeSpan atMost, CancellationToken stopping)
    {
        using var wait = CancellationTokenSource.CreateLinkedTokenSource(stopping);
        wait.CancelAfter(atMost);
        try { await _wakeUps.Reader.ReadAsync(wait.Token); }
        catch (OperationCanceledException) when (!stopping.IsCancellationRequested) { }
        // Several wake-ups for one pass are one wake-up.
        while (_wakeUps.Reader.TryRead(out _)) { }
    }

    /// <summary>The token a running job is canceled through; <see cref="Done"/> releases it.</summary>
    public CancellationToken Running(Guid jobId, CancellationToken stopping)
    {
        var cts = CancellationTokenSource.CreateLinkedTokenSource(stopping);
        _running[jobId] = cts;
        return cts.Token;
    }

    public void Done(Guid jobId)
    {
        if (_running.TryRemove(jobId, out var cts)) cts.Dispose();
    }

    /// <summary>Stops the job if it is running in this process. The database flag covers the rest.</summary>
    public void Cancel(Guid jobId)
    {
        if (!_running.TryGetValue(jobId, out var cts)) return;
        try { cts.Cancel(); }
        catch (ObjectDisposedException) { /* it finished as the cancel arrived */ }
    }
}
