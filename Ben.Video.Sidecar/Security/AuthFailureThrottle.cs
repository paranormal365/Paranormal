using System.Collections.Concurrent;

namespace Ben.Video.Sidecar.Security;

/// <summary>
/// Simple in-memory rate limiter for authentication failures (item #38 phase E threat T1/T6) —
/// caps how fast a caller can brute-force the pairing token. Deliberately not tied to caller
/// identity beyond "the sidecar as a whole" — this is a single-user local process, not a
/// multi-tenant server, so one global counter is the right granularity: repeated failures from
/// anywhere are equally suspicious.
/// </summary>
public sealed class AuthFailureThrottle
{
    private readonly int _maxFailuresPerWindow;
    private readonly TimeSpan _window;
    private readonly ConcurrentQueue<DateTimeOffset> _recentFailures = new();

    public AuthFailureThrottle(int maxFailuresPerWindow = 10, TimeSpan? window = null)
    {
        _maxFailuresPerWindow = maxFailuresPerWindow;
        _window = window ?? TimeSpan.FromMinutes(1);
    }

    /// <summary>Records a failed auth attempt.</summary>
    public void RecordFailure() => _recentFailures.Enqueue(DateTimeOffset.UtcNow);

    /// <summary>True when the recent-failure rate has tripped the throttle and the caller should
    /// be told to slow down (429) instead of just 401.</summary>
    public bool IsThrottled()
    {
        var cutoff = DateTimeOffset.UtcNow - _window;
        while (_recentFailures.TryPeek(out var oldest) && oldest < cutoff)
            _recentFailures.TryDequeue(out _);

        return _recentFailures.Count >= _maxFailuresPerWindow;
    }
}
