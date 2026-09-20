namespace Ben.Video.Sidecar.Lifetime;

/// <summary>What a request to stop the sidecar should do.</summary>
public enum ShutdownDecision
{
    /// <summary>Stop, once the response has been written.</summary>
    Stop,

    /// <summary>Refuse: work is in progress and stopping would throw it away.</summary>
    RefuseWorkInProgress,
}

/// <summary>
/// Whether a request to stop the sidecar is honoured.
/// </summary>
/// <remarks>
/// The person turning the switch off cannot see what the sidecar is doing, and an export they
/// started five minutes ago looks exactly like an idle process from the outside. So a stop with a
/// job running is refused and the caller is told how many, rather than silently discarding the
/// work - <b>unless</b> the caller asks again meaning it, which is the person having been told and
/// deciding anyway. That second answer is theirs to give, not ours to override.
/// </remarks>
public static class ShutdownPolicy
{
    public static ShutdownDecision Decide(int activeJobs, bool force) =>
        activeJobs > 0 && !force ? ShutdownDecision.RefuseWorkInProgress : ShutdownDecision.Stop;
}
