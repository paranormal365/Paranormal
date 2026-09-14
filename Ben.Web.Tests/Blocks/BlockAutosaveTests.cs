using Ben.Web.Website.Library.Kit.Blocks;
using Xunit;

namespace Ben.Web.Tests.Blocks;

public sealed class BlockAutosaveTests
{
    private readonly ManualTimeProvider _clock = new();
    private readonly List<TaskCompletionSource<AutosaveOutcome>> _saves = [];
    private int _changedCount;

    private BlockAutosave Build(TimeSpan? idle = null) => new(
        _ => { var tcs = new TaskCompletionSource<AutosaveOutcome>(TaskCreationOptions.RunContinuationsAsynchronously); lock (_saves) _saves.Add(tcs); return tcs.Task; },
        idle ?? TimeSpan.FromSeconds(60),
        dispatch: work => work(),
        changed: () => Interlocked.Increment(ref _changedCount),
        _clock);

    /// <summary>Waits until the autosave has asked to save <paramref name="count"/> times — the event under test, not a clock.</summary>
    private async Task SavesAskedAsync(int count)
    {
        for (var i = 0; i < 500; i++)
        {
            lock (_saves) if (_saves.Count >= count) return;
            await Task.Yield();
            await Task.Delay(1);
        }
        lock (_saves) Assert.Fail($"expected {count} save(s), saw {_saves.Count}");
    }

    /// <summary>Waits until the timer the last change started is actually waiting on the clock.</summary>
    private async Task TimerWaitingAsync()
    {
        for (var i = 0; i < 500 && _clock.Pending == 0; i++) await Task.Delay(1);
        Assert.True(_clock.Pending > 0, "no save was scheduled");
    }

    [Fact]
    public async Task Changes_save_once_a_minute_after_the_last_of_them()
    {
        var autosave = Build();
        autosave.Touch();
        await TimerWaitingAsync();
        _clock.Advance(TimeSpan.FromSeconds(30));
        autosave.Touch();                      // restarts the wait
        await TimerWaitingAsync();
        _clock.Advance(TimeSpan.FromSeconds(45));
        lock (_saves) Assert.Empty(_saves);   // 45 s after the last change: not yet

        _clock.Advance(TimeSpan.FromSeconds(15));
        await SavesAskedAsync(1);
        _saves[0].SetResult(AutosaveOutcome.Saved);

        await autosave.FlushAsync();
        Assert.Equal(AutosaveState.Clean, autosave.State);
        Assert.False(autosave.HasUnsavedChanges);
        Assert.NotNull(autosave.LastSavedAt);
        lock (_saves) Assert.Single(_saves);
    }

    [Fact]
    public async Task A_change_reported_as_the_save_begins_is_saved_by_that_save()
    {
        // The text editor's last keystrokes are only reported when the save asks for them. They go into that save, so the
        // page must come out clean — not "Unsaved changes" with nothing left to save.
        BlockAutosave? autosave = null;
        autosave = new BlockAutosave(
            _ => { var tcs = new TaskCompletionSource<AutosaveOutcome>(TaskCreationOptions.RunContinuationsAsynchronously); lock (_saves) _saves.Add(tcs); return tcs.Task; },
            TimeSpan.FromSeconds(60), work => work(), () => Interlocked.Increment(ref _changedCount), _clock,
            collect: () => { autosave!.Touch(); return Task.CompletedTask; });

        var flush = autosave.FlushAsync();
        await SavesAskedAsync(1);
        _saves[0].SetResult(AutosaveOutcome.Saved);

        Assert.True(await flush);
        Assert.Equal(AutosaveState.Clean, autosave.State);
        Assert.False(autosave.HasUnsavedChanges);
    }

    [Fact]
    public async Task Collecting_nothing_new_saves_nothing()
    {
        var collected = 0;
        var autosave = new BlockAutosave(
            _ => { lock (_saves) _saves.Add(new TaskCompletionSource<AutosaveOutcome>()); return Task.FromResult(AutosaveOutcome.Saved); },
            TimeSpan.FromSeconds(60), work => work(), () => { }, _clock,
            collect: () => { collected++; return Task.CompletedTask; });

        Assert.True(await autosave.FlushAsync());
        Assert.Equal(1, collected);
        lock (_saves) Assert.Empty(_saves);
    }

    [Fact]
    public async Task Flushing_with_nothing_changed_saves_nothing()
    {
        var autosave = Build();
        Assert.True(await autosave.FlushAsync());
        lock (_saves) Assert.Empty(_saves);
    }

    [Fact]
    public async Task A_failed_save_keeps_the_work_unsaved_and_a_later_flush_tries_again()
    {
        var autosave = Build();
        autosave.Touch();
        var first = autosave.FlushAsync();
        await SavesAskedAsync(1);
        _saves[0].SetResult(new AutosaveOutcome(AutosaveResult.Failed, "Could not reach the site."));
        Assert.False(await first);
        Assert.Equal(AutosaveState.Failed, autosave.State);
        Assert.True(autosave.HasUnsavedChanges);
        Assert.Equal("Could not reach the site.", autosave.Message);

        var second = autosave.FlushAsync();
        await SavesAskedAsync(2);
        _saves[1].SetResult(AutosaveOutcome.Saved);
        Assert.True(await second);
        Assert.Equal(AutosaveState.Clean, autosave.State);
    }

    [Fact]
    public async Task A_conflict_stops_saving_for_good()
    {
        var autosave = Build();
        autosave.Touch();
        var flush = autosave.FlushAsync();
        await SavesAskedAsync(1);
        _saves[0].SetResult(new AutosaveOutcome(AutosaveResult.Conflict, "Newer work somewhere else."));
        Assert.False(await flush);
        Assert.Equal(AutosaveState.Conflict, autosave.State);

        autosave.Touch();
        Assert.False(await autosave.FlushAsync());
        lock (_saves) Assert.Single(_saves);
    }

    [Fact]
    public async Task A_change_made_while_saving_is_saved_by_the_next_save()
    {
        var autosave = Build();
        autosave.Touch();
        var flush = autosave.FlushAsync();
        await SavesAskedAsync(1);

        autosave.Touch();                          // typed while the save was on its way
        _saves[0].SetResult(AutosaveOutcome.Saved);
        await flush;
        Assert.True(autosave.HasUnsavedChanges);
        Assert.Equal(AutosaveState.Dirty, autosave.State);

        var again = autosave.FlushAsync();
        await SavesAskedAsync(2);
        _saves[1].SetResult(AutosaveOutcome.Saved);
        Assert.True(await again);
    }
}
