namespace Ben.Web.Tests.Blocks;

/// <summary>A clock that moves only when told, so code that waits can be tested without waiting.</summary>
public sealed class ManualTimeProvider : TimeProvider
{
    private readonly List<ManualTimer> _timers = [];
    private DateTimeOffset _now = new(2026, 9, 14, 12, 0, 0, TimeSpan.Zero);

    public override DateTimeOffset GetUtcNow() => _now;

    public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
    {
        var timer = new ManualTimer(this, callback, state);
        timer.Change(dueTime, period);
        lock (_timers) _timers.Add(timer);
        return timer;
    }

    /// <summary>Moves the clock on and fires every timer that has come due.</summary>
    public void Advance(TimeSpan by)
    {
        _now += by;
        List<ManualTimer> due;
        lock (_timers) due = _timers.Where(t => t.DueAt is { } at && at <= _now).ToList();
        foreach (var timer in due) timer.Fire();
    }

    /// <summary>How many timers are waiting.</summary>
    public int Pending { get { lock (_timers) return _timers.Count(t => t.DueAt is not null); } }

    private sealed class ManualTimer(ManualTimeProvider clock, TimerCallback callback, object? state) : ITimer
    {
        public DateTimeOffset? DueAt { get; private set; }

        public bool Change(TimeSpan dueTime, TimeSpan period)
        {
            DueAt = dueTime == Timeout.InfiniteTimeSpan ? null : clock._now + dueTime;
            return true;
        }

        public void Fire()
        {
            DueAt = null;
            callback(state);
        }

        public void Dispose() { DueAt = null; lock (clock._timers) clock._timers.Remove(this); }
        public ValueTask DisposeAsync() { Dispose(); return ValueTask.CompletedTask; }
    }
}
