namespace Ben.Data.Common.Enums;

/// <summary>Where a background EVP scan, or one recording in it, stands (phase 6, 10/26/2026).</summary>
public enum EvpScanJobStatus
{
    /// <summary>Waiting its turn.</summary>
    Queued = 0,

    /// <summary>Being read now.</summary>
    Running = 1,

    /// <summary>Finished; its candidates are in the review queue.</summary>
    Done = 2,

    /// <summary>Stopped by a problem, which <c>Error</c> says in a sentence.</summary>
    Failed = 3,

    /// <summary>Stopped because somebody asked. Recordings already finished keep their candidates.</summary>
    Canceled = 4,
}
