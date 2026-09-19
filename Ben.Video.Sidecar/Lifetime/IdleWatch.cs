using System.Diagnostics;

namespace Ben.Video.Sidecar.Lifetime;

/// <summary>
/// When this sidecar was last asked for anything.
/// </summary>
/// <remarks>
/// A monotonic clock, not the wall clock: the machine sleeping, waking or having its time changed
/// must not read as fifteen quiet minutes and stop a sidecar somebody is about to render with.
/// </remarks>
public sealed class IdleWatch
{
    private readonly Stopwatch _sinceLastRequest = Stopwatch.StartNew();
    private readonly Lock _lock = new();

    /// <summary>Called on every request, whatever it was and whether or not it succeeded.</summary>
    public void Touch()
    {
        lock (_lock) _sinceLastRequest.Restart();
    }

    /// <summary>How long since anything last asked for anything.</summary>
    public TimeSpan IdleFor
    {
        get { lock (_lock) return _sinceLastRequest.Elapsed; }
    }
}
