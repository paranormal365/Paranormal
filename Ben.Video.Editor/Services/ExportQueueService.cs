using Ben.Video.Editor.Models;

namespace Ben.Video.Editor.Services;

/// <summary>
/// Scoped service that queues <see cref="ExportSettings"/> snapshots and runs them
/// sequentially through <see cref="ExportService"/>.
///
/// <para>Usage: call <see cref="EnqueueAsync"/> to add a job. The service automatically
/// starts the next pending job whenever the active one finishes (completed, failed,
/// or cancelled). UI components subscribe to <see cref="OnChanged"/> to re-render.</para>
///
/// <para>Each <see cref="ExportQueueEntry"/> transitions through:
/// <c>Queued → Running → Completed | Failed | Cancelled</c></para>
/// </summary>
public sealed class ExportQueueService : IDisposable
{
    private readonly ExportService _exporter;
    private readonly List<ExportQueueEntry> _entries = [];
    private bool _isProcessing;

    public ExportQueueService(ExportService exporter)
    {
        _exporter = exporter;
    }

    // ── State ─────────────────────────────────────────────────────────────────

    /// <summary>All entries — queued, running, and historical.</summary>
    public IReadOnlyList<ExportQueueEntry> Entries => _entries;

    /// <summary>Entries waiting to start.</summary>
    public IEnumerable<ExportQueueEntry> Pending
        => _entries.Where(e => e.State == QueueEntryState.Queued);

    /// <summary>The currently running entry, or <c>null</c>.</summary>
    public ExportQueueEntry? Active
        => _entries.FirstOrDefault(e => e.State == QueueEntryState.Running);

    /// <summary>Count of entries not yet finished (queued + running).</summary>
    public int ActiveCount => _entries.Count(e =>
        e.State is QueueEntryState.Queued or QueueEntryState.Running);

    /// <summary>
    /// Overall combined progress: 0–100 across all jobs that have been started.
    /// Completed and failed jobs count as 100. Running job contributes its current percent.
    /// </summary>
    public int CombinedPercent
    {
        get
        {
            var total = _entries.Count;
            if (total == 0) return 0;

            var sum = _entries.Sum(e => e.State switch
            {
                QueueEntryState.Completed => 100,
                QueueEntryState.Failed    => 100,
                QueueEntryState.Cancelled => 100,
                QueueEntryState.Running   => e.Job?.OverallPercent ?? 0,
                _                         => 0,
            });

            return sum / total;
        }
    }

    // ── Events ────────────────────────────────────────────────────────────────

    /// <summary>Fired whenever the queue or any entry state changes.</summary>
    public event Action? OnChanged;

    // ── Enqueue ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Add an export job to the queue with the given settings.
    /// Automatically starts processing if nothing is currently running.
    /// </summary>
    /// <param name="settings">Export settings snapshot (the queue takes ownership).</param>
    /// <param name="name">Display name shown in the queue panel. Defaults to the filename.</param>
    /// <remarks>
    /// Returns as soon as the entry is queued — it does not wait for the render to finish.
    /// Awaiting the whole <see cref="TryProcessNextAsync"/> chain here would mean any caller
    /// (e.g. the Export dialog's "Add to Queue" button) blocks until the entire ffmpeg render
    /// completes before getting control back, which defeats the point of a background queue:
    /// the UI can't close the dialog or show a "queued" state until the job is done. Errors
    /// during processing are still captured — <see cref="TryProcessNextAsync"/> catches them
    /// internally and reflects them on the entry's <see cref="ExportQueueEntry.State"/> /
    /// <see cref="ExportQueueEntry.ErrorMessage"/>, then calls <see cref="Notify"/> — so
    /// nothing is silently swallowed by not awaiting it here.
    /// </remarks>
    public Task EnqueueAsync(ExportSettings settings, string? name = null)
    {
        var entry = new ExportQueueEntry
        {
            Name     = name ?? settings.OutputFilename,
            Settings = settings,
        };
        _entries.Add(entry);
        Notify();
        _ = TryProcessNextAsync();
        return Task.CompletedTask;
    }

