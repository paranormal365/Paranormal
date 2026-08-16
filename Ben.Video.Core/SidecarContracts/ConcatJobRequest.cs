namespace Ben.Video.Core.SidecarContracts;

/// <summary>
/// Concatenate previously-retained segments, in order, with <c>-c copy</c> — item #70 phase 160.
///
/// <para>Takes <b>ids of segments the sidecar already holds</b> (via <c>Retain = true</c> on the
/// segment jobs that produced them) rather than uploaded files. That's the whole point of dual
/// residency: the inputs are already next to the ffmpeg binary, so a concat moves no bytes across
/// the loopback at all.</para>
///
/// <para>Every id must still be retained when the job starts. If any is missing — evicted by the
/// LRU, deleted, or lost to a sidecar restart — the request fails with the full missing list so the
/// caller can re-render precisely those segments instead of guessing or redoing all of them.</para>
/// </summary>
public sealed record ConcatJobRequest(IReadOnlyList<Guid> SegmentIds);

/// <summary>Body of the 409 a concat gets when some inputs are no longer retained.</summary>
public sealed record MissingSegmentsInfo(IReadOnlyList<Guid> MissingSegmentIds);
