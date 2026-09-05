using Ben.Video.Editor.Effects;
using Ben.Video.Editor.Models;
using Ben.Video.Editor.Models.Assets;
using Microsoft.Extensions.Options;
using Microsoft.JSInterop;
using Ben.Video.Core.SidecarContracts;

namespace Ben.Video.Editor.Services;

/// <summary>
/// Scoped service that orchestrates the full ffmpeg export pipeline:
///
///   1. Trim each video clip into an individual MEMFS segment (libx264, frame-accurate)
///   2. If Transitions enabled: build a filter_complex graph stitching segments with crossfades
///   3. If TextOverlays enabled: chain drawtext filters over the composited video
///   4. If AudioTracks enabled: amix all audio streams into a single output stream
///   5. Concat / composite final output
///   6. Trigger browser download then clean up MEMFS
///
/// Progress is reported through <see cref="ExportJob.OnProgress"/> so UI components
/// can re-render without polling.
/// </summary>
public sealed class ExportService : IAsyncDisposable
{
    private readonly FfmpegService          _ffmpeg;
    private readonly ClipStore             _clips;
    private readonly VideoEditorOptions    _options;
    private readonly ClipEffectRegistry    _effectRegistry;
    private readonly MotionKeyframeService _motion;
    private readonly SvgAnimationExporter  _svgExporter;
    private readonly RasterClipArtAnimationExporter _rasterClipArtExporter;
    private readonly OPFSService           _opfs;
    private readonly WatermarkService      _watermark;
    private readonly GoogleFontService     _googleFonts;
    private readonly Microsoft.JSInterop.IJSRuntime _js;
    private readonly NativeSidecarService  _nativeSidecar;
    private readonly NativeClipEncoder     _nativeClipEncoder;
    private readonly RemoteSegmentIndex     _remoteSegments;

    // Audit #4 — cached domInterop handle (see BlobDownloadAsync).
    private IJSObjectReference? _dom;

    private async Task<IJSObjectReference> DomAsync() =>
        _dom ??= await _js.InvokeAsync<IJSObjectReference>(
            "benImportEditorModule", "js/domInterop.js");
    private readonly SidecarExportAssembler _nativeExportAssembler;
    private readonly ErrorLogService       _errorLog;

    public ExportService(
        FfmpegService ffmpeg,
        ClipStore clips,
        IOptions<VideoEditorOptions> options,
        ClipEffectRegistry effectRegistry,
        MotionKeyframeService motion,
        SvgAnimationExporter svgExporter,
        RasterClipArtAnimationExporter rasterClipArtExporter,
        OPFSService opfs,
        WatermarkService watermark,
        GoogleFontService googleFonts,
        Microsoft.JSInterop.IJSRuntime js,
        NativeSidecarService nativeSidecar,
        NativeClipEncoder nativeClipEncoder,
        RemoteSegmentIndex remoteSegments,
        SidecarExportAssembler nativeExportAssembler,
        ErrorLogService errorLog)
    {
        _ffmpeg         = ffmpeg;
        _clips          = clips;
        _options        = options.Value;
        _effectRegistry = effectRegistry;
        _motion         = motion;
        _svgExporter    = svgExporter;
        _rasterClipArtExporter = rasterClipArtExporter;
        _opfs           = opfs;
        _errorLog       = errorLog;
        _watermark      = watermark;
        _googleFonts    = googleFonts;
        _js             = js;
        _nativeSidecar  = nativeSidecar;
        _nativeClipEncoder = nativeClipEncoder;
        _remoteSegments = remoteSegments;
        _nativeExportAssembler = nativeExportAssembler;
    }

    // â”€â”€ Active job â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

    public ExportJob? CurrentJob { get; private set; }

    // â”€â”€ Entry point â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

    /// <summary>
    /// Start a new export run. Returns the job immediately â€” progress is
    /// delivered via <see cref="ExportJob.OnProgress"/>.
    /// </summary>
    /// <param name="downloadToDisk">
    /// True (default): the normal export â€” the final file is downloaded through the browser save
    /// dialog and the MEMFS copy is deleted. False (item #36 phase 84 â€” the toolbar's "Preview"
    /// button, full-resolution/quality but never saved): the pipeline runs identically all the way
    /// through, but the last step creates a blob URL (<see cref="ExportJob.PreviewBlobUrl"/>)
    /// instead of downloading â€” no file ever touches the user's disk. Every phase before that
    /// (trim, transitions, overlays, audio mix, watermark, chapter embed) is the exact same code
    /// path either way, so a "preview the real result" render can never drift from what Export
    /// actually produces.
    /// </param>
    public async Task<ExportJob> ExportAsync(ExportSettings settings, bool downloadToDisk = true)
    {
        if (CurrentJob?.State == ExportJobState.Running)
            throw new InvalidOperationException("An export is already running.");

        var job = new ExportJob { Settings = settings };
        CurrentJob = job;

        try
        {
            await RunPipelineAsync(job, downloadToDisk);
        }
        catch (OperationCanceledException)
        {
            job.State        = ExportJobState.Cancelled;
            job.FinishedAt   = DateTimeOffset.UtcNow;
            job.PhaseLabel   = "Cancelled.";
            job.NotifyProgress();
        }
        catch (Exception ex)
        {
            job.State        = ExportJobState.Failed;
            job.ErrorMessage = ex.Message;
            job.FinishedAt   = DateTimeOffset.UtcNow;
            job.PhaseLabel   = $"Failed: {ex.Message}";
            job.NotifyProgress();
        }
        finally
        {
            // Audit #1 — one CTS per export; without this each run leaks its registrations for the
            // lifetime of the page. Safe here because the job is terminal by this point: any later
            // Cancel() call swallows the ObjectDisposedException by design (see ExportJob.Cancel).
            job.DisposeCancellation();
        }

        return job;
    }

    // â”€â”€ Pipeline â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

