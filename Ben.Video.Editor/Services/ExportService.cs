using Ben.Video.Editor.Effects;
using Ben.Video.Editor.Models;
using Ben.Video.Editor.Models.Assets;
using Microsoft.Extensions.Options;
using Microsoft.JSInterop;
using Ben.Video.Core.SidecarContracts;
using System.Globalization;

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
        catch (OperationCanceledException) when (job.CancelRequested)
        {
            job.State        = ExportJobState.Cancelled;
            job.FinishedAt   = DateTimeOffset.UtcNow;
            job.PhaseLabel   = "Cancelled.";
            job.NotifyProgress();
        }
        catch (OperationCanceledException)
        {
            // Cancelled is what the person did, not what happened to them. A timeout inside a
            // wedged ffmpeg worker also arrives here, and reporting it as "Cancelled." told
            // somebody who had cancelled nothing that they had — so the export looked like their
            // own doing and there was nothing to report (2026-09-05 audit, export-12).
            job.State        = ExportJobState.Failed;
            job.ErrorMessage = "The video engine stopped responding and the export was abandoned. "
                             + "Reload the engine from the toolbar and try again; if it keeps "
                             + "happening, the file may be too large for the browser to handle.";
            job.FinishedAt   = DateTimeOffset.UtcNow;
            job.PhaseLabel   = "Failed: the video engine stopped responding.";
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

    // ── Still frame ───────────────────────────────────────────────────────────

    /// <summary>
    /// Saves the frame at <paramref name="timelineSeconds"/> as a PNG on the person's machine.
    /// </summary>
    /// <returns>The suggested filename, or null with a reason when there is no frame there.</returns>
    /// <remarks>
    /// <para>For a site whose members are cutting evidence reels, the single frame where something
    /// appears is what actually gets shared, and the editor could only produce video (2026-09-05
    /// audit, the completeness critic's list).</para>
    ///
    /// <para>The frame comes from the clip's own source at its full resolution, not from the
    /// preview: the preview is a scaled proxy, and a still taken from it would be softer than the
    /// footage it came from — which is exactly the wrong trade for a frame someone is going to
    /// look closely at. Overlays are therefore not on it, which is also the right answer for a
    /// frame offered as evidence.</para>
    /// </remarks>
    public async Task<(string? FileName, string? Problem)> SaveFrameAsync(double timelineSeconds)
    {
        var item = TrackLayout.SequentialItems(_clips.PrimaryVideoTrack)
            .FirstOrDefault(i => timelineSeconds >= i.TimelinePosition
                              && timelineSeconds < i.TimelinePosition + i.EffectiveLength);

        var (source, sourceSeconds) = item switch
        {
            // Timeline time back to the clip's own: subtract where it starts, add what was
            // trimmed off its head, and undo the speed change.
            VideoClip v when v.MemFsName is not null =>
                (v.MemFsName, v.StartTrim + (timelineSeconds - v.TimelinePosition) * v.Speed),
            ImageClip i when i.MemFsName is not null => (i.MemFsName, 0.0),
            _ => (null, 0.0),
        };

        if (source is null)
            return (null, "There is no clip at the playhead to take a frame from.");

        var output = $"frame_{Guid.NewGuid():N}.png";

        try
        {
            await _ffmpeg.ExecAsync(ExportArgBuilders.BuildStillFrameArgs(source, output, sourceSeconds));

            var name    = $"frame-{FormatTimecode(timelineSeconds)}.png";
            var blobUrl = await _ffmpeg.CreatePreviewUrlAsync(output, "image/png");
            await _ffmpeg.DownloadBlobUrlAsync(blobUrl, name);
            return (name, null);
        }
        catch (Exception ex)
        {
            _errorLog.Log("ExportService.SaveFrameAsync", $"Could not save the frame: {ex.Message}", ex.ToString());
            return (null, $"Could not save the frame: {ex.Message}");
        }
        finally
        {
            try { await _ffmpeg.DeleteFileAsync(output); } catch { }
        }
    }

    /// <summary>A filename-safe timecode, so two frames from one project never collide.</summary>
    private static string FormatTimecode(double seconds)
    {
        var t = TimeSpan.FromSeconds(Math.Max(0, seconds));
        return $"{(int)t.TotalMinutes:D2}m{t.Seconds:D2}s{t.Milliseconds:D3}";
    }

    // â”€â”€ Pipeline â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

    private async Task RunPipelineAsync(ExportJob job, bool downloadToDisk = true)
    {
        job.State = ExportJobState.Running;

        // The primary track is the sequence; everything above it is composited over that sequence
        // by ComposeVideoLayersAsync. Images used to be gathered from EVERY video track and folded
        // into this one list, which flattened a picture placed on track 2 into the main sequence
        // and pushed the rest of the timeline along by its length.
        var primary    = _clips.PrimaryVideoTrack;
        var videoClips = primary.VideoClips
                               .OrderBy(c => c.TimelinePosition)
                               .ToList();
        var imageClips = primary.ImageClips
                               .OrderBy(c => c.TimelinePosition)
                               .ToList();
        var extraVideoTracks = _clips.VideoTracks.Where(t => t.Id != primary.Id).ToList();

        if (!_clips.HasExportableContent)
            throw new InvalidOperationException("No clips on the timeline to export.");

        var s         = job.Settings;
        var tempFiles = new List<string>();

        try
        {
            // ── Phase 1: Render all timeline segments (video + image) in order ─
            // A cross-track transition pass used to run before this one, replacing the first
            // clip's segment with a merged crossfade. It produced a segment longer than the two it
            // replaced while every later offset stayed measured against the old length, so
            // everything after the junction drifted — see IsCrossTrack (2026-09-05 audit,
            // transitions-9).
            var videoSegments = await TrimSegmentsAsync(job, videoClips, s, tempFiles, []);
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
            var placed = videoClips
                .Select((c, i) => (Segment: videoSegments[i], ClipId: c.Id, Start: c.TimelinePosition,
                                   Duration: c.EffectiveDuration > 0 ? c.EffectiveDuration : c.Duration))
                .Concat(imageClips.Select((c, i) => (Segment: imageSegments[i], ClipId: c.Id,
                                                     Start: c.TimelinePosition,
                                                     Duration: c.Duration > 0 ? c.Duration : 5.0)))
                .ToList();

            // The plan is where the gaps become real. Everything downstream — the audio mix, the
            // overlays, the chapter marks — is positioned in timeline time, so an export that
            // closed its gaps put all of them against the wrong picture from the first gap onward
            // (2026-09-05 audit, export-2).
            var plan = ExportSegmentPlanner.Plan(placed);
            ThrowIfCancelled(job);

            plan = await RenderFillerSegmentsAsync(job, plan, s, tempFiles);

            var allSegments         = plan.Select(x => x.Segment).ToList();
            var allSegmentDurations = plan.Select(x => x.Duration).ToList();

            // Which transition belongs to which junction, decided by the clips it names rather
            // than by its position in the list. Matching by index meant one transition anywhere on
            // the track gave every other junction an unrequested one-second fade (transitions-2).
            var junctions = ExportSegmentPlanner.MatchTransitions(
                plan, _clips.AllTransitions.Where(t => !IsCrossTrack(t)));

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
            var hasXfadeTransitions = junctions.Any(t => t is not null);

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
                composited = await ApplyTransitionsAsync(
                    job, allSegments, allSegmentDurations, junctions, s, tempFiles);
                ThrowIfCancelled(job);
            }
            else
            {
                composited = await ConcatSegmentsAsync(job, allSegments, s, tempFiles);
                ThrowIfCancelled(job);
            }

            // ── Phase 2b: Tracks above the primary one ───────────────────────
            // Composited before the overlays so a title or callout still draws on top of
            // everything, which is what the timeline's own row order means.
            if (extraVideoTracks.Count > 0)
            {
                composited = await ComposeVideoLayersAsync(job, composited, extraVideoTracks, s, tempFiles);
                ThrowIfCancelled(job);
            }

            // ── Phase 3: Titles, callouts and clip art ───────────────────────
            // One pass, bottom layer first, across all three kinds. A project's overlays are
            // rendered whatever this host lets people create: gating the render on the host flag
            // meant a title made on the site silently vanished from an export run anywhere else
            // (2026-09-05 audit, titles-11).
            composited = await ApplyOverlaysAsync(job, composited, s, tempFiles);
            ThrowIfCancelled(job);


            // â”€â”€ Phase 4: Audio mix â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
            // Skipped when the native assemble already mixed audio (item #70 phase 162) —
            // re-running it here would amix the standalone clips a SECOND time on top of the
            // already-mixed track, audibly doubling them.
            // The project's audio tracks are mixed whether or not this host offers the button
            // that creates them (2026-09-05 audit, F2/titles-11 class).
            if (_clips.AudibleAudioClips.Any() && s.IncludeAudio && !_nativeAssembleMixedAudio)
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

            // ── Phase 4.6: The container the person chose ─────────────────────
            outputName = await FinaliseContainerAsync(job, outputName, s, tempFiles);
            ThrowIfCancelled(job);

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

            // The canvas every segment lands on, whichever engine renders it.
            var (fxW, fxH) = ResolveCanvas(s);

            // Item #38 phase 124 — offload this one clip's trim/encode to the native sidecar
            // when it's connected, via the exact same ExportArgBuilders.BuildTrimArgs the wasm
            // path below calls, so the resulting segment is structurally identical either way.
            // TryEncodeVideoSegmentAsync never throws: a dead sidecar, an unsupported codec, or a
            // clip with no OPFS source just falls straight through to the unchanged wasm path —
            // this one clip renders in the browser instead, the export itself never fails or
            // reruns because of it.
            var nativeBytes = _nativeSidecar.IsConnected
                ? await _nativeClipEncoder.TryEncodeVideoSegmentAsync(
                    clip, s, job.CancellationToken, fxW, fxH)
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
                // fxW/fxH is also the canvas an effect runs against: zoompan needs a literal
                // size and cannot be told one as an expression (see ZoompanFragment).
                var appliedVf = ExportArgBuilders.BuildAppliedEffectsFilter(
                    clip.AppliedEffects, _effectRegistry, effectiveDuration, clip.Speed, fxW, fxH);
                // Muting the video track silences its clips, the same as muting each of them.
                var args = BuildTrimArgs(clip.MemFsName, segName, start, end, clip.Speed, s, volumeFilter, clip.Effects, !_clips.IsAudible(clip),
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
    /// <summary>
    /// Transitions that span two tracks are ignored.
    /// </summary>
    /// <remarks>
    /// <para>There used to be a pass here that rendered them. It replaced the first clip's segment
    /// with a merged one whose length is fromDur + toDur − overlap, while every later offset was
    /// still measured against the original length — so everything after the junction drifted, and
    /// the preview never showed any of it. The way to fade a clip in or out is the clip's own
    /// FadeInSeconds/FadeOutSeconds, which this pipeline already renders and a project already
    /// saves (2026-09-05 audit, transitions-9).</para>
    ///
    /// <para>A project made before this can still hold one. It is skipped rather than rendered
    /// wrongly, which is why <see cref="IsCrossTrack"/> stays.</para>
    /// </remarks>
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

            var (fxImgW, fxImgH) = ResolveCanvas(s);

            // Item #38 phase 124 — same native offload as TrimSegmentsAsync above, for image
            // clips. See that method's comment for the fall-through contract.
            var nativeBytes = _nativeSidecar.IsConnected
                ? await _nativeClipEncoder.TryEncodeImageSegmentAsync(
                    clip, s, job.CancellationToken, fxImgW, fxImgH)
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
                    clip.AppliedEffects, _effectRegistry, duration, 1.0, fxImgW, fxImgH);
                // Item #9 — scale/pad to the PROJECT's output resolution, not the image's own
                // source size. Passing clip.Width/clip.Height made the filter
                // "scale={imgW}:{imgH},pad={imgW}:{imgH}" — a no-op that left every image segment
                // at its native resolution, which is exactly the reported symptom. Every other
                // segment path in this file already derives its canvas from the export settings;
                // this was the one that didn't.
                var (imgW, imgH) = ResolveCanvas(s);
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
            ? _clips.AudibleAudioClips.ToList()
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

    /// <summary>
    /// Rewrites the finished render into the chosen container, with the flags that container wants.
    /// </summary>
    /// <remarks>
    /// A stream copy, so nothing is re-encoded. Everything upstream works in mp4 intermediates and
    /// the last step used to be a rename, which meant a WebM export was an MP4 file with a .webm
    /// name (2026-09-05 audit, export-14). A failure here leaves the previous file in place: the
    /// render is finished and correct, and the wrong extension is better than no export.
    /// </remarks>
    private async Task<string> FinaliseContainerAsync(
        ExportJob job, string input, ExportSettings s, List<string> tempFiles)
    {
        Advance(job, 92, "Writing the file…");

        var remuxed = $"container_{job.Id:N}.{s.OutputFormat}";

        try
        {
            await _ffmpeg.ExecAsync(
                ExportArgBuilders.BuildContainerArgs(input, remuxed, s), job.CancellationToken);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            job.Warnings.Add($"Could not rewrite the file as {s.OutputFormat.ToUpperInvariant()}: {ex.Message}");
            return input;
        }

        var final = $"{SanitiseFilename(s.OutputFilename)}.{s.OutputFormat}";
        if (tempFiles.Remove(input)) await _ffmpeg.DeleteFileAsync(input);
        else await _ffmpeg.DeleteFileAsync(input);

        await RenameAsync(remuxed, final);
        job.CompletedPhases.Add($"Written as {s.OutputFormat.ToUpperInvariant()}");
        return final;
    }

    /// <summary>
    /// Renders the gaps the plan asked for, so the returned plan is all real files.
    /// </summary>
    private async Task<IReadOnlyList<ExportSegment>> RenderFillerSegmentsAsync(
        ExportJob job, IReadOnlyList<ExportSegment> plan, ExportSettings s, List<string> tempFiles)
    {
        if (!plan.Any(p => p.Kind == ExportSegmentKind.Filler)) return plan;

        var (vw, vh) = ResolveCanvas(s);
        var filled   = new List<ExportSegment>(plan.Count);
        var index    = 0;

        foreach (var segment in plan)
        {
            if (segment.Kind != ExportSegmentKind.Filler)
            {
                filled.Add(segment);
                continue;
            }

            var name = $"gap_{job.Id:N}_{index++:D3}.mp4";
            tempFiles.Add(name);

            await _ffmpeg.ExecAsync(
                ExportArgBuilders.BuildFillerSegmentArgs(name, segment.Duration, s, vw, vh),
                job.CancellationToken);

            filled.Add(segment with { Segment = name });
        }

        job.CompletedPhases.Add($"Rendered {index} gap(s)");
        return filled;
    }

    private async Task<string> ApplyTransitionsAsync(
        ExportJob job, List<string> segments, List<double> segmentDurations,
        IReadOnlyList<Transition?> junctions, ExportSettings s, List<string> tempFiles)
    {
        Advance(job, 50, "Applying transitions…");

        var outputName = $"transitioned_{job.Id:N}.mp4";
        tempFiles.Add(outputName);

        var filterArgs = ExportArgBuilders.BuildXfadeFilterComplex(
            segments, segmentDurations, junctions, withAudio: s.IncludeAudio);

        // Inputs
        var inputArgs = segments.SelectMany(s2 => new[] { "-i", s2 }).ToList();
        inputArgs.AddRange(["-filter_complex", filterArgs, "-map", "[vout]"]);

        // Mapping the picture and nothing else is what made every export with a transition come
        // out silent: the audio codec arguments below were applied to a stream that was never
        // selected (2026-09-05 audit, transitions-1).
        if (s.IncludeAudio) inputArgs.AddRange(["-map", "[aout]"]);

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
    /// <summary>
    /// Counter behind the temporary filenames the overlay passes generate, kept across the runs of
    /// one dispatch so two kinds never claim the same name.
    /// </summary>
    private int _overlayPassIndex;

    /// <summary>
    /// Draws every title, callout and piece of clip art over the picture, bottom layer first.
    /// </summary>
    /// <remarks>
    /// <para>The pipeline used to run three passes in a fixed order — all titles, then all
    /// callouts, then all clip art — so the stacking in the file was decided by what kind of thing
    /// each overlay was, not by the order they were added. The canvas stacks them by
    /// <c>LayerIndex</c> across all three kinds, so a title placed on top of a callout previewed on
    /// top and exported underneath (2026-09-05 audit, titles-9).</para>
    ///
    /// <para>Consecutive overlays of the same kind are handed to their pass together, because each
    /// pass already composites its list in order and each ffmpeg call costs a full re-encode of the
    /// whole video. Grouping runs rather than dispatching one at a time keeps the number of passes
    /// the same as before for the common case where overlays of a kind were added together.</para>
    /// </remarks>
    private async Task<string> ApplyOverlaysAsync(
        ExportJob job, string inputName, ExportSettings s, List<string> tempFiles)
    {
        var overlays = _clips.VideoTracks
            .SelectMany(t => t.Items)
            .Where(i => i is TextOverlay or CalloutClip or ClipArtClip)
            .OrderBy(i => i.LayerIndex)
            .ToList();

        if (overlays.Count == 0) return inputName;

        Advance(job, 68, $"Applying {overlays.Count} overlay(s)…");
        _overlayPassIndex = 0;

        var composited = inputName;
        var index      = 0;

        while (index < overlays.Count)
        {
            var kind = overlays[index].GetType();
            var run  = new List<TrackItem>();

            while (index < overlays.Count && overlays[index].GetType() == kind)
                run.Add(overlays[index++]);

            composited = run[0] switch
            {
                TextOverlay => await ApplyTextOverlaysAsync(
                    job, composited, s, tempFiles, [.. run.Cast<TextOverlay>()]),
                CalloutClip => await ApplyCalloutsAsync(
                    job, composited, s, tempFiles, [.. run.Cast<CalloutClip>()]),
                _           => await ApplyClipArtClipsAsync(
                    job, composited, s, tempFiles, [.. run.Cast<ClipArtClip>()]),
            };

            ThrowIfCancelled(job);
            Advance(job, ProgressInRange(index * 100 / overlays.Count, 100, 68, 80), "Applying overlays…");
        }

        job.CompletedPhases.Add($"Overlays applied ({overlays.Count})");
        Advance(job, 80, "Overlays applied.");
        return composited;
    }

    /// <summary>
    /// Composites a run of titles, in the order given.
    /// </summary>
    /// <remarks>
    /// The caller decides the order across all three overlay kinds; see
    /// <see cref="ApplyOverlaysAsync"/> for why that used to be decided by the pipeline instead.
    /// </remarks>
    private async Task<string> ApplyTextOverlaysAsync(
        ExportJob job, string inputName, ExportSettings s, List<string> tempFiles,
        IReadOnlyList<TextOverlay> overlays)
    {
        var composited = inputName;
        var (vw, vh)   = ResolveCanvas(s);

        var overlayIdx = _overlayPassIndex;
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
                    "-framerate", s.Fps.ToString("F2", CultureInfo.InvariantCulture),
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

                // InvariantCulture, not the browser's. A French or German locale formats 2.5 as
                // "2,5", and a comma inside enable='between(t,…)' is a filter ffmpeg cannot parse
                // — so animated overlays failed the export outright for anyone whose browser was
                // not set to English (2026-09-05 audit, titles-10).
                var filter = $"[1:v]scale={vw}:{vh}[ov];[0:v][ov]overlay=0:0:"
                           + $"enable='between(t,{startT.ToString("F3", CultureInfo.InvariantCulture)},"
                           + $"{endT.ToString("F3", CultureInfo.InvariantCulture)})'[out]";

                await _ffmpeg.ExecAsync(
                [
                    "-i", composited,
                    "-framerate", s.Fps.ToString("F2", CultureInfo.InvariantCulture),
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

        _overlayPassIndex = overlayIdx;
        return composited;
    }

    /// <summary>
    /// Composites a run of callouts, in the order given.
    /// </summary>
    /// <remarks>
    /// Every shape goes through the SVG renderer — the same code that draws it on the canvas.
    /// Rectangles and ellipses used to take an ffmpeg <c>drawbox</c> path instead, which cannot
    /// round a corner, cannot draw an ellipse at all and does not know what the preview's border
    /// or shadow look like, so the two most ordinary shapes were the two that came out wrong
    /// (2026-09-05 audit, callouts-2).
    /// </remarks>
    private async Task<string> ApplyCalloutsAsync(
        ExportJob job, string inputName, ExportSettings s, List<string> tempFiles,
        IReadOnlyList<CalloutClip> svgShapes)
    {
        var composited    = inputName;
        var (vw, vh)      = ResolveCanvas(s);

        // ── SVG-rendered shapes (via SvgFrameRendererService) ────────────────
        // Static shapes composite ONE PNG via a looped input (fades as alpha-fade filters);
        // only animated (motion-path) callouts render per-frame PNG sequences — see the
        // matching note on ApplyTextOverlaysAsync (backlog #29 memory fix).
        var clipIdx = _overlayPassIndex;
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
                    "-framerate", s.Fps.ToString("F2", CultureInfo.InvariantCulture),
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

                // InvariantCulture, not the browser's. A French or German locale formats 2.5 as
                // "2,5", and a comma inside enable='between(t,…)' is a filter ffmpeg cannot parse
                // — so animated overlays failed the export outright for anyone whose browser was
                // not set to English (2026-09-05 audit, titles-10).
                var filter = $"[1:v]scale={vw}:{vh}[ov];[0:v][ov]overlay=0:0:"
                           + $"enable='between(t,{startT.ToString("F3", CultureInfo.InvariantCulture)},"
                           + $"{endT.ToString("F3", CultureInfo.InvariantCulture)})'[out]";

                await _ffmpeg.ExecAsync(
                [
                    "-i", composited,
                    "-framerate", s.Fps.ToString("F2", CultureInfo.InvariantCulture),
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

        _overlayPassIndex = clipIdx;
        return composited;
    }

    /// <summary>
    /// Phase 3c: composite all <see cref="ClipArtClip"/> layers over the current video.
    /// Raster clips (PNG/AVIF/WebP) are written to MEMFS and overlaid statically.
    /// SVG clips are passed to <see cref="SvgAnimationExporter"/> which renders
    /// a PNG frame sequence then composites it via the overlay filter.
    /// </summary>
    private async Task<string> ApplyClipArtClipsAsync(
        ExportJob job, string inputName, ExportSettings s, List<string> tempFiles,
        IReadOnlyList<ClipArtClip> clips)
    {
        var composited = inputName;
        var clipIdx    = _overlayPassIndex;

        foreach (var clip in clips)
        {
            clipIdx++;
            var outputName = $"clipart_{job.Id:N}_{clipIdx}.mp4";
            tempFiles.Add(outputName);

            if (clip.AssetFormat == VideoAssetFormat.Svg && clip.ControlPoints is { Count: > 0 })
            {
                // SVG with control points — render frame-by-frame via SvgAnimationExporter
                var (width, height) = ResolveCanvas(s);
                var (args, writtenFiles) = await _svgExporter.RenderAsync(
                    clip, composited, s.Fps, width, height, tempFiles);

                if (args.Length == 0)
                {
                    // The asset could not be read. Saying so beats a render that quietly lacks it.
                    job.Warnings.Add($"'{clip.Name}' was left out: its artwork could not be read.");
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
                if (animated is null)
                {
                    job.Warnings.Add($"'{clip.Name}' was left out: its artwork could not be read.");
                    continue;
                }
            }
            else
            {
                // Raster (or simple SVG without animated control points) — static overlay
                var overlayFile = await WriteClipArtToMemFsAsync(clip);
                if (overlayFile is null)
                {
                    job.Warnings.Add($"'{clip.Name}' was left out: its artwork could not be read.");
                    continue;
                }
                tempFiles.Add(overlayFile);

                var (vw, vh) = ResolveCanvas(s);
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

        _overlayPassIndex = clipIdx;
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

        var (vw, vh) = ResolveCanvas(s);
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
                "-framerate", s.Fps.ToString("F2", CultureInfo.InvariantCulture),
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
        // A muted track is not mixed. The flag has always been documented as "audio suppressed
        // during playback and export"; nothing read it, so muting a track changed the icon and
        // nothing else (2026-09-05 audit, audio-5).
        var audioClips = _clips.AudibleAudioClips.ToList();

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

            // PCM, not the export's own codec: these exist only to be mixed, and the mix encodes
            // the result. Compressing them first put every audio clip through a lossy codec twice
            // (2026-09-05 audit, audio-24).
            var segName = $"audio_seg_{i:D3}_{job.Id:N}.wav";
            tempFiles.Add(segName);
            var segArgs = ExportArgBuilders.BuildAudioClipTrimArgs(
                ac.MemFsName, segName, start, end, fullFilter, s, lossless: true);
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

    /// <summary>
    /// Lays every track above the primary one over the picture, each clip at its own place on the
    /// timeline.
    /// </summary>
    /// <remarks>
    /// Nothing called the composite this replaces, so a clip on a second video track was visible on
    /// the timeline, visible in the properties panel, and absent from the file (2026-09-05 audit,
    /// export-1). Lower tracks are composited first so the topmost one ends up on top, matching the
    /// order the timeline draws them in.
    /// </remarks>
    private async Task<string> ComposeVideoLayersAsync(
        ExportJob              job,
        string                 baseLayer,
        List<TimelineTrack>    extraTracks,
        ExportSettings         s,
        List<string>           tempFiles)
    {
        var layers = extraTracks
            .SelectMany(t => TrackLayout.SequentialItems(t)
                .Where(i => i is VideoClip or ImageClip)
                .Select(i => (Track: t, Item: i)))
            .ToList();

        if (layers.Count == 0) return baseLayer;

        Advance(job, 67, $"Compositing {layers.Count} clip(s) from {extraTracks.Count} track(s)…");

        var (vw, vh)   = ResolveCanvas(s);
        var composited = baseLayer;
        var index      = 0;

        foreach (var (track, item) in layers)
        {
            ThrowIfCancelled(job);

            var segName     = $"layer_{index:D3}_{job.Id:N}.mp4";
            var segHasAudio = false;

            if (item is VideoClip clip)
            {
                if (clip.MemFsName is null)
                {
                    job.Warnings.Add($"'{clip.Name}' was left out: its media is not loaded.");
                    continue;
                }

                var trimStart = clip.StartTrim;
                var trimEnd   = clip.EndTrim > clip.StartTrim ? clip.EndTrim : clip.Duration;

                // A muted track means muted here as everywhere else.
                var muted   = track.IsMuted || clip.MuteAudio;
                segHasAudio = s.IncludeAudio && !muted && clip.HasAudio;

                tempFiles.Add(segName);
                await _ffmpeg.ExecAsync(ExportArgBuilders.BuildTrimArgs(
                    clip.MemFsName, segName, trimStart, trimEnd, clip.Speed, s,
                    ExportArgBuilders.BuildVolumeAutomationFilter(clip, trimEnd - trimStart),
                    clip.Effects, muteAudio: muted,
                    outputWidth: vw, outputHeight: vh, sourceHasAudio: clip.HasAudio),
                    job.CancellationToken);
            }
            else
            {
                var image = (ImageClip)item;
                if (image.MemFsName is null)
                {
                    job.Warnings.Add($"'{image.Name}' was left out: its media is not loaded.");
                    continue;
                }

                tempFiles.Add(segName);
                var appliedVf = ExportArgBuilders.BuildAppliedEffectsFilter(
                    image.AppliedEffects, _effectRegistry, item.EffectiveLength, 1.0, vw, vh);

                await _ffmpeg.ExecAsync(ExportArgBuilders.BuildImageSegmentArgs(
                    image.MemFsName, segName, item.EffectiveLength, s,
                    outputWidth: vw, outputHeight: vh, effects: image.Effects,
                    extraVf: string.IsNullOrEmpty(appliedVf) ? null : appliedVf),
                    job.CancellationToken);
            }

            var next = $"composite_{index:D3}_{job.Id:N}.mp4";
            tempFiles.Add(next);

            await _ffmpeg.ExecAsync(ExportArgBuilders.BuildLayerCompositeArgs(
                composited, segName, next, item.TimelinePosition, item.EffectiveLength, s,
                layerHasAudio: segHasAudio),
                job.CancellationToken);

            if (tempFiles.Remove(segName)) await _ffmpeg.DeleteFileAsync(segName);
            if (composited != baseLayer && tempFiles.Remove(composited))
                await _ffmpeg.DeleteFileAsync(composited);

            composited = next;
            index++;
        }

        if (index == 0) return baseLayer;

        job.CompletedPhases.Add($"Video layers composited ({index} clip(s))");
        Advance(job, 75, "Layer compositing complete.");
        return composited;
    }


    // â”€â”€ ffmpeg arg builders â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

    private string[] BuildTrimArgs(
        string input, string output, double start, double end, double speed, ExportSettings s,
        string? audioVolumeFilter = null, ClipEffects? effects = null, bool muteAudio = false,
        string? extraVf = null, bool sourceHasAudio = true)
    {
        var (vw, vh) = ResolveCanvas(s);
        return ExportArgBuilders.BuildTrimArgs(
            input, output, start, end, speed, s, audioVolumeFilter, effects, muteAudio, extraVf,
            outputWidth: vw, outputHeight: vh, sourceHasAudio: sourceHasAudio);
    }

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

    /// <summary>
    /// Moves the progress bar, never backwards.
    /// </summary>
    /// <remarks>
    /// The phases each announce a percentage of their own, and several of them start lower than
    /// the one before ended — the bar visibly ran backwards several times in a single job, which
    /// reads as the export restarting (2026-09-05 audit, export-8). A phase that would go
    /// backwards keeps the number and changes only the label, so the words still say what is
    /// happening. Reaching 100 is the exception: the end of the job is allowed to say so however
    /// it arrives.
    /// </remarks>
    private static void Advance(ExportJob job, int pct, string label)
    {
        job.OverallPercent = pct >= 100 ? 100 : Math.Max(job.OverallPercent, pct);
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
    /// <summary>
    /// The canvas for a settings object with no clips to consult.
    /// </summary>
    /// <remarks>
    /// Prefer the instance <see cref="ResolveCanvas"/>, which can answer "source resolution"
    /// honestly. This overload keeps the old behaviour for callers that have no timeline.
    /// </remarks>
    internal static (int w, int h) ParseResolution(string resolution)
        => ExportCanvas.Resolve(resolution);

    /// <summary>
    /// The canvas this export renders at, resolving "source resolution" against the first clip on
    /// the timeline rather than quietly rescaling everything to Full HD (2026-09-05 audit,
    /// export-5).
    /// </summary>
    private (int w, int h) ResolveCanvas(ExportSettings s)
    {
        if (!string.IsNullOrWhiteSpace(s.Resolution))
            return ExportCanvas.Resolve(s.Resolution);

        var first = TrackLayout.SequentialItems(_clips.PrimaryVideoTrack)
            .Select(i => i switch
            {
                VideoClip v => (v.Width, v.Height),
                ImageClip i2 => (i2.Width, i2.Height),
                _ => (0, 0),
            })
            .FirstOrDefault(d => d.Item1 > 0 && d.Item2 > 0);

        return ExportCanvas.Resolve(s.Resolution, first.Item1, first.Item2);
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

        var (vw, vh) = ResolveCanvas(s);
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