    // ── Cancel ────────────────────────────────────────────────────────────────

    /// <summary>Cancel a pending or running entry.</summary>
    public void Cancel(Guid entryId)
    {
        var entry = _entries.FirstOrDefault(e => e.Id == entryId);
        if (entry is null) return;

        if (entry.State == QueueEntryState.Queued)
        {
            entry.State = QueueEntryState.Cancelled;
            Notify();
        }
        else if (entry.State == QueueEntryState.Running)
        {
            entry.Job?.Cancel();
        }
    }

    // ── Clear ─────────────────────────────────────────────────────────────────

    /// <summary>Remove all finished entries (Completed, Failed, Cancelled).</summary>
    public void ClearFinished()
    {
        _entries.RemoveAll(e => e.State is
            QueueEntryState.Completed or
            QueueEntryState.Failed    or
            QueueEntryState.Cancelled);
        Notify();
    }

    // ── Internal processing ───────────────────────────────────────────────────

    private async Task TryProcessNextAsync()
    {
        if (_isProcessing) return;

        var next = _entries.FirstOrDefault(e => e.State == QueueEntryState.Queued);
        if (next is null) return;

        _isProcessing = true;
        next.State    = QueueEntryState.Running;
        next.StartedAt = DateTimeOffset.UtcNow;
        Notify();

        try
        {
            // Start the pipeline — ExportService sets CurrentJob synchronously before
            // the first internal await, so we can subscribe to OnProgress immediately.
            var exportTask = _exporter.ExportAsync(next.Settings);
            next.Job = _exporter.CurrentJob;   // grab ref before any await
            if (next.Job is not null)
                next.Job.OnProgress += Notify;
            Notify();

            await exportTask;

            if (next.Job is not null)
                next.Job.OnProgress -= Notify;

            next.State      = (next.Job?.State) switch
            {
                ExportJobState.Completed => QueueEntryState.Completed,
                ExportJobState.Cancelled => QueueEntryState.Cancelled,
                _                        => QueueEntryState.Failed,
            };
            next.ErrorMessage = next.Job?.ErrorMessage;
            next.FinishedAt   = DateTimeOffset.UtcNow;
        }
        catch (Exception ex)
        {
            next.State        = QueueEntryState.Failed;
            next.ErrorMessage = ex.Message;
            next.FinishedAt   = DateTimeOffset.UtcNow;
        }
        finally
        {
            _isProcessing = false;
            Notify();
        }

        // Start the next pending job
        await TryProcessNextAsync();
    }

    private void Notify() => OnChanged?.Invoke();

    public void Dispose() { /* no unmanaged resources */ }
}

/// <summary>A single entry in the export queue.</summary>
public sealed class ExportQueueEntry
{
    public Guid            Id            { get; } = Guid.NewGuid();
    public string          Name          { get; set; } = "Export";
    public ExportSettings  Settings      { get; init; } = new();
    public QueueEntryState State         { get; internal set; } = QueueEntryState.Queued;
    public ExportJob?      Job           { get; internal set; }
    public string?         ErrorMessage  { get; internal set; }
    public DateTimeOffset? StartedAt     { get; internal set; }
    public DateTimeOffset? FinishedAt    { get; internal set; }

    public TimeSpan Elapsed => StartedAt is null
        ? TimeSpan.Zero
        : (FinishedAt ?? DateTimeOffset.UtcNow) - StartedAt.Value;

    public string ElapsedDisplay => Elapsed.TotalSeconds < 60
        ? $"{(int)Elapsed.TotalSeconds}s"
        : $"{(int)Elapsed.TotalMinutes}m {Elapsed.Seconds:D2}s";

    public int ProgressPercent => State switch
    {
        QueueEntryState.Running   => Job?.OverallPercent ?? 0,
        QueueEntryState.Completed => 100,
        QueueEntryState.Failed    => 100,
        QueueEntryState.Cancelled => 100,
        _                          => 0,
    };
}

public enum QueueEntryState { Queued, Running, Completed, Failed, Cancelled }