    private async Task RunPipelineAsync(ExportJob job, bool downloadToDisk = true)
    {
        job.State = ExportJobState.Running;

        var videoClips = _clips.PrimaryVideoTrack.VideoClips
                               .OrderBy(c => c.Order)
                               .ToList();
        var imageClips = _clips.AllImageClips
                               .OrderBy(c => c.TimelinePosition)
                               .ToList();

        if (videoClips.Count == 0 && imageClips.Count == 0)
            throw new InvalidOperationException("No clips on the timeline to export.");

        var s         = job.Settings;
        var tempFiles = new List<string>();

        try
        {
            // ── Phase 0: Cross-track transitions — pre-render the merged crossfade
            // segment for each one BEFORE the normal per-clip trim pass, so the "from"
            // clip's own segment (produced by TrimSegmentsAsync below) is transparently
            // replaced by the merged one. This is also how a secondary video track's
            // clip becomes part of the export output at all — see ApplyCrossTrackTransitionsAsync.
            var crossTrackOverrides = await ApplyCrossTrackTransitionsAsync(job, s, tempFiles);
            ThrowIfCancelled(job);

            // ── Phase 1: Render all timeline segments (video + image) in order ─
            var videoSegments = await TrimSegmentsAsync(job, videoClips, s, tempFiles, crossTrackOverrides);
            ThrowIfCancelled(job);

            List<string> imageSegments = [];
            if (imageClips.Count > 0)
            {
                imageSegments = await RenderImageSegmentsAsync(job, imageClips, s, tempFiles);
                ThrowIfCancelled(job);
            }

            // Merge video and image segments sorted by their clip's TimelinePosition. Durations
            // are carried alongside in the same order — BuildXfadeFilterComplex needs each
            // segment's real encoded length (not a hardcoded assumption) to compute correct
            // chained xfade offsets. VideoClip uses EffectiveDuration (trim ÷ speed — the real
            // wall-clock length of the encoded segment); ImageClip mirrors the same
            // "> 0 ? : 5.0" fallback RenderImageSegmentsAsync uses when writing that segment.
            var orderedSegments = videoClips
                .Select((c, i) => (pos: c.TimelinePosition, seg: videoSegments[i],
                                    dur: c.EffectiveDuration > 0 ? c.EffectiveDuration : c.Duration))
                .Concat(imageClips.Select((c, i) => (pos: c.TimelinePosition, seg: imageSegments[i],
                                                      dur: c.Duration > 0 ? c.Duration : 5.0)))
                .OrderBy(x => x.pos)
                .ToList();
            var allSegments         = orderedSegments.Select(x => x.seg).ToList();
            var allSegmentDurations = orderedSegments.Select(x => x.dur).ToList();

            // ── Phase 2: Build output via filtergraph or simple concat ───────
            // Same-track transitions only — cross-track ones were already baked into
            // their "from" clip's segment above and must not also be positionally
            // matched here (BuildXfadeFilterComplex assumes transitions[i] pairs with
            // segments[i], which a mixed-in cross-track transition would misalign).
            string composited;
            // Not gated on _options.Transitions: the flag decides whether a person can ADD a
            // transition on this host, and a project carries its own. Gating the render here meant
            // a project made on the site, opened on a host with the flag off, silently exported
            // hard cuts (2026-09-05 audit, transitions-15).
            var hasXfadeTransitions = _clips.AllTransitions.Any(t => !IsCrossTrack(t));

            // Item #70 phase 162 — try to run concat AND the audio mix as one sidecar job. When it
            // engages, the audio mix moves EARLIER than its usual position in this pipeline; that
            // is safe because every overlay pass stream-copies audio through untouched
            // (-map 0:a? -c:a copy via AudioPassthroughArgs), which was audited before this phase
            // was built. Any failure falls back to the unchanged pipeline below, using the
            // segments still sitting in MEMFS (dual residency) — no re-render, no rework.
            var assembled = await TryNativeAssembleAsync(job, allSegments, hasXfadeTransitions, s, tempFiles);

            if (assembled is not null)
            {
                composited = assembled;
                ThrowIfCancelled(job);
            }
            else if (hasXfadeTransitions)
            {
                composited = await ApplyTransitionsAsync(job, allSegments, allSegmentDurations, s, tempFiles);
                ThrowIfCancelled(job);
            }
            else
            {
                composited = await ConcatSegmentsAsync(job, allSegments, s, tempFiles);
                ThrowIfCancelled(job);
            }

            // â”€â”€ Phase 3: Text overlays â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
            // Same rule as transitions above: a title in the project is rendered whatever this
            // host lets people create (2026-09-05 audit, titles-11). Callouts below already work
            // this way, which is what made the inconsistency obvious.
            if (_clips.AllTextOverlays.Any())
            {
                composited = await ApplyTextOverlaysAsync(job, composited, s, tempFiles);
                ThrowIfCancelled(job);
            }

            // ── Phase 3b: Callout clips (always applied, independent of TextOverlays flag) ───
            if (_clips.AllCalloutClips.Any())
            {
                composited = await ApplyCalloutsAsync(job, composited, s, tempFiles);
                ThrowIfCancelled(job);
            }

            // ── Phase 3c: ClipArt overlay clips ────────────────────────────────
            if (_clips.AllClipArtClips.Any())
            {
                composited = await ApplyClipArtClipsAsync(job, composited, s, tempFiles);
                ThrowIfCancelled(job);
            }

            // â”€â”€ Phase 4: Audio mix â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
            // Skipped when the native assemble already mixed audio (item #70 phase 162) —
            // re-running it here would amix the standalone clips a SECOND time on top of the
            // already-mixed track, audibly doubling them.
            // The project's audio tracks are mixed whether or not this host offers the button
            // that creates them (2026-09-05 audit, F2/titles-11 class).
            if (_clips.AudioTracks.Any() && s.IncludeAudio && !_nativeAssembleMixedAudio)
            {
                composited = await MixAudioTracksAsync(job, composited, s, tempFiles);
                ThrowIfCancelled(job);
            }

            // â”€â”€ Phase 5: Download â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
            // â”€â”€ Phase 5: Embed chapters â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
            // s.EmbedChapters is the person's own choice in the export dialog, and the markers are
            // in the project; the host flag only decides whether this host can add one.
            var markers = _clips.Markers;
            if (s.EmbedChapters
                && markers.Count > 0
                && s.OutputFormat != "webm")
            {
                composited = await EmbedChaptersAsync(job, composited, markers, s, tempFiles);
                ThrowIfCancelled(job);
            }

            var outputName = $"{SanitiseFilename(s.OutputFilename)}.{s.OutputFormat}";
            await RenameAsync(composited, outputName);

            // ── Phase 4.5: Watermark (server-enforced, no user override) ──────
            var wmConfig = await _watermark.GetConfigAsync();
            if (wmConfig?.Enabled == true)
            {
                outputName = await ApplyWatermarkAsync(job, outputName, wmConfig, s, tempFiles);
                ThrowIfCancelled(job);
            }

            // Final sanity probe (backlog #29): a mid-pipeline pass that silently dropped the
            // video stream (e.g. an explicit -map that deselected it) exits 0, and every later
            // pass carries the video-less file forward. An export always starts from at least
            // one video/image clip, so the output must have a video stream — fail loudly here
            // instead of handing the user an audio-only file behind a "✓ Export complete".
            var probe = await _ffmpeg.GetMetadataAsync(outputName);
            if (probe.Width <= 0 || probe.Height <= 0)
                throw new InvalidOperationException(
                    "Export produced a file with no video track (a compositing pass dropped the "
                    + $"video stream — probe reported {probe.Width}×{probe.Height}). "
                    + "The file was not downloaded. Please report which overlays were on the timeline.");
            job.Duration = probe.Duration;

            var mime = MimeType(s.OutputFormat);
            var ext  = "." + s.OutputFormat;

            // Item #38 phase D: move the finished output from MEMFS into OPFS entirely JS-side
            // (no byte[] marshals into .NET) instead of retaining a full-size MEMFS copy through
            // the download/preview step below — this was the single largest remaining peak-memory
            // moment in the whole pipeline for a long export. Falls back to the pre-phase-D
            // direct-MEMFS path when OPFS isn't available/usable — Export must never become newly
            // OPFS-dependent for its core job, matching every other OPFS touchpoint in this app.
            var sizeBytes = await _ffmpeg.ExportToOpfsAsync(outputName, job.Id, ext);

            if (sizeBytes >= 0)
            {
                job.OutputSizeBytes = sizeBytes;
                if (downloadToDisk)
                {
                    Advance(job, 95, "Preparing download…");
                    // Item #59-#65 flakiness investigation, phase 144 — blobUrl is now backed by
                    // an in-memory Blob (opfsInterop.js's opfsExportsReadAsBlobUrl), not a live
                    // reference to the OPFS file itself, so deleting that file immediately below
                    // no longer risks 404ing this URL. DownloadBlobUrlAsync's own JS now owns
                    // revoking blobUrl (deferred ~30s) — no explicit revoke call here anymore.
                    var blobUrl = await _opfs.ReadExportAsBlobUrlAsync(job.Id, ext);
                    if (blobUrl is not null)
                    {
                        await _ffmpeg.DownloadBlobUrlAsync(blobUrl, outputName);
                    }
                    // Nothing else reads a completed export back out today (confirmed — see
                    // README-phase-119.md) — delete rather than accumulate unbounded OPFS growth.
                    await _opfs.DeleteExportAsync(job.Id, ext);
                }
                else
                {
                    Advance(job, 95, "Preparing preview…");
                    // Deliberately NOT deleted here — the full-quality preview popout keeps
                    // playing this blob URL and explicitly revokes it later via
                    // RevokePreviewUrlAsync (VideoEditor.razor, unchanged by this phase).
                    job.PreviewBlobUrl = await _opfs.ReadExportAsBlobUrlAsync(job.Id, ext);
                }
            }
            else
            {
                // OPFS unavailable/failed — outputName is still a valid MEMFS file (ExportToOpfsAsync
                // never deletes it unless the OPFS write already succeeded).
                if (downloadToDisk)
                {
                    Advance(job, 95, "Preparing download…");
                    await _ffmpeg.DownloadFileAsync(outputName, outputName, mime);
                    await _ffmpeg.DeleteFileAsync(outputName);
                }
                else
                {
                    Advance(job, 95, "Preparing preview…");
                    job.PreviewBlobUrl = await _ffmpeg.CreatePreviewUrlAsync(outputName, mime);
                    await _ffmpeg.DeleteFileAsync(outputName);
                }
            }

            job.State           = ExportJobState.Completed;
            job.FinishedAt      = DateTimeOffset.UtcNow;
            Advance(job, 100, $"Done! ({job.Elapsed.TotalSeconds:F1}s)");
        }
        finally
        {
            // Always clean up temp MEMFS files
            foreach (var f in tempFiles)
            {
                try { await _ffmpeg.DeleteFileAsync(f); } catch { }
            }
        }
    }

    // â”€â”€ Phase implementations â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

    private async Task<List<string>> TrimSegmentsAsync(
        ExportJob job, List<VideoClip> clips, ExportSettings s, List<string> tempFiles,
        Dictionary<Guid, string>? segmentOverrides = null)
    {
        var segments = new List<string>();
        var total    = clips.Count;

        for (var i = 0; i < total; i++)
        {
            ThrowIfCancelled(job);

            var clip    = clips[i];

            // A cross-track transition already produced a merged segment (this clip's own
            // solo portion + the crossfade + the other track's clip's solo tail) — use that
            // instead of trimming this clip normally, which would drop the transition/handoff.
            if (segmentOverrides is not null && segmentOverrides.TryGetValue(clip.Id, out var overrideSeg))
            {
                Advance(job, ProgressInRange(i, total, 0, 45), $"Using cross-track transition segment for: {clip.Name}");
                segments.Add(overrideSeg);
                job.CompletedPhases.Add($"Cross-track segment: {clip.Name}");
                continue;
            }

            var segName = $"seg_{i:D3}_{job.Id:N}.mp4";
            tempFiles.Add(segName);

            var pct = ProgressInRange(i, total, 0, 45);
            Advance(job, pct, $"Trimming clip {i + 1} of {total}: {clip.Name}");

            if (clip.MemFsName is null)
                throw new InvalidOperationException($"Clip '{clip.Name}' has no MEMFS source. Re-import the clip.");

            // Item #38 phase 124 — offload this one clip's trim/encode to the native sidecar
            // when it's connected, via the exact same ExportArgBuilders.BuildTrimArgs the wasm
            // path below calls, so the resulting segment is structurally identical either way.
            // TryEncodeVideoSegmentAsync never throws: a dead sidecar, an unsupported codec, or a
            // clip with no OPFS source just falls straight through to the unchanged wasm path —
            // this one clip renders in the browser instead, the export itself never fails or
            // reruns because of it.
            var nativeBytes = _nativeSidecar.IsConnected
                ? await _nativeClipEncoder.TryEncodeVideoSegmentAsync(clip, s, job.CancellationToken)
                : null;

            if (nativeBytes is not null)
            {
                // Phase 143: WriteFileWhenReadyAsync polls until the MAIN ffmpeg instance reaches
                // Ready, with no bound of its own, so it needs a ceiling of its own or a wedged
                // instance hangs the export forever. Audit #1 linked the job's real token in
                // alongside that ceiling, so this wait now ends on EITHER a genuine wedge (60s) or
                // the user cancelling — previously it could only end on the former.
                using var writeReadyCts = CancellationTokenSource.CreateLinkedTokenSource(job.CancellationToken);
                writeReadyCts.CancelAfter(TimeSpan.FromSeconds(60));
                await _ffmpeg.WriteFileWhenReadyAsync(segName, nativeBytes, writeReadyCts.Token);

                // Item #70 phase 162 — map this MEMFS name to the sidecar's retained copy so the
                // assemble job can use it as an input without re-uploading. The name is chosen
                // here, which is why NativeClipEncoder surfaces the id rather than registering it.
                if (_nativeClipEncoder.LastRetainedSegmentId is { } retainedId)
                    _remoteSegments.Register(segName, retainedId);
            }
            else
            {
                var start = clip.StartTrim;
                var end   = clip.EndTrim > clip.StartTrim ? clip.EndTrim : clip.Duration;

                // Build trim args â€” use selected codec, CRF or bitrate, per-clip speed, volume and effects
                var effectiveDuration = clip.EffectiveDuration > 0 ? clip.EffectiveDuration : clip.Duration;
                var volumeFilter = ExportArgBuilders.BuildVolumeAutomationFilter(clip, effectiveDuration);
                var appliedVf = ExportArgBuilders.BuildAppliedEffectsFilter(clip.AppliedEffects, _effectRegistry, effectiveDuration, clip.Speed);
                var args = BuildTrimArgs(clip.MemFsName, segName, start, end, clip.Speed, s, volumeFilter, clip.Effects, clip.MuteAudio,
                    sourceHasAudio: clip.HasAudio,
                    extraVf: string.IsNullOrEmpty(appliedVf) ? null : appliedVf);
                await _ffmpeg.ExecAsync(args, job.CancellationToken);
            }

            segments.Add(segName);
            job.CompletedPhases.Add($"Trimmed: {clip.Name}");
        }

        Advance(job, 45, "All clips trimmed.");
        return segments;
    }

