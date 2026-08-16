namespace Ben.Video.Core.SidecarContracts;

/// <summary>Lifecycle state of a sidecar job. <see cref="Running"/> covers both queued-behind-
/// <c>SidecarOptions.MaxConcurrentJobs</c> and actually-executing — the browser only needs
/// "not done yet" to keep polling; <see cref="JobStatusInfo.ProgressPercent"/> stays 0 until
/// ffmpeg actually starts emitting progress lines.</summary>
public enum JobState { Running, Succeeded, Failed }

/// <summary>
/// Response body for <c>GET /v1/jobs/{id}</c> — polled by the browser (phase 123) roughly every
/// few hundred milliseconds while a job runs. Polling, not Server-Sent Events: Blazor
/// WebAssembly's <c>HttpClient</c> buffers the full response before any of it is observable
/// unless the browser-only <c>SetBrowserResponseStreamingEnabled</c> toggle is set, which lives in
/// a WASM-specific assembly <c>Ben.Video.Editor</c> (a plain <c>net10.0</c> Razor class library,
/// not itself WASM-targeted) cannot reference — so a short poll loop is the simpler mechanism that
/// actually compiles and works under either Blazor hosting model.
/// </summary>
public sealed record JobStatusInfo(
    Guid JobId,
    JobState State,
    int ProgressPercent,
    string? ErrorMessage,
    long? ResultSizeBytes,
    /// <summary>
    /// Item #70 phase 160 — set when the job was submitted with <c>Retain = true</c> and the
    /// sidecar kept its own copy. Null for every other job.
    ///
    /// <para>This is an <b>additive response field</b>, which is only safe because phase 158 gave
    /// the browser <see cref="SidecarJsonOptions.LenientResponses"/>: an older browser build
    /// parsing a newer sidecar's status must ignore fields it doesn't know rather than throw.</para>
    /// </summary>
    Guid? RetainedSegmentId = null);
