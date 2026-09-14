namespace Ben.Web.Website.Library.Kit.Blocks;

/// <summary>What one save came to.</summary>
public enum AutosaveResult
{
    Saved,

    /// <summary>Did not save; the work is still unsaved and saving again may succeed.</summary>
    Failed,

    /// <summary>Did not save because it would overwrite newer work. Saving stops until the page is reloaded.</summary>
    Conflict,
}

public sealed record AutosaveOutcome(AutosaveResult Result, string? Message = null)
{
    public static AutosaveOutcome Saved { get; } = new(AutosaveResult.Saved);
}

public enum AutosaveState
{
    /// <summary>Nothing changed since the page was loaded or last saved.</summary>
    Clean,
    Dirty,
    Saving,
    Failed,
    Conflict,
}

/// <summary>
/// Saves a page a while after the person stops changing it, one save at a time.
/// </summary>
/// <remarks>
/// <para>Beta feedback, 2026-09-14: a research page saves itself about a minute after the last change. The shape is the
/// video editor's project store: every change restarts the wait; a change made during a save is saved by the next one; a
/// failure keeps the work marked unsaved; a conflict — newer work somewhere else — stops saving altogether, because
/// saving again would overwrite it.</para>
/// <para>Saves run through <c>dispatch</c> — a component's <c>InvokeAsync</c> — so the save sees the component's state on
/// the renderer's own context, never from a timer's thread. Time comes from a <see cref="TimeProvider"/> so the waiting
/// is testable without waiting.</para>
/// </remarks>
public sealed class BlockAutosave : IAsyncDisposable
{
    private readonly Func<CancellationToken, Task<AutosaveOutcome>> _save;
    private readonly Func<Func<Task>, Task> _dispatch;
    private readonly Action _changed;
    private readonly TimeSpan _idle;
    private readonly TimeProvider _clock;
    private readonly SemaphoreSlim _one = new(1, 1);
    private CancellationTokenSource? _wait;
    private int _changeCount;
    private int _savedChangeCount;
    private bool _disposed;

    public BlockAutosave(Func<CancellationToken, Task<AutosaveOutcome>> save, TimeSpan idle,
        Func<Func<Task>, Task> dispatch, Action changed, TimeProvider? clock = null)
    {
        _save = save;
        _idle = idle;
        _dispatch = dispatch;
        _changed = changed;
        _clock = clock ?? TimeProvider.System;
    }

    public AutosaveState State { get; private set; } = AutosaveState.Clean;

    public DateTimeOffset? LastSavedAt { get; private set; }

    /// <summary>The last failure's or conflict's sentence.</summary>
    public string? Message { get; private set; }

    /// <summary>True while there is work that is not saved.</summary>
    public bool HasUnsavedChanges => _changeCount != _savedChangeCount;

    /// <summary>Something changed: mark it unsaved and restart the wait.</summary>
    public void Touch()
    {
        if (_disposed || State == AutosaveState.Conflict) return;
        _changeCount++;
        if (State != AutosaveState.Saving) State = AutosaveState.Dirty;
        _changed();
        RestartWait();
    }

    /// <summary>Saves now if anything is unsaved, and waits for it. True when nothing unsaved remains.</summary>
    public async Task<bool> FlushAsync(CancellationToken ct = default)
    {
        _wait?.Cancel();
        if (State == AutosaveState.Conflict) return false;
        await SaveAsync(ct);
        return !HasUnsavedChanges;
    }

    private void RestartWait()
    {
        _wait?.Cancel();
        var wait = _wait = new CancellationTokenSource();
        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(_idle, _clock, wait.Token);
            }
            catch (OperationCanceledException)
            {
                return;
            }
            await _dispatch(() => SaveAsync(CancellationToken.None));
        });
    }

    private async Task SaveAsync(CancellationToken ct)
    {
        if (_disposed) return;
        await _one.WaitAsync(ct);
        try
        {
            if (!HasUnsavedChanges || State == AutosaveState.Conflict) return;

            var saving = _changeCount;
            State = AutosaveState.Saving;
            _changed();

            AutosaveOutcome outcome;
            try
            {
                outcome = await _save(ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                outcome = new AutosaveOutcome(AutosaveResult.Failed, "Could not reach the site to save.");
            }

            switch (outcome.Result)
            {
                case AutosaveResult.Saved:
                    _savedChangeCount = saving;
                    LastSavedAt = _clock.GetUtcNow();
                    Message = null;
                    State = HasUnsavedChanges ? AutosaveState.Dirty : AutosaveState.Clean;
                    if (HasUnsavedChanges) RestartWait();   // changed while saving: the next save takes it
                    break;
                case AutosaveResult.Conflict:
                    Message = outcome.Message;
                    State = AutosaveState.Conflict;
                    break;
                default:
                    Message = outcome.Message;
                    State = AutosaveState.Failed;
                    break;
            }
            _changed();
        }
        finally
        {
            _one.Release();
        }
    }

    public ValueTask DisposeAsync()
    {
        _disposed = true;
        _wait?.Cancel();
        return ValueTask.CompletedTask;
    }
}