    /// <summary>
    /// Pre-renders a merged crossfade segment for each cross-track <see cref="Transition"/>
    /// (one whose FromClipId and ToClipId live on different video tracks), keyed by the
    /// "from" clip's id so <see cref="TrimSegmentsAsync"/> can substitute it in place of that
    /// clip's normal solo trim. No-op (returns an empty map, zero extra ffmpeg calls) when
    /// Transitions is disabled or no cross-track transitions exist — every project without
    /// this feature behaves exactly as before.
    /// Only supports the "from" clip being on the primary video track (matching the
    /// documented/common case); a cross-track transition whose lower-track clip is itself on
    /// a secondary track is skipped.
    /// </summary>
    private async Task<Dictionary<Guid, string>> ApplyCrossTrackTransitionsAsync(
        ExportJob job, ExportSettings s, List<string> tempFiles)
    {
        var overrides = new Dictionary<Guid, string>();

        var crossTrack = _clips.AllTransitions.Where(IsCrossTrack).ToList();
        if (crossTrack.Count == 0) return overrides;

        Advance(job, 46, $"Applying {crossTrack.Count} cross-track transition(s)…");

        foreach (var transition in crossTrack)
        {
            var fromTrack = _clips.FindTrackOf(transition.FromClipId);
            var toTrack   = _clips.FindTrackOf(transition.ToClipId);
            if (fromTrack is null || toTrack is null) continue;
            if (fromTrack.Id != _clips.PrimaryVideoTrack.Id) continue;

            var fromClip = fromTrack.Items.OfType<VideoClip>().FirstOrDefault(c => c.Id == transition.FromClipId);
            var toClip   = toTrack.Items.OfType<VideoClip>().FirstOrDefault(c => c.Id == transition.ToClipId);
            if (fromClip?.MemFsName is null || toClip?.MemFsName is null) continue;

            var fromSeg = $"xfrom_{transition.Id:N}.mp4";
            var toSeg   = $"xto_{transition.Id:N}.mp4";
            tempFiles.Add(fromSeg);
            tempFiles.Add(toSeg);

            var (xvw, xvh) = ParseResolution(s.Resolution);

            var fromStart = fromClip.StartTrim;
            var fromEnd   = fromClip.EndTrim > fromClip.StartTrim ? fromClip.EndTrim : fromClip.Duration;
            var fromVol   = ExportArgBuilders.BuildVolumeAutomationFilter(fromClip, fromEnd - fromStart);
            await _ffmpeg.ExecAsync(ExportArgBuilders.BuildTrimArgs(
                fromClip.MemFsName, fromSeg, fromStart, fromEnd, fromClip.Speed, s,
                fromVol, fromClip.Effects, fromClip.MuteAudio,
                outputWidth: xvw, outputHeight: xvh,
                sourceHasAudio: fromClip.HasAudio), job.CancellationToken);

            var toStart = toClip.StartTrim;
            var toEnd   = toClip.EndTrim > toClip.StartTrim ? toClip.EndTrim : toClip.Duration;
            var toVol   = ExportArgBuilders.BuildVolumeAutomationFilter(toClip, toEnd - toStart);
            await _ffmpeg.ExecAsync(ExportArgBuilders.BuildTrimArgs(
                toClip.MemFsName, toSeg, toStart, toEnd, toClip.Speed, s,
                toVol, toClip.Effects, toClip.MuteAudio,
                outputWidth: xvw, outputHeight: xvh,
                sourceHasAudio: toClip.HasAudio), job.CancellationToken);

            var mergedName = $"xmerged_{transition.Id:N}.mp4";
            tempFiles.Add(mergedName);

            // Where, within fromClip's own rendered/trimmed segment, the overlap begins.
            var offset = transition.TimelinePosition - fromClip.TimelinePosition;
            var filter = ExportArgBuilders.BuildCrossTrackXfadeFilter(transition.Style, transition.Duration, offset);

            var args = new List<string> { "-i", fromSeg, "-i", toSeg, "-filter_complex", filter, "-map", "[vout]" };
            args.AddRange(AudioOutputArgs(s));
            args.AddRange(QualityArgs(s));
            args.AddRange(["-pix_fmt", s.PixelFormat, mergedName]);
            await _ffmpeg.ExecAsync([.. args], job.CancellationToken);

            // Item #38 phase D — fromSeg/toSeg are fully consumed now.
            if (tempFiles.Remove(fromSeg)) await _ffmpeg.DeleteFileAsync(fromSeg);
            if (tempFiles.Remove(toSeg)) await _ffmpeg.DeleteFileAsync(toSeg);

            overrides[fromClip.Id] = mergedName;
        }

        job.CompletedPhases.Add($"Cross-track transitions applied ({overrides.Count})");
        return overrides;
    }

    private bool IsCrossTrack(Transition t) =>
        _clips.FindTrackOf(t.FromClipId)?.Id != _clips.FindTrackOf(t.ToClipId)?.Id;

    private async Task<List<string>> RenderImageSegmentsAsync(
        ExportJob job, List<ImageClip> clips, ExportSettings s, List<string> tempFiles)
    {
        var segments = new List<string>();
        var total    = clips.Count;

        for (var i = 0; i < total; i++)
        {
            ThrowIfCancelled(job);

            var clip    = clips[i];
            var segName = $"img_{i:D3}_{job.Id:N}.mp4";
            tempFiles.Add(segName);

            var pct = ProgressInRange(i, total, 0, 45);
            Advance(job, pct, $"Rendering image {i + 1} of {total}: {clip.Name}");

            if (clip.MemFsName is null)
                throw new InvalidOperationException($"Image clip '{clip.Name}' has no MEMFS source. Re-import the file.");

            // Item #38 phase 124 — same native offload as TrimSegmentsAsync above, for image
            // clips. See that method's comment for the fall-through contract.
            var nativeBytes = _nativeSidecar.IsConnected
                ? await _nativeClipEncoder.TryEncodeImageSegmentAsync(clip, s, job.CancellationToken)
                : null;

            if (nativeBytes is not null)
            {
                using var writeReadyCts = new CancellationTokenSource(TimeSpan.FromSeconds(60)); // see TrimSegmentsAsync's own note
                await _ffmpeg.WriteFileWhenReadyAsync(segName, nativeBytes, writeReadyCts.Token);
            }
            else
            {
                var duration = clip.Duration > 0 ? clip.Duration : 5.0;
                var appliedVf = ExportArgBuilders.BuildAppliedEffectsFilter(
                    clip.AppliedEffects, _effectRegistry, duration);
                // Item #9 — scale/pad to the PROJECT's output resolution, not the image's own
                // source size. Passing clip.Width/clip.Height made the filter
                // "scale={imgW}:{imgH},pad={imgW}:{imgH}" — a no-op that left every image segment
                // at its native resolution, which is exactly the reported symptom. Every other
                // segment path in this file already derives its canvas from ParseResolution(s.Resolution);
                // this was the one that didn't.
                var (imgW, imgH) = ParseResolution(s.Resolution);
                var args     = ExportArgBuilders.BuildImageSegmentArgs(
                    clip.MemFsName, segName, duration, s,
                    outputWidth: imgW, outputHeight: imgH,
                    effects: clip.Effects,
                    extraVf: string.IsNullOrEmpty(appliedVf) ? null : appliedVf);

                await _ffmpeg.ExecAsync(args, job.CancellationToken);
            }

            segments.Add(segName);
            job.CompletedPhases.Add($"Rendered image: {clip.Name}");
        }

        return segments;
    }


    /// <summary>
    /// Item #70 phase 162 — runs the export's concat (and, when there are standalone audio clips,
    /// the amix) as a single sidecar job. Returns the MEMFS name of the assembled body, or null to
    /// fall back to the in-browser pipeline.
    ///
    /// <para>Sets <see cref="_nativeAssembleMixedAudio"/> so the later wasm mix phase knows to skip
    /// itself — mixing twice would amix the standalone clips on top of an already-mixed track and
    /// audibly double them.</para>
    /// </summary>
    private async Task<string?> TryNativeAssembleAsync(
        ExportJob job, List<string> allSegments, bool hasXfadeTransitions,
        ExportSettings s, List<string> tempFiles)
    {
        _nativeAssembleMixedAudio = false;

        var audioClips = s.IncludeAudio
            ? _clips.AudioTracks.SelectMany(t => t.AudioClips).OrderBy(a => a.TimelinePosition).ToList()
            : [];

        _remoteSegments.SyncInstance(_nativeSidecar.InstanceId);
        var decision = ExportAssembleGate.Decide(
            allSegments,
            hasXfadeTransitions,
            _nativeSidecar.HasCapability(SidecarCapabilities.ExportAssemble),
            _remoteSegments.TryGetRemoteId,
            audioClips.All(a => !string.IsNullOrEmpty(a.OpfsExt)));

        if (decision != ExportAssembleDecision.UseSidecar) return null;
        if (_remoteSegments.TryGetAll(allSegments) is not { } remoteIds) return null;
        if (NativeClipEncoder.ToExportQualityDto(s) is not { } quality) return null;

        // Build each clip's audio filter chain HERE rather than server-side: it derives from
        // volume automation, channel balance and fades, and duplicating that derivation in the
        // sidecar would create exactly the drift this arc has been removing.
        var audioSources = new List<SidecarExportAssembler.AudioSource>(audioClips.Count);
        foreach (var ac in audioClips)
        {
            if (ac.MemFsName is null || string.IsNullOrEmpty(ac.OpfsExt)) continue;
            var start = ac.StartTrim;
            var end   = ac.EndTrim > ac.StartTrim ? ac.EndTrim : ac.Duration;
            if (end - start <= 0) continue;

            var chain   = ExportArgBuilders.BuildAudioClipFilterChain(ac, end - start);
            var delayMs = (int)Math.Round(Math.Max(0, ac.TimelinePosition) * 1000.0);
            var full    = delayMs > 0 ? $"{chain},adelay={delayMs}:all=1" : chain;
            audioSources.Add(new SidecarExportAssembler.AudioSource(ac.Id, ac.OpfsExt!, start, end, full));
        }

        Advance(job, 50, audioSources.Count > 0
            ? $"Assembling natively (concat + {audioSources.Count} audio track(s))…"
            : "Assembling natively…");

        var progress = new Progress<int>(p => Advance(job, ProgressInRange(p, 100, 50, 88), "Assembling natively…"));
        var assembled = await _nativeExportAssembler.TryAssembleAsync(
            remoteIds, quality, audioSources, progress, job.CancellationToken);

        if (assembled is null) return null;

        tempFiles.Add(assembled);
        _nativeAssembleMixedAudio = audioSources.Count > 0;

        job.CompletedPhases.Add(audioSources.Count > 0
            ? $"Assembled natively (concat + audio mix, {audioSources.Count} track(s))"
            : "Assembled natively (concat)");
        Advance(job, 90, "Native assembly complete.");
        return assembled;
    }

