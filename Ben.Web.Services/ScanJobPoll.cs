using Ben.Service.Models.Entities;

namespace Ben.Web.Services;

/// <summary>One look at a background scan (phase 6).</summary>
/// <param name="Job">The scan as it stands; null when <paramref name="Unchanged"/> or on a failure.</param>
/// <param name="ETag">Send this with the next look.</param>
/// <param name="Unchanged">Nothing has moved since the last look.</param>
/// <param name="Error">Why the look failed. The scan itself may well be fine.</param>
public sealed record ScanJobPoll(EvpScanJobRecord? Job, string? ETag, bool Unchanged, string? Error);
