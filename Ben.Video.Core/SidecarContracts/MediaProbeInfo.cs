namespace Ben.Video.Core.SidecarContracts;

/// <summary>Request body for <c>POST /v1/probe</c> — item #70 phase 159. The source must already
/// be in the sidecar's cache (the browser does its usual HEAD/PUT first); this only names it.</summary>
public sealed record MediaProbeRequest(Guid ClipId, string SourceExt);

/// <summary>
/// Response body for <c>POST /v1/probe</c> — the sidecar's answer to what the browser's
/// <c>FfmpegService.GetMetadataAsync</c> computes in wasm, and deliberately the same three fields
/// so the two paths are interchangeable at the call site.
///
/// <para>ffprobe's raw JSON never crosses the wire: it's parsed server-side into these typed
/// fields. That keeps the browser from having to know anything about ffprobe's output schema (and
/// keeps a future ffprobe version change from becoming a browser-side parsing problem).</para>
/// </summary>
public sealed record MediaProbeInfo(double Duration, int Width, int Height);

/// <summary>Request body for <c>POST /v1/jobs/thumbnails</c> — item #70 phase 159. Mirrors the
/// arguments of the browser's <c>ExtractThumbnailsAsync(inputName, count, duration)</c>; the
/// output scale is fixed server-side (matching <c>ffmpegInterop.js</c>'s <c>scale=160:-1</c>)
/// rather than being a wire parameter, so there's nothing to validate or get wrong.</summary>
public sealed record ThumbnailJobRequest(Guid ClipId, string SourceExt, int Count, double Duration);

/// <summary>One file in a multi-file job result.</summary>
public sealed record ResultFileInfo(string Name, long SizeBytes);

/// <summary>
/// Manifest returned by <c>GET /v1/jobs/{id}/result</c> for job kinds that produce more than one
/// file (thumbnails) — item #70 phase 159. Single-file kinds (segment) keep streaming their one
/// file directly with no manifest, exactly as before, so nothing about the phase-123 contract
/// changes. Each entry's <see cref="ResultFileInfo.Name"/> is then fetched from
/// <c>GET /v1/jobs/{id}/result/{name}</c>.
/// </summary>
public sealed record ResultManifest(IReadOnlyList<ResultFileInfo> Files);