    private bool _nativeAssembleMixedAudio;

    private async Task<string> ConcatSegmentsAsync(
        ExportJob job, List<string> segments, ExportSettings s, List<string> tempFiles)
    {
        Advance(job, 50, "Concatenating segments…");

        var outputName = $"concat_{job.Id:N}.mp4";
        tempFiles.Add(outputName);

        await _ffmpeg.ConcatClipsAsync([.. segments], outputName);

        // Item #38 phase D — the per-clip segments are consumed now; no reason to hold them in
        // MEMFS until the pipeline's final cleanup.
        foreach (var seg in segments)
            if (tempFiles.Remove(seg)) await _ffmpeg.DeleteFileAsync(seg);

        job.CompletedPhases.Add("Concatenated");
        Advance(job, 65, "Concat complete.");
        return outputName;
    }

    private async Task<string> ApplyTransitionsAsync(
        ExportJob job, List<string> segments, List<double> segmentDurations, ExportSettings s, List<string> tempFiles)
    {
        Advance(job, 50, "Applying transitions…");

        // Build xfade filter_complex for consecutive segment pairs. Cross-track transitions
        // are excluded — they were already baked into their "from" clip's segment by
        // ApplyCrossTrackTransitionsAsync, and BuildXfadeFilterComplex's positional
        // transitions[i]<->segments[i] pairing would misalign if one were mixed in here.
        var transitions = _clips.AllTransitions.Where(t => !IsCrossTrack(t)).OrderBy(t => t.Order).ToList();
        var outputName  = $"transitioned_{job.Id:N}.mp4";
        tempFiles.Add(outputName);

        // Build the filter_complex string
        var filterArgs = BuildXfadeFilterComplex(segments, segmentDurations, transitions);

        // Inputs
        var inputArgs = segments.SelectMany(s2 => new[] { "-i", s2 }).ToList();
        inputArgs.AddRange(["-filter_complex", filterArgs, "-map", "[vout]"]);
        inputArgs.AddRange(AudioOutputArgs(s));
        inputArgs.AddRange(QualityArgs(s));
        inputArgs.AddRange(["-pix_fmt", s.PixelFormat, outputName]);

        await _ffmpeg.ExecAsync([.. inputArgs], job.CancellationToken);

        // Item #38 phase D — same reasoning as ConcatSegmentsAsync.
        foreach (var seg in segments)
            if (tempFiles.Remove(seg)) await _ffmpeg.DeleteFileAsync(seg);

        job.CompletedPhases.Add("Transitions applied");
        Advance(job, 65, "Transitions applied.");
        return outputName;
    }

    // Every text overlay — static or animated — renders through the SVG rasterization pipeline
    // (backlog #16 phase 74). Font-family names resolve against the browser's own installed fonts at
    // render time — this is what makes font selection actually work, replacing the old ffmpeg-native
    // drawtext path, whose fontfile= pointed at a literal macOS path that ffmpeg.wasm's in-memory
    // filesystem never had a matching file for, on any OS.
    //
    // Static overlays render ONE PNG composited via a looped input, with fade-in/out expressed as
    // ffmpeg fade=…:alpha=1 filters — not duration×fps identical full-canvas frames, which was the
    // core memory pressure behind backlog #29's ffmpeg.wasm OOM crash. Only animated (motion-path)
    // overlays genuinely need per-frame PNGs, and their frames + the consumed intermediate video are
    // deleted from MEMFS immediately after each compositing pass instead of at end of export.
    private async Task<string> ApplyTextOverlaysAsync(
        ExportJob job, string inputName, ExportSettings s, List<string> tempFiles)
    {
        // LayerIndex (backlog #39), not TimelinePosition — matches the timeline UI's overlay
        // stacking ("everything added gets its own layer, each layer higher than any added
        // before it") within this type's own compositing pass. Cross-type ordering (text always
        // composites before callouts, before clip art — see the phase order in RunPipelineAsync)
        // is a separate, pre-existing pipeline characteristic not addressed by this ordering key.
        var overlays   = _clips.AllTextOverlays.OrderBy(o => o.LayerIndex).ToList();
        var composited = inputName;
        var (vw, vh)   = ParseResolution(s.Resolution);

        Advance(job, 68, $"Applying {overlays.Count} text overlay(s)...");

        var overlayIdx = 0;
        foreach (var overlay in overlays)
        {
            overlayIdx++;
            var outputName = $"text_svg_{job.Id:N}_{overlayIdx}.mp4";
            tempFiles.Add(outputName);

            var startT = overlay.TimelinePosition;
            var endT   = overlay.TimelinePosition + overlay.Duration;

            // Item #16, phase 116 — a Google Font selected in a fresh session (no prior page load
            // to have already injected its <link>) must be loaded before the SVG is rasterized, or
            // the browser silently falls back to its default font. No-op for a system font.
            await _googleFonts.EnsureLoadedAsync(overlay.FontFamily);

            if (!_motion.HasPath(overlay.Id))
            {
                // Static — single PNG, looped for the overlay's window; fades via alpha-fade filters.
                var png = $"svgtext_{overlay.Id:N}_static.png";
                var svg = TextOverlayRenderer.Render(overlay, vw, vh);
                await _ffmpeg.WriteFileFromBytesAsync(png, await _svgExporter.RenderFrameFromSvgAsync(svg, vw, vh));
                tempFiles.Add(png);

                var filter = ExportArgBuilders.BuildStaticOverlayFilter(
                    vw, vh, startT, endT, overlay.FadeInSeconds, overlay.FadeOutSeconds);

                await _ffmpeg.ExecAsync(
                [
                    "-i", composited,
                    "-loop", "1",
                    "-framerate", s.Fps.ToString("F2"),
                    "-t", endT.ToString("F3", System.Globalization.CultureInfo.InvariantCulture),
                    "-i", png,
                    "-filter_complex", filter,
                    "-map", "[out]",
                    ..AudioPassthroughArgs(s),
                    ..QualityArgs(s),
                    outputName,
                ], job.CancellationToken);

                if (tempFiles.Remove(png)) await _ffmpeg.DeleteFileAsync(png);
            }
            else
            {
                // Animated — one interpolated SVG per output frame; the fade envelope multiplies
                // into each frame's opacity alongside the motion path's own alpha.
                var frameCount = Math.Max(1, (int)Math.Round(overlay.Duration * s.Fps));
                var prefix     = $"svgtext_{overlay.Id:N}";
                var frameNames = new List<string>(frameCount);
                for (var i = 0; i < frameCount; i++)
                {
                    var elapsed  = ExportArgBuilders.ElapsedSeconds(i, s.Fps); // seconds into the overlay's own lifetime
                    var t        = overlay.TimelinePosition + elapsed; // absolute timeline seconds
                    var frame    = _motion.Evaluate(overlay.Id, t)
                                   ?? new MotionFrame(overlay.OverrideX ?? 0.5, overlay.OverrideY ?? 0.5, 1.0, 1.0);
                    var animated = ExportArgBuilders.ApplyMotionFrame(overlay, frame);
                    animated     = animated with { Opacity = animated.Opacity * overlay.ComputeFadeAlpha(elapsed) };

                    var frameSvg = TextOverlayRenderer.Render(animated, vw, vh);
                    var fname    = $"{prefix}_{i:D4}.png";
                    await _ffmpeg.WriteFileFromBytesAsync(fname, await _svgExporter.RenderFrameFromSvgAsync(frameSvg, vw, vh));
                    tempFiles.Add(fname);
                    frameNames.Add(fname);
                }

                var filter = $"[1:v]scale={vw}:{vh}[ov];[0:v][ov]overlay=0:0:enable='between(t,{startT:F3},{endT:F3})'[out]";

                await _ffmpeg.ExecAsync(
                [
                    "-i", composited,
                    "-framerate", s.Fps.ToString("F2"),
                    "-i", $"{prefix}_%04d.png",
                    "-filter_complex", filter,
                    "-map", "[out]",
                    ..AudioPassthroughArgs(s),
                    ..QualityArgs(s),
                    outputName,
                ], job.CancellationToken);

                foreach (var fname in frameNames)
                    if (tempFiles.Remove(fname)) await _ffmpeg.DeleteFileAsync(fname);
            }

            // The previous intermediate has been consumed — free its MEMFS space now rather
            // than letting full-length videos accumulate until the end of the export.
            if (tempFiles.Remove(composited)) await _ffmpeg.DeleteFileAsync(composited);
            composited = outputName;
        }

        job.CompletedPhases.Add($"Text overlays applied ({overlays.Count})");
        Advance(job, 78, "Text overlays applied.");
        return composited;
    }

    private async Task<string> ApplyCalloutsAsync(
        ExportJob job, string inputName, ExportSettings s, List<string> tempFiles)
    {
        // LayerIndex, not TimelinePosition — see ApplyTextOverlaysAsync's remarks (backlog #39).
        var callouts = _clips.AllCalloutClips.OrderBy(c => c.LayerIndex).ToList();
        Advance(job, 72, $"Applying {callouts.Count} callout shape(s)…");

        // Separate SVG-rendered shapes from ffmpeg-native shapes
        var svgShapes     = callouts.Where(NeedsSvgRenderer).ToList();
        var nativeShapes  = callouts.Where(c => !NeedsSvgRenderer(c)).ToList();
        var composited    = inputName;
        var (vw, vh)      = ParseResolution(s.Resolution);

        // ── Native ffmpeg shapes (Rectangle, Ellipse via drawbox) ────────────
        if (nativeShapes.Count > 0)
        {
            var outputName = $"callout_native_{job.Id:N}.mp4";
            tempFiles.Add(outputName);
            var fragments = nativeShapes
                .Select(c => ExportArgBuilders.BuildCalloutFilter(c, s))
                .Where(f => !string.IsNullOrEmpty(f));
            var vfChain = string.Join(",", fragments);
            if (!string.IsNullOrEmpty(vfChain))
            {
                // Filter chain + explicit video map. The old "-vf chain" + AudioPassthroughArgs
                // combination mapped ONLY audio (explicit -map disables default stream
                // selection), producing an audio-only file that exited 0 — the silent
                // video-less export at the heart of backlog #29.
                await _ffmpeg.ExecAsync(
                    ExportArgBuilders.BuildFilteredVideoArgs(composited, vfChain, s, outputName), job.CancellationToken);
                composited = outputName;
            }
        }

        // ── SVG-rendered shapes (via SvgFrameRendererService) ────────────────
        // Static shapes composite ONE PNG via a looped input (fades as alpha-fade filters);
        // only animated (motion-path) callouts render per-frame PNG sequences — see the
        // matching note on ApplyTextOverlaysAsync (backlog #29 memory fix).
        var clipIdx = 0;
        foreach (var callout in svgShapes)
        {
            clipIdx++;
            var outputName = $"callout_svg_{job.Id:N}_{clipIdx}.mp4";
            tempFiles.Add(outputName);

            var startT = callout.TimelinePosition;
            var endT   = callout.TimelinePosition + callout.Duration;

            // Item #16, phase 116 — same reasoning as ApplyTextOverlaysAsync; no-op when the
            // callout has no text or uses a system font.
            if (!string.IsNullOrEmpty(callout.Text))
                await _googleFonts.EnsureLoadedAsync(callout.FontFamily);

            if (!_motion.HasPath(callout.Id))
            {
                // Static shape — single PNG looped for the clip's window.
                var png = $"svgcallout_{callout.Id:N}_static.png";
                var svg = CalloutShapeRenderer.Render(callout, vw, vh);
                await _ffmpeg.WriteFileFromBytesAsync(png, await _svgExporter.RenderFrameFromSvgAsync(svg, vw, vh));
                tempFiles.Add(png);

                var filter = ExportArgBuilders.BuildStaticOverlayFilter(
                    vw, vh, startT, endT, callout.FadeInSeconds, callout.FadeOutSeconds);

                await _ffmpeg.ExecAsync(
                [
                    "-i", composited,
                    "-loop", "1",
                    "-framerate", s.Fps.ToString("F2"),
                    "-t", endT.ToString("F3", System.Globalization.CultureInfo.InvariantCulture),
                    "-i", png,
                    "-filter_complex", filter,
                    "-map", "[out]",
                    ..AudioPassthroughArgs(s),
                    ..QualityArgs(s),
                    outputName,
                ], job.CancellationToken);

                if (tempFiles.Remove(png)) await _ffmpeg.DeleteFileAsync(png);
            }
            else
            {
                // Animated — one interpolated SVG per frame (position/size/opacity from the motion
                // path, multiplied by the fade envelope).
                var frameCount = Math.Max(1, (int)Math.Round(callout.Duration * s.Fps));
                var prefix     = $"svgcallout_{callout.Id:N}";
                var frameNames = new List<string>(frameCount);
                for (var i = 0; i < frameCount; i++)
                {
                    var elapsed   = ExportArgBuilders.ElapsedSeconds(i, s.Fps);
                    var t         = callout.TimelinePosition + elapsed; // absolute timeline seconds
                    var frame     = _motion.Evaluate(callout.Id, t)
                                    ?? new MotionFrame(callout.X, callout.Y, 1.0, 1.0);
                    var animated  = ExportArgBuilders.ApplyMotionFrame(callout, frame);
                    animated      = animated with { Opacity = animated.Opacity * callout.ComputeFadeAlpha(elapsed) };
                    var frameSvg  = CalloutShapeRenderer.Render(animated, vw, vh);
                    var fname     = $"{prefix}_{i:D4}.png";
                    await _ffmpeg.WriteFileFromBytesAsync(fname, await _svgExporter.RenderFrameFromSvgAsync(frameSvg, vw, vh));
                    tempFiles.Add(fname);
                    frameNames.Add(fname);
                }

                var filter = $"[1:v]scale={vw}:{vh}[ov];[0:v][ov]overlay=0:0:enable='between(t,{startT:F3},{endT:F3})'[out]";

                await _ffmpeg.ExecAsync(
                [
                    "-i", composited,
                    "-framerate", s.Fps.ToString("F2"),
                    "-i", $"{prefix}_%04d.png",
                    "-filter_complex", filter,
                    "-map", "[out]",
                    ..AudioPassthroughArgs(s),
                    ..QualityArgs(s),
                    outputName,
                ], job.CancellationToken);

                foreach (var fname in frameNames)
                    if (tempFiles.Remove(fname)) await _ffmpeg.DeleteFileAsync(fname);
            }

            if (tempFiles.Remove(composited)) await _ffmpeg.DeleteFileAsync(composited);
            composited = outputName;
        }

        job.CompletedPhases.Add($"Callout shapes applied ({callouts.Count})");
        Advance(job, 75, "Callout shapes applied.");
        return composited;
    }

    /// <summary>
    /// True when the callout shape needs SVG frame rendering (not ffmpeg-native filters) — either because
    /// the shape itself requires it (Arrow/Line/Star), because it has a motion path (animated callouts
    /// always render through the per-frame SVG pipeline, regardless of shape, since that's the only path
    /// that can express a changing position/size/opacity per frame — native <c>drawbox</c> can't animate
    /// alpha at all), because it has a text label (the native <c>drawbox</c> fast path has no text
    /// rendering at all — <see cref="CalloutShapeRenderer.Render"/> already draws it), or because it has
    /// a fade (expressed as alpha-fade filters on the SVG overlay input; <c>drawbox</c> can't fade).
    /// </summary>
    private bool NeedsSvgRenderer(CalloutClip c) =>
        c.Shape is ShapeType.Arrow or ShapeType.Line or ShapeType.Star
        || _motion.HasPath(c.Id)
        || !string.IsNullOrEmpty(c.Text)
        || c.FadeInSeconds > 0 || c.FadeOutSeconds > 0;

    /// <summary>
    /// Phase 3c: composite all <see cref="ClipArtClip"/> layers over the current video.
    /// Raster clips (PNG/AVIF/WebP) are written to MEMFS and overlaid statically.
    /// SVG clips are passed to <see cref="SvgAnimationExporter"/> which renders
    /// a PNG frame sequence then composites it via the overlay filter.
    /// </summary>
    private async Task<string> ApplyClipArtClipsAsync(
        ExportJob job, string inputName, ExportSettings s, List<string> tempFiles)
    {
        // LayerIndex, not TimelinePosition — see ApplyTextOverlaysAsync's remarks (backlog #39).
        var clips = _clips.AllClipArtClips.OrderBy(c => c.LayerIndex).ToList();
        Advance(job, 76, $"Applying {clips.Count} clipart layer(s)…");

        var composited = inputName;
        var clipIdx    = 0;

        foreach (var clip in clips)
        {
            clipIdx++;
            var outputName = $"clipart_{job.Id:N}_{clipIdx}.mp4";
            tempFiles.Add(outputName);

            if (clip.AssetFormat == VideoAssetFormat.Svg && clip.ControlPoints is { Count: > 0 })
            {
                // SVG with control points — render frame-by-frame via SvgAnimationExporter
                var (width, height) = ParseResolution(s.Resolution);
                var (args, writtenFiles) = await _svgExporter.RenderAsync(
                    clip, composited, s.Fps, width, height, tempFiles);

                if (args.Length == 0)
                {
                    // Could not read SVG — skip this clip
                    continue;
                }

                var fullArgs = new List<string> { "-i", composited };
                fullArgs.AddRange(args);
                fullArgs.AddRange(QualityArgs(s));
                fullArgs.AddRange(AudioPassthroughArgs(s));
                fullArgs.Add(outputName);
                await _ffmpeg.ExecAsync([.. fullArgs], job.CancellationToken);

                // Item #38 phase D — the per-frame PNG sequence is consumed now. RenderAsync
                // already returns this list for exactly this purpose; previously discarded.
                foreach (var fname in writtenFiles)
                    if (tempFiles.Remove(fname)) await _ffmpeg.DeleteFileAsync(fname);
            }
            else if (_motion.HasPath(clip.Id))
            {
                // Raster (or simple SVG) with a motion path — position/size/opacity vary per
                // frame, which a single static overlay can't express. Mirrors the animated
                // CalloutClip/TextOverlay path above: render one full-canvas PNG per output frame
                // (via RasterClipArtAnimationExporter, decoding the source image once in JS and
                // re-drawing it per frame at the already-interpolated geometry) instead of trying
                // to replicate MotionKeyframeService's easing/bezier math as ffmpeg expressions.
                var animated = await ApplyAnimatedClipArtAsync(clip, composited, s, outputName, tempFiles, job.CancellationToken);
                if (animated is null) continue;
            }
            else
            {
                // Raster (or simple SVG without animated control points) — static overlay
                var overlayFile = await WriteClipArtToMemFsAsync(clip);
                if (overlayFile is null) continue;
                tempFiles.Add(overlayFile);

                var (vw, vh) = ParseResolution(s.Resolution);
                var filter = ExportArgBuilders.BuildClipArtStaticOverlayFilter(clip, vw, vh);

                await _ffmpeg.ExecAsync(
                [
                    "-i", composited,
                    "-i", overlayFile,
                    "-filter_complex", filter,
                    "-map", "[out]",
                    ..AudioPassthroughArgs(s),
                    ..QualityArgs(s),
                    outputName,
                ], job.CancellationToken);

                if (tempFiles.Remove(overlayFile)) await _ffmpeg.DeleteFileAsync(overlayFile);
            }

            // Item #38 phase D — this method was the one overlay pass that didn't eagerly delete
            // its consumed predecessor, unlike ApplyTextOverlaysAsync/ApplyCalloutsAsync above.
            if (tempFiles.Remove(composited)) await _ffmpeg.DeleteFileAsync(composited);
            composited = outputName;
        }

        job.CompletedPhases.Add($"ClipArt overlays applied ({clipIdx})");
        Advance(job, 80, "ClipArt overlays applied.");
        return composited;
    }

    /// <summary>
    /// Renders an animated raster (or plain, control-point-free SVG) <see cref="ClipArtClip"/> as a
    /// per-frame PNG sequence and composites it into <paramref name="outputName"/>. Returns null (and
    /// leaves <paramref name="outputName"/> unwritten) if the source asset couldn't be read, matching
    /// the static path's "skip this clip" behavior.
    ///
    /// Item #59-#65 flakiness investigation, phase 146 (MEMFS pressure) — this used to render
    /// EVERY frame for the clip's whole duration (duration × fps — e.g. 1800 frames for a
    /// 60s@30fps clip) in one JS batch call, write every one into MEMFS, and only then run a
    /// single ffmpeg exec to consume them — the dominant source of MEMFS/heap pressure in the
    /// whole export pipeline (confirmed via the item #38 design doc's own ranking). Now renders,
    /// writes, encodes, and deletes in <see cref="AnimatedOverlayBatchPlanner"/>-sized batches,
    /// each producing a small intermediate segment covering just that batch's time slice of the
    /// clip's own overlay window — then splices that window back into <paramref name="composited"/>
    /// via the same trim-before/middle/trim-after + concat structure the three-point-editing work
    /// (item #49-52) already established, rather than inventing a new pattern.
    /// </summary>
    private async Task<string?> ApplyAnimatedClipArtAsync(
        ClipArtClip clip, string composited, ExportSettings s, string outputName, List<string> tempFiles,
        CancellationToken ct)
    {
        if (!Guid.TryParse(clip.AssetId, out var guid)) return null;

        var ext        = FormatExt(clip.AssetFormat);
        var sourceFile = await _opfs.ReadAsJSFileAsync(guid, $".{ext}");
        if (sourceFile is null) return null;

        var (vw, vh) = ParseResolution(s.Resolution);
        var frameCount = Math.Max(1, (int)Math.Round(clip.Duration * s.Fps));
        var startT = clip.TimelinePosition;
        var endT   = clip.TimelinePosition + clip.Duration;

        // A large batch count is a real, if now-bounded-per-batch, cost — worth a heads-up rather
        // than a silent multi-minute export. Purely informational; batching already keeps this
        // from becoming a memory problem regardless of frame count.
        if (frameCount > 600)
        {
            _errorLog.Log("ExportService.ApplyAnimatedClipArtAsync",
                $"'{clip.Name}' animates {frameCount} frames ({clip.Duration:F1}s at {s.Fps:F0}fps) — this export will take proportionally longer, though MEMFS usage stays bounded.");
        }

        var middleName = await RenderAnimatedMiddleInBatchesAsync(clip, composited, sourceFile, vw, vh, frameCount, startT, s, tempFiles, ct);
        if (middleName is null) return null;

        var meta = await _ffmpeg.GetMetadataAsync(composited);
        var totalDuration = meta.Duration;

        var pieces = new List<string>();
        if (startT > 0.01)
        {
            var beforeName = $"clipart_anim_{clip.Id:N}_before.mp4";
            await _ffmpeg.TrimClipAsync(composited, beforeName, 0, startT);
            tempFiles.Add(beforeName);
            pieces.Add(beforeName);
        }
        pieces.Add(middleName);
        if (endT < totalDuration - 0.01)
        {
            var afterName = $"clipart_anim_{clip.Id:N}_after.mp4";
            await _ffmpeg.TrimClipAsync(composited, afterName, endT, totalDuration);
            tempFiles.Add(afterName);
            pieces.Add(afterName);
        }

        if (pieces.Count == 1)
        {
            // The animated clip spans the whole export — nothing to splice.
            await _ffmpeg.RenameFileAsync(middleName, outputName);
            tempFiles.Remove(middleName);
        }
        else
        {
            // ConcatClipsAsync (re-encoding), not ConcatCopyAsync — TrimClipAsync's own hardcoded
            // codec args aren't guaranteed to bit-match QualityArgs(s), and a stream-copy concat
            // across mismatched codec parameters produces corrupt output rather than failing loudly.
            await _ffmpeg.ConcatClipsAsync([.. pieces], outputName);
            foreach (var p in pieces)
                if (tempFiles.Remove(p)) await _ffmpeg.DeleteFileAsync(p);
        }

        return outputName;
    }

    /// <summary>
    /// Renders <paramref name="clip"/>'s animated overlay in bounded batches, each producing a
    /// small MEMFS segment covering that batch's time slice, then concatenates the segments into
    /// one MEMFS file covering the clip's whole <c>[startT, startT + duration]</c> window — the
    /// "middle" piece <see cref="ApplyAnimatedClipArtAsync"/> splices back into the base video.
    /// </summary>
    private async Task<string?> RenderAnimatedMiddleInBatchesAsync(
        ClipArtClip clip, string composited, IJSObjectReference sourceFile,
        int vw, int vh, int frameCount, double startT, ExportSettings s, List<string> tempFiles,
        CancellationToken ct)
    {
        var batchSize = AnimatedOverlayBatchPlanner.BatchSize(vw, vh);
        var segments  = new List<string>();

        foreach (var (batchIndex, batchStart, batchCount) in AnimatedOverlayBatchPlanner.Batches(frameCount, batchSize))
        {
            // Audit #1 — the batch boundary is the single most valuable cancellation point in the
            // whole pipeline: a 60s@30fps animated overlay is ~1800 rasterised PNGs plus one encode
            // per batch, easily the longest-running stretch of an export. Checking here means
            // Cancel lands after the current batch rather than after the entire overlay.
            ct.ThrowIfCancellationRequested();
            var frames = new List<RasterClipArtFrame>(batchCount);
            for (var i = 0; i < batchCount; i++)
            {
                var globalIndex = batchStart + i;
                var elapsed  = ExportArgBuilders.ElapsedSeconds(globalIndex, s.Fps);
                var t        = clip.TimelinePosition + elapsed; // absolute timeline seconds
                var frame    = _motion.Evaluate(clip.Id, t) ?? new MotionFrame(clip.X, clip.Y, 1.0, 1.0);
                var animated = ExportArgBuilders.ApplyMotionFrame(clip, frame);

                var px = animated.X * vw;
                var py = animated.Y * vh;
                var ow = Math.Max(1.0, animated.Width * vw);
                var oh = animated.Height > 0 ? Math.Max(1.0, animated.Height * vh) : ow;
                frames.Add(new RasterClipArtFrame(px, py, ow, oh, animated.Opacity, animated.Rotation, animated.TintColor));
            }

            var pngFrames = await _rasterClipArtExporter.RenderBatchAsync(sourceFile, vw, vh, frames);

            var prefix = $"clipart_anim_{clip.Id:N}_b{batchIndex}";
            for (var i = 0; i < pngFrames.Count; i++)
            {
                var fname = $"{prefix}_{i:D4}.png";
                await _ffmpeg.WriteFileFromBytesAsync(fname, pngFrames[i]);
                tempFiles.Add(fname);
            }

            // This batch's own base slice — trimmed fresh from `composited` each time, never the
            // whole video, so this exec's own peak stays bounded too.
            var batchStartSec = startT + ExportArgBuilders.ElapsedSeconds(batchStart, s.Fps);
            var batchEndSec   = startT + ExportArgBuilders.ElapsedSeconds(batchStart + batchCount, s.Fps);
            var baseSliceName = $"clipart_anim_{clip.Id:N}_base{batchIndex}.mp4";
            await _ffmpeg.TrimClipAsync(composited, baseSliceName, batchStartSec, batchEndSec);
            tempFiles.Add(baseSliceName);

            var segName = $"clipart_anim_{clip.Id:N}_seg{batchIndex}.mp4";
            tempFiles.Add(segName);

            // No enable=/between() needed — the base slice IS exactly this batch's window, so the
            // overlay is active for its entire duration.
            var filter = $"[1:v]scale={vw}:{vh}[ov];[0:v][ov]overlay=0:0[out]";
            await _ffmpeg.ExecAsync(
            [
                "-i", baseSliceName,
                "-framerate", s.Fps.ToString("F2"),
                "-i", $"{prefix}_%04d.png",
                "-filter_complex", filter,
                "-map", "[out]",
                ..AudioPassthroughArgs(s),
                ..QualityArgs(s),
                segName,
            ], ct);

            // Delete this batch's PNGs and base slice immediately — never more than one batch's
            // worth of frames resident in MEMFS at once.
            for (var i = 0; i < pngFrames.Count; i++)
            {
                var fname = $"{prefix}_{i:D4}.png";
                if (tempFiles.Remove(fname)) await _ffmpeg.DeleteFileAsync(fname);
            }
            if (tempFiles.Remove(baseSliceName)) await _ffmpeg.DeleteFileAsync(baseSliceName);

            segments.Add(segName);
        }

        if (segments.Count == 0) return null;
        if (segments.Count == 1)
        {
            tempFiles.Remove(segments[0]); // caller takes ownership (rename or concat-and-delete)
            return segments[0];
        }

        var middleName = $"clipart_anim_{clip.Id:N}_middle.mp4";
        await _ffmpeg.ConcatClipsAsync([.. segments], middleName);
        foreach (var seg in segments)
            if (tempFiles.Remove(seg)) await _ffmpeg.DeleteFileAsync(seg);
        return middleName;
    }

    /// <summary>Write a raster <see cref="ClipArtClip"/> asset to ffmpeg MEMFS. Returns the MEMFS name or null.</summary>
    private async Task<string?> WriteClipArtToMemFsAsync(ClipArtClip clip)
    {
        if (!Guid.TryParse(clip.AssetId, out var guid)) return null;

        var ext     = FormatExt(clip.AssetFormat);
        var memName = $"ca_{clip.Id:N}.{ext}";

        // Read from OPFS and write to MEMFS
        var fileRef = await _opfs.ReadAsJSFileAsync(guid, $".{ext}");
        if (fileRef is null) return null;

        await _ffmpeg.WriteFileAsync(memName, fileRef);
        return memName;
    }

    private static string FormatExt(VideoAssetFormat f) => f switch
    {
        VideoAssetFormat.Avif  => "avif",
        VideoAssetFormat.WebP  => "webp",
        VideoAssetFormat.Gif   => "gif",
        VideoAssetFormat.Svg   => "svg",
        _                      => "png",
    };

    /// <summary>Trigger a browser download of an SRT subtitle file for all TextOverlays.</summary>
    public async Task DownloadSrtAsync(string projectName = "subtitles")
    {
        var overlays = _clips.AllTextOverlays.ToList();
        if (overlays.Count == 0) return;
        await BlobDownloadAsync(SubtitleBuilder.BuildSrt(overlays), $"{projectName}.srt", "text/plain");
    }

    /// <summary>Trigger a browser download of a WebVTT subtitle file.</summary>
    public async Task DownloadWebVttAsync(string projectName = "subtitles")
    {
        var overlays = _clips.AllTextOverlays.ToList();
        if (overlays.Count == 0) return;
        await BlobDownloadAsync(SubtitleBuilder.BuildWebVtt(overlays), $"{projectName}.vtt", "text/vtt");
    }

    /// <summary>Trigger a browser download of an ASS subtitle file.</summary>
    public async Task DownloadAssAsync(string projectName = "subtitles")
    {
        var overlays = _clips.AllTextOverlays.ToList();
        if (overlays.Count == 0) return;
        await BlobDownloadAsync(SubtitleBuilder.BuildAss(overlays), $"{projectName}.ass", "text/plain");
    }

    private async Task BlobDownloadAsync(string content, string filename, string mimeType)
    {
        // Phase 144's deferred revoke lives inside domInterop.downloadText now (audit #4) — see
        // that function's own note for why the 30s delay must not be tidied into an immediate one.
        // The module handle is cached rather than re-imported per download: a dynamic import on
        // every call costs a module-resolution round trip for no benefit, and these can fire
        // several times in a row (srt + vtt).
        await (await DomAsync()).InvokeVoidAsync("downloadText", content, filename, mimeType);
    }

    private async Task<string> MixAudioTracksAsync(
        ExportJob job, string videoInput, ExportSettings s, List<string> tempFiles)
    {
        var audioClips = _clips.AudioTracks
                               .SelectMany(t => t.AudioClips)
                               .OrderBy(a => a.TimelinePosition)
                               .ToList();

        if (audioClips.Count == 0) return videoInput;

        Advance(job, 80, $"Mixing {audioClips.Count} audio track(s)…");

        // Trim + apply each clip's own volume/automation/channel-balance/fade, then delay the
        // result to its TimelinePosition — previously this method amixed every clip's raw,
        // untrimmed source starting at t=0, silently ignoring StartTrim/EndTrim/Volume/
        // VolumeAutomation/FadeInSeconds/FadeOutSeconds/TimelinePosition entirely (found while
        // implementing backlog #10 — per-channel volume would have been equally inert bolted onto
        // the same broken path). Each per-clip segment below is a normal audio-only ffmpeg output,
        // already positioned; amix combines them with no further position/offset math needed.
        var segmentNames = new List<string>();
        for (var i = 0; i < audioClips.Count; i++)
        {
            var ac = audioClips[i];
            if (ac.MemFsName is null) continue;

            var start = ac.StartTrim;
            var end   = ac.EndTrim > ac.StartTrim ? ac.EndTrim : ac.Duration;
            var clipDuration = end - start;
            if (clipDuration <= 0) continue;

            var filterChain = ExportArgBuilders.BuildAudioClipFilterChain(ac, clipDuration);
            var delayMs     = (int)Math.Round(Math.Max(0, ac.TimelinePosition) * 1000.0);
            var fullFilter  = delayMs > 0 ? $"{filterChain},adelay={delayMs}:all=1" : filterChain;

            var segName = $"audio_seg_{i:D3}_{job.Id:N}.mp4";
            tempFiles.Add(segName);
            var segArgs = ExportArgBuilders.BuildAudioClipTrimArgs(ac.MemFsName, segName, start, end, fullFilter, s);
            await _ffmpeg.ExecAsync(segArgs, job.CancellationToken);
            segmentNames.Add(segName);
        }

        if (segmentNames.Count == 0) return videoInput;

        var outputName = $"mixed_{job.Id:N}.mp4";
        tempFiles.Add(outputName);

        // Item #70 phase 162 — the amix argv moved to ExportArgBuilders.BuildAmixArgs so the
        // sidecar's export-assemble job runs the byte-identical command. Extraction parity is
        // pinned by AmixArgBuilderTests, whose expectations were transcribed from this code
        // BEFORE the move.
        var n = segmentNames.Count;
        await _ffmpeg.ExecAsync(ExportArgBuilders.BuildAmixArgs(videoInput, segmentNames, outputName, s), job.CancellationToken);

        // Item #38 phase D — the pre-mix video and every per-clip audio segment are consumed now.
        if (tempFiles.Remove(videoInput)) await _ffmpeg.DeleteFileAsync(videoInput);
        foreach (var segName in segmentNames)
            if (tempFiles.Remove(segName)) await _ffmpeg.DeleteFileAsync(segName);

        job.CompletedPhases.Add($"Audio mixed ({n + 1} streams)");
        Advance(job, 90, "Audio mix complete.");
        return outputName;
    }

    private async Task<string> ComposeVideoLayersAsync(
        ExportJob              job,
        string                 baseLayer,
        List<TimelineTrack>    extraTracks,
        ExportSettings         s,
        List<string>           tempFiles)
    {
        Advance(job, 67, $"Compositing {extraTracks.Count + 1} video layer(s)…");

        // Render each extra track's clips into a temporary file
        var layerFiles = new List<string> { baseLayer };

        for (var i = 0; i < extraTracks.Count; i++)
        {
            var track      = extraTracks[i];
            var trackClips = track.VideoClips.OrderBy(c => c.Order).ToList();
            if (trackClips.Count == 0) continue;

            var layerSegs   = new List<string>();
            for (var j = 0; j < trackClips.Count; j++)
            {
                var clip    = trackClips[j];
                if (clip.MemFsName is null) continue;

                var segName = $"layer{i}_seg{j}_{job.Id:N}.mp4";
                tempFiles.Add(segName);

                var start = clip.StartTrim;
                var end   = clip.EndTrim > clip.StartTrim ? clip.EndTrim : clip.Duration;
                var volFilter = ExportArgBuilders.BuildVolumeAutomationFilter(clip, end - start);
                var (lvw, lvh) = ParseResolution(s.Resolution);

                var trimArgs = ExportArgBuilders.BuildTrimArgs(
                    clip.MemFsName, segName, start, end, clip.Speed, s,
                    volFilter, clip.Effects, outputWidth: lvw, outputHeight: lvh,
                    sourceHasAudio: clip.HasAudio);
                await _ffmpeg.ExecAsync(trimArgs, job.CancellationToken);
                layerSegs.Add(segName);
            }

            if (layerSegs.Count == 0) continue;

            string layerOutput;
            if (layerSegs.Count == 1)
            {
                layerOutput = layerSegs[0];
            }
            else
            {
                layerOutput = $"layer{i}_concat_{job.Id:N}.mp4";
                tempFiles.Add(layerOutput);
                await _ffmpeg.ConcatClipsAsync([.. layerSegs], layerOutput);
            }

            layerFiles.Add(layerOutput);
        }

        if (layerFiles.Count < 2) return baseLayer;

        var compositeOutput = $"composite_{job.Id:N}.mp4";
        tempFiles.Add(compositeOutput);

        // layerFiles[0] = bottom (primary), layerFiles[^1] = top (lowest Order)
        var overlayArgs = ExportArgBuilders.BuildOverlayFilterComplex(
            layerFiles, compositeOutput, _options.AlphaCompositing, s);
        await _ffmpeg.ExecAsync(overlayArgs, job.CancellationToken);

        job.CompletedPhases.Add($"Video layers composited ({layerFiles.Count})");
        Advance(job, 75, "Layer compositing complete.");
        return compositeOutput;
    }

    // â”€â”€ ffmpeg arg builders â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

    private static string[] BuildTrimArgs(
        string input, string output, double start, double end, double speed, ExportSettings s,
        string? audioVolumeFilter = null, ClipEffects? effects = null, bool muteAudio = false,
        string? extraVf = null, bool sourceHasAudio = true)
    {
        var (vw, vh) = ParseResolution(s.Resolution);
        return ExportArgBuilders.BuildTrimArgs(
            input, output, start, end, speed, s, audioVolumeFilter, effects, muteAudio, extraVf,
            outputWidth: vw, outputHeight: vh, sourceHasAudio: sourceHasAudio);
    }

    private static string BuildXfadeFilterComplex(
        List<string> segments, List<double> segmentDurations, List<Transition> transitions)
        => ExportArgBuilders.BuildXfadeFilterComplex(segments, segmentDurations, transitions);

    private static IEnumerable<string> QualityArgs(ExportSettings s)
        => ExportArgBuilders.QualityArgs(s);

    private static IEnumerable<string> AudioOutputArgs(ExportSettings s)
        => ExportArgBuilders.AudioOutputArgs(s);

    private static IEnumerable<string> AudioPassthroughArgs(ExportSettings s)
        => ExportArgBuilders.AudioPassthroughArgs(s);

    // â”€â”€ Helpers â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

    /// <summary>Rename a MEMFS file in place (item #38 phase D — a genuine filesystem rename, not
    /// the old read-bytes/write-under-new-name/delete-old round trip).</summary>
    private async Task RenameAsync(string from, string to)
    {
        if (from == to) return;
        await _ffmpeg.RenameFileAsync(from, to);
    }

    private static void ThrowIfCancelled(ExportJob job)
    {
        if (job.CancelRequested)
            throw new OperationCanceledException("Export cancelled by user.");
    }

    private static void Advance(ExportJob job, int pct, string label)
    {
        job.OverallPercent = pct;
        job.PhaseLabel     = label;
        job.NotifyProgress();
    }

    private static int ProgressInRange(int index, int total, int rangeStart, int rangeEnd)
        => ExportArgBuilders.ProgressInRange(index, total, rangeStart, rangeEnd);

    private static string MimeType(string format)
        => ExportArgBuilders.MimeType(format);

    private static string SanitiseFilename(string name)
        => ExportArgBuilders.SanitiseFilename(name);

    // ── Retained-export disposition (phase 176) ───────────────────────────────
    //
    // A job run with downloadToDisk:false leaves its output sitting in OPFS. That mode already
    // existed for the full-quality Preview popout (item #36 phase 84); phase 176 gives it a second
    // caller — the destination prompt, which asks the user whether the render should go to their
    // machine or up to the host — and these three methods are the only ways that retained output
    // is allowed to end. Each one is terminal: the OPFS copy is gone afterwards either way, so a
    // prompt that is dismissed still cleans up and can never leak a full render into storage the
    // user has no UI to reclaim.

    // internal (not private) so RetainedExportNamingTests can pin these against the pipeline's own
    // Phase-5 naming. They are two independent expressions of the same rule: get them out of step
    // and the file the user is offered — or the filename the host stores — silently stops matching
    // the file the pipeline actually wrote.
    internal static string RetainedExt(ExportJob job)      => "." + job.Settings.OutputFormat;
    internal static string RetainedFileName(ExportJob job) => $"{SanitiseFilename(job.Settings.OutputFilename)}.{job.Settings.OutputFormat}";

    /// <summary>
    /// Saves a retained export to the user's machine through the browser's own download, then
    /// drops the OPFS copy — the same end state a plain <c>downloadToDisk: true</c> export reaches,
    /// just decided after the render instead of before it.
    /// </summary>
    public async Task SaveRetainedExportToDeviceAsync(ExportJob job)
    {
        var blobUrl = job.PreviewBlobUrl;
        if (blobUrl is null) return;

        try
        {
            await _ffmpeg.DownloadBlobUrlAsync(blobUrl, RetainedFileName(job));
        }
        finally
        {
            // Deliberately not revoking blobUrl here: DownloadBlobUrlAsync's own JS owns that and
            // defers it ~30s, because revoking immediately races the browser actually starting the
            // download (phase 144's lesson — see the Phase 5 download step above).
            job.PreviewBlobUrl = null;
            await _opfs.DeleteExportAsync(job.Id, RetainedExt(job));
        }
    }

    /// <summary>
    /// Hands a retained export to the host as an <see cref="ExportedVideo"/> and returns once the
    /// host is done with it, then drops the OPFS copy and the blob URL.
    ///
    /// <para>The host callback runs <i>inside</i> this method rather than being handed a detached
    /// object, which is what makes "the bytes are only alive for the callback" enforceable rather
    /// than merely documented.</para>
    /// </summary>
    public async Task TakeRetainedExportAsync(ExportJob job, Func<ExportedVideo, Task> consume)
    {
        var blobUrl = job.PreviewBlobUrl;

        var exported = new ExportedVideo(
            FileName:        RetainedFileName(job),
            ContentType:     MimeType(job.Settings.OutputFormat),
            SizeBytes:       job.OutputSizeBytes,
            DurationSeconds: job.Duration,
            ReadBytesAsync:  () => ReadRetainedBytesAsync(blobUrl));

        // NOT in a finally: a host that throws (upload failed) must keep its output, because the
        // destination prompt's whole recovery story is "you can still save it to your machine".
        // Discarding on failure would delete the only copy of a render the user just waited for.
        await consume(exported);
        await DiscardRetainedExportAsync(job);
    }

    private async Task<byte[]?> ReadRetainedBytesAsync(string? blobUrl)
    {
        if (blobUrl is null) return null;
        try
        {
            var dom = await DomAsync();
            return await dom.InvokeAsync<byte[]>("blobUrlAsBytes", blobUrl);
        }
        catch (Exception ex)
        {
            _errorLog.Log("ExportService.ReadRetainedBytesAsync", ex);
            return null;
        }
    }

    /// <summary>Drops a retained export the user decided not to keep anywhere — the dismiss path.</summary>
    public async Task DiscardRetainedExportAsync(ExportJob job)
    {
        if (job.PreviewBlobUrl is { } url)
        {
            job.PreviewBlobUrl = null;
            // Same revoke the full-quality Preview popout already uses for this exact kind of URL
            // (minted by opfsExportsReadAsBlobUrl), rather than PreviewUrlRevoker: that class
            // routes by origin for URLs that may have come from the sidecar, which these never do.
            await _ffmpeg.RevokePreviewUrlAsync(url);
        }
        await _opfs.DeleteExportAsync(job.Id, RetainedExt(job));
    }

    /// <summary>Parse a "WxH" resolution string into (width, height). Defaults to 1920×1080.</summary>
    internal static (int w, int h) ParseResolution(string resolution)
    {
        if (!string.IsNullOrEmpty(resolution))
        {
            var parts = resolution.Split('x');
            if (parts.Length == 2
                && int.TryParse(parts[0], out var w)
                && int.TryParse(parts[1], out var h))
                return (w, h);
        }
        return (1920, 1080);
    }

    /// <summary>
    /// Phase 4.5: composite the server-enforced watermark over the current output.
    /// Downloads and caches the watermark file in OPFS on first use.
    /// Returns the new MEMFS output file name.
    /// </summary>
    private async Task<string> ApplyWatermarkAsync(
        ExportJob job, string inputName, VideoWatermarkConfig config,
        ExportSettings s, List<string> tempFiles)
    {
        Advance(job, 91, "Applying watermark…");

        // Ensure the watermark file is in OPFS
        var local = await _watermark.EnsureLocalAsync(config);
        if (local is null)
        {
            // No watermark file available — skip silently
            return inputName;
        }

        var (wmId, wmExt) = local.Value;

        // Write watermark from OPFS to MEMFS
        var wmRef = await _opfs.ReadAsJSFileAsync(wmId, wmExt);
        if (wmRef is null) return inputName;

        var wmMemFs = $"watermark_{job.Id:N}{wmExt}";
        await _ffmpeg.WriteFileAsync(wmMemFs, wmRef);
        tempFiles.Add(wmMemFs);

        var (vw, vh) = ParseResolution(s.Resolution);
        var filter   = WatermarkService.BuildOverlayFilter(config, wmMemFs, vw, vh);

        var outputName = $"wm_{job.Id:N}.{s.OutputFormat}";
        tempFiles.Add(outputName);

        await _ffmpeg.ExecAsync(
        [
            "-i", inputName,
            "-i", wmMemFs,
            "-filter_complex", filter,
            "-map", "[out]",
            ..AudioPassthroughArgs(s),
            ..QualityArgs(s),
            outputName,
        ], job.CancellationToken);

        // Item #38 phase D — real bug found here: this method runs *after* RunPipelineAsync's
        // RenameAsync step, so inputName is the user-facing renamed file, which was never added to
        // tempFiles in the first place. That meant a watermarked export previously leaked the full
        // pre-watermark file for the rest of the process's lifetime — not even the final `finally`
        // cleanup could catch it, since it was never registered anywhere. Delete unconditionally
        // here (the two early-return "no watermark configured" paths above correctly leave
        // inputName alone, since nothing replaces it in that case).
        await _ffmpeg.DeleteFileAsync(inputName);
        if (tempFiles.Remove(wmMemFs)) await _ffmpeg.DeleteFileAsync(wmMemFs);


        // Back to the name the person chose. The watermark pass writes to wm_<jobid>.<fmt>, and
        // returning that made it the download's filename — a watermarked export arrived as
        // wm_9f3c….mp4 while an unwatermarked one kept "my-video.mp4" (2026-09-05 audit,
        // export-6). Renaming here keeps the caller's single outputName true for the probe, the
        // OPFS move and the browser download alike.
        var finalName = inputName;
        await RenameAsync(outputName, finalName);
        tempFiles.Remove(outputName);

        job.CompletedPhases.Add("Watermark applied");
        Advance(job, 93, "Watermark applied.");
        return finalName;
    }

    // â”€â”€ Chapter embed â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

    private async Task<string> EmbedChaptersAsync(
        ExportJob job,
        string inputFile,
        IReadOnlyList<TimelineMarker> markers,
        ExportSettings s,
        List<string> tempFiles)
    {
        var totalDuration = _clips.TotalDuration;
        var metadata      = ExportArgBuilders.BuildChapterMetadata(markers, totalDuration);
        if (string.IsNullOrEmpty(metadata)) return inputFile;

        var metaName   = $"chapters_{job.Id:N}.ffmeta";
        var outputName = $"chaptered_{job.Id:N}.{s.OutputFormat}";
        tempFiles.Add(metaName);
        tempFiles.Add(outputName);

        Advance(job, 90, $"Embedding {markers.Count} chapter(s)\u2026");

        // Write the ffmetadata text file into MEMFS as UTF-8 bytes
        var metaBytes = System.Text.Encoding.UTF8.GetBytes(metadata);
        await _ffmpeg.WriteBytesAsync(metaName, metaBytes);

        var args = ExportArgBuilders.BuildChapterEmbedArgs(inputFile, metaName, outputName);
        await _ffmpeg.ExecAsync(args, job.CancellationToken);

        // Item #38 phase D — the pre-chapter video and the small metadata file are consumed now.
        if (tempFiles.Remove(inputFile)) await _ffmpeg.DeleteFileAsync(inputFile);
        if (tempFiles.Remove(metaName)) await _ffmpeg.DeleteFileAsync(metaName);

        job.CompletedPhases.Add($"Chapters embedded ({markers.Count})");
        Advance(job, 93, "Chapters embedded.");
        return outputName;
    }

    /// <summary>Audit #4 — releases the cached domInterop handle. Scoped service, so Blazor's DI
    /// container disposes this at circuit teardown.</summary>
    public async ValueTask DisposeAsync()
    {
        if (_dom is null) return;
        try { await _dom.DisposeAsync(); } catch (JSDisconnectedException) { } catch (ObjectDisposedException) { }
        _dom = null;
    }
}
