using Ben.Video.Editor.Effects;
using Ben.Video.Editor.Models;

namespace Ben.Video.Editor.Services;

/// <summary>
/// Pure, stateless ffmpeg argument builders extracted from ExportService so that
/// Ben.Video.Tests can exercise every decision branch without mocking any service.
/// Exposed as internal via [assembly: InternalsVisibleTo("Ben.Video.Tests")].
/// </summary>
internal static class ExportArgBuilders
{
    // ── Stream-copy concat (item #70 phase 160) ──────────────────────────────

    /// <summary>
    /// The concat-demuxer, stream-copy argv — mirrors <c>ffmpegInterop.js concatCopy</c>.
    ///
    /// <para>Unlike the thumbnail argv (which lives only in JS and has to be kept honest by a
    /// fixture test), this one is <b>shared by construction</b>: the sidecar's concat job calls
    /// this exact method via <c>InternalsVisibleTo</c>, so browser and sidecar cannot drift.</para>
    ///
    /// <para><c>-safe 0</c> is required because the list file references paths ffmpeg considers
    /// unsafe; the list itself is written by the caller (MEMFS in the browser, the job workspace in
    /// the sidecar) since only the caller knows where its files live.</para>
    /// </summary>
    /// <summary>
    /// Re-encoding concat over a list file — mirrors <c>ffmpegInterop.js concatClips</c> with no
    /// scale arguments. Export never scales at concat time (each segment was already produced at
    /// the export's target size), unlike the preview path which passes a scaleTo.
    /// </summary>
    internal static string[] BuildConcatEncodeArgs(string listPath, string output, ExportSettings s) =>
    [
        "-f", "concat", "-safe", "0", "-i", listPath,
        ..QualityArgs(s),
        output,
    ];

    /// <summary>
    /// The amix assembly: video passed through by stream copy, its own audio mixed with N
    /// already-trimmed/positioned audio segments — item #70 phase 162, extracted verbatim from
    /// <c>ExportService.MixAudioTracksAsync</c> so the sidecar and the browser run the identical
    /// command.
    ///
    /// <para>Each audio segment is expected to be a normal audio-only output that has ALREADY had
    /// its trim, volume/automation, fades and <c>adelay</c> positioning applied (see
    /// <see cref="BuildAudioClipTrimArgs"/>). amix therefore needs no offset math of its own —
    /// that separation is what makes this step safe to move across the process boundary.</para>
    ///
    /// <para><c>inputs</c> is <c>N+1</c> because input 0 is the video's own audio track, which
    /// participates in the mix alongside the standalone audio clips.</para>
    /// </summary>
    /// <param name="videoHasAudio">
    /// Whether the assembled video carries an audio stream of its own. Referencing <c>[0:a]</c>
    /// when it does not is a graph ffmpeg refuses to build, which is what made "Separate Audio" on
    /// the only clip, and any slideshow with music, fail outright (2026-09-05 audit, audio-1).
    /// </param>
    internal static string[] BuildAmixArgs(
        string videoInput, IReadOnlyList<string> audioSegments, string output, ExportSettings s,
        bool videoHasAudio = true)
    {
        var args = new List<string> { "-i", videoInput };
        var labels = new List<string>();
        foreach (var segment in audioSegments)
        {
            args.Add("-i");
            args.Add(segment);
            labels.Add($"[{labels.Count + 1}:a]");
        }

        var inputs = labels.Count + (videoHasAudio ? 1 : 0);
        var chain  = (videoHasAudio ? "[0:a]" : string.Empty) + string.Join("", labels);

        // amix's defaults are wrong for an edit. normalize=1 divides every input by the number of
        // inputs, so adding one music track quietly dropped the dialogue by about 6 dB — the track
        // people add last is the one that made everything else quieter. dropout_transition then
        // swelled the remaining inputs back up over two seconds each time one ended. Both are
        // sensible for live mixing and neither is what a timeline means: a clip at full volume
        // stays at full volume (2026-09-05 audit, audio-3).
        //
        // Summing instead of averaging can exceed full scale, so the limiter catches the peaks the
        // old division used to hide. It is transparent until something would have clipped.
        var filter = $"{chain}amix=inputs={inputs}:duration=longest:normalize=0:dropout_transition=0[amixed];"
                   + "[amixed]alimiter=limit=0.95[aout]";

        args.AddRange([
            "-filter_complex", filter,
            "-map", "0:v",
            "-map", "[aout]",
            "-c:v", "copy",
            "-c:a", s.AudioCodec,
            "-b:a", $"{s.AudioBitrate}k",
            output,
        ]);
        return [.. args];
    }

    /// <summary>
    /// Writes the finished render into the container the person actually asked for, without
    /// re-encoding anything.
    /// </summary>
    /// <remarks>
    /// <para>The pipeline works in <c>.mp4</c> intermediates and the last step simply renamed the
    /// final one to the chosen extension. Choosing WebM therefore produced an MP4 file called
    /// <c>.webm</c> — the codecs inside were right, the container was not, and what happens next
    /// depends entirely on how forgiving the player is.</para>
    ///
    /// <para>A stream copy is cheap: no frame is decoded, so this costs a file rewrite rather than
    /// a second encode. It is also the only place the container-level flags can be set
    /// (2026-09-05 audit, export-14).</para>
    /// </remarks>
    internal static string[] BuildContainerArgs(string input, string output, ExportSettings s)
    {
        var args = new List<string> { "-i", input, "-c", "copy" };

        var isMp4Family = s.OutputFormat is "mp4" or "mov" or "m4v";

        // H.265 in an MP4 is tagged "hev1" by default, and QuickTime, Safari and most Apple
        // hardware will not play that. "hvc1" is the same bytes with the tag every consumer player
        // expects, so an export that opened nowhere on a Mac now opens everywhere.
        if (isMp4Family && s.VideoCodec is "libx265" or "hevc")
            args.AddRange(["-tag:v", "hvc1"]);

        // Moves the index to the front of the file, so a browser can start playing before the
        // whole thing has downloaded. For a render people upload and share, this is the difference
        // between playing at once and waiting for the last byte.
        if (isMp4Family)
            args.AddRange(["-movflags", "+faststart"]);

        args.Add(output);
        return [.. args];
    }

    /// <summary>
    /// One frame, as a picture.
    /// </summary>
    /// <param name="input">The clip to take it from.</param>
    /// <param name="output">The PNG to write.</param>
    /// <param name="sourceSeconds">Where in that clip's own timeline to take it from.</param>
    /// <remarks>
    /// For a site whose members are cutting evidence reels, the single frame where something
    /// appears is the thing that actually gets shared — more often than the clip it came from —
    /// and the editor could only produce video (2026-09-05 audit, the completeness critic's list).
    /// Seeking on the input rather than after it means ffmpeg decodes to the frame and stops,
    /// instead of decoding everything before it first.
    /// </remarks>
    internal static string[] BuildStillFrameArgs(string input, string output, double sourceSeconds)
    {
        var ic = System.Globalization.CultureInfo.InvariantCulture;
        return
        [
            "-ss", Math.Max(0, sourceSeconds).ToString("F3", ic),
            "-i", input,
            "-frames:v", "1",
            "-update", "1",
            output,
        ];
    }

    /// <summary>
    /// Rounds a canvas down to even dimensions.
    /// </summary>
    /// <remarks>
    /// H.264 and H.265 in 4:2:0 cannot encode an odd width or height. A 1007x675 photo — an
    /// ordinary size for a screenshot or a phone crop — was handed straight to the encoder as its
    /// own canvas, and ffmpeg aborted. In the browser that abort surfaced as nothing at all: the
    /// preview simply stopped updating and kept showing the timeline as it had been before the
    /// picture was added, which reads as the picture not having been added (2026-09-05 audit,
    /// found while verifying export-5 on screen).
    /// </remarks>
    internal static (int Width, int Height) EvenCanvas(int width, int height) =>
        (width > 0 ? width - (width % 2) : width,
         height > 0 ? height - (height % 2) : height);

    internal static string[] BuildConcatCopyArgs(string listPath, string output) =>
        ["-f", "concat", "-safe", "0", "-i", listPath, "-c", "copy", output];

    /// <summary>
    /// The body of the concat list file: one <c>file '&lt;name&gt;'</c> line per segment, in order.
    /// Kept beside the argv builder because the two are meaningless apart — same format the JS
    /// writes.
    /// </summary>
    internal static string BuildConcatListContent(IEnumerable<string> segmentNames) =>
        string.Join("\n", segmentNames.Select(n => $"file '{n}'"));

    // \u2500\u2500 Image segment \u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500

    /// <summary>
    /// Build ffmpeg args that convert a static image to a video segment of <paramref name="duration"/> seconds.
    /// Uses <c>-loop 1 -framerate 25 -i &lt;img&gt; -t &lt;duration&gt;</c> plus codec settings.
    /// The output is a video-only segment ready to concatenate with other segments.
    /// </summary>
    internal static string[] BuildImageSegmentArgs(
        string input, string output, double duration, ExportSettings s,
        int outputWidth = 0, int outputHeight = 0, ClipEffects? effects = null, string? extraVf = null)
    {
        var args = new List<string>
        {
            "-loop", "1",
            "-framerate", (s.Fps > 0 ? s.Fps : 30).ToString(),
            "-i", input,
        };

        // A picture has no sound, but the segment still needs an audio stream or concat cannot
        // join it to the clips around it — which is how a slideshow with a music track came out
        // silent (2026-09-05 audit, export-3 and audio-2).
        if (s.IncludeAudio)
            args.AddRange(["-f", "lavfi", "-i", "anullsrc=channel_layout=stereo:sample_rate=48000"]);

        args.AddRange(["-t", duration.ToString("F3", System.Globalization.CultureInfo.InvariantCulture)]);

        var filterParts = new List<string>();
        (outputWidth, outputHeight) = EvenCanvas(outputWidth, outputHeight);

        if (outputWidth > 0 && outputHeight > 0)
        {
            filterParts.Add($"scale={outputWidth}:{outputHeight}:force_original_aspect_ratio=decrease");
            filterParts.Add($"pad={outputWidth}:{outputHeight}:(ow-iw)/2:(oh-ih)/2");
        }

        if (effects is not null && !effects.IsNeutral)
        {
            var fx = BuildVideoEffectsFilter(effects, clipDuration: duration, speed: 1.0);
            if (!string.IsNullOrEmpty(fx))
                filterParts.Add(fx);
        }

        if (!string.IsNullOrEmpty(extraVf))
            filterParts.Add(extraVf);

        // An empty "-vf" value is an invalid ffmpeg argument that fails the whole exec —
        // and since the JS exec() wrapper never checks the exit code, that failure was
        // silent, surfacing later as an unrelated "file not found" when reading the
        // (never-written) output. Only emit -vf when there's an actual filter to apply.
        if (filterParts.Count > 0)
            args.AddRange(["-vf", string.Join(",", filterParts)]);
        args.AddRange(["-c:v", s.VideoCodec]);

        if (s.UseCrf)
            args.AddRange(["-crf", s.Crf.ToString()]);
        else
            args.AddRange(["-b:v", $"{s.Bitrate}k"]);

        if (!string.IsNullOrEmpty(s.Preset) && s.VideoCodec is "libx264" or "libx265")
            args.AddRange(["-preset", s.Preset]);

        args.AddRange(["-pix_fmt", s.PixelFormat]);

        if (s.IncludeAudio)
            args.AddRange(["-c:a", s.AudioCodec, "-b:a", $"{s.AudioBitrate}k", "-shortest"]);
        else
            args.Add("-an");

        args.Add(output);
        return [.. args];
    }

    /// <summary>
    /// A stretch of black and silence, standing in for a gap on the timeline.
    /// </summary>
    /// <remarks>
    /// Gaps used to be closed on export: segments were concatenated back to back while the audio,
    /// the overlays and the chapter marks all kept their timeline positions, so everything after
    /// the first gap played against the wrong picture (2026-09-05 audit, export-2). Rendering the
    /// gap is what keeps the export the same length as the timeline that produced it.
    /// </remarks>
    internal static string[] BuildFillerSegmentArgs(
        string output, double duration, ExportSettings s, int outputWidth, int outputHeight)
    {
        var ic  = System.Globalization.CultureInfo.InvariantCulture;
        var fps = s.Fps > 0 ? s.Fps : 30;
        var (ew, eh) = EvenCanvas(outputWidth, outputHeight);
        var w = ew > 0 ? ew : 1920;
        var h = eh > 0 ? eh : 1080;

        var args = new List<string>
        {
            "-f", "lavfi", "-i", $"color=c=black:s={w}x{h}:r={fps}",
        };

        if (s.IncludeAudio)
            args.AddRange(["-f", "lavfi", "-i", "anullsrc=channel_layout=stereo:sample_rate=48000"]);

        args.AddRange(["-t", duration.ToString("F3", ic)]);
        args.AddRange(QualityArgs(s));
        args.AddRange(["-pix_fmt", s.PixelFormat]);

        if (s.IncludeAudio)
            args.AddRange(["-c:a", s.AudioCodec, "-b:a", $"{s.AudioBitrate}k"]);
        else
            args.Add("-an");

        args.Add(output);
        return [.. args];
    }

    // ── Trim ─────────────────────────────────────────────────────────────────

    internal static string[] BuildTrimArgs(
        string input, string output, double start, double end, double speed, ExportSettings s,
        string? audioVolumeFilter = null, ClipEffects? effects = null, bool muteAudio = false,
        string? extraVf = null, int outputWidth = 0, int outputHeight = 0,
        bool sourceHasAudio = true)
    {
        // Every segment carries an audio stream when the export includes audio — a real one, or
        // silence. Mixed stream layouts are what broke concat: an image or a muted clip was written
        // with "-an", and joining those to clips that do have sound dropped the audio from the
        // result or failed outright. A slideshow with music was the everyday case (2026-09-05
        // audit, export-3 and audio-2). BuildBackgroundRenderVideoArgs has always done this; the
        // real export path never did.
        var hasRealAudio = s.IncludeAudio && !muteAudio && sourceHasAudio;

        // Seek BEFORE the input, not after. With "-i input -ss start", the filter graph sees the
        // source's own timestamps — so a volume envelope or a fade computed against the trimmed
        // length was applied to the head that had just been discarded (2026-09-05 audit, audio-4).
        var args = new List<string>
        {
            "-ss",  start.ToString("F3", System.Globalization.CultureInfo.InvariantCulture),
            "-to",  end.ToString("F3", System.Globalization.CultureInfo.InvariantCulture),
            "-i",   input,
        };

        if (s.IncludeAudio && !hasRealAudio)
            args.AddRange(["-f", "lavfi", "-i", "anullsrc=channel_layout=stereo:sample_rate=48000"]);

        // Build the video filter chain: scale/pad (resolution) → setpts (speed) → eq (colour) → fade
        var videoFilters = new List<string>();

        // Video segments previously encoded at the SOURCE clip's native resolution regardless
        // of the export's selected Resolution setting — only image segments and overlay PNGs
        // (text/callout/clipart, always rendered at the target resolution) actually honoured
        // it. A source smaller than the target left overlays positioned/clipped against a
        // canvas the real video frame didn't match (e.g. a callout's "10% from top" landed at
        // an absolute pixel row past the actual frame's bottom edge) — and a source LARGER
        // than the target silently ignored the user's requested resolution entirely. Matches
        // BuildImageSegmentArgs' existing scale+pad so every segment kind lands on the same
        // canvas before compositing.
        (outputWidth, outputHeight) = EvenCanvas(outputWidth, outputHeight);

        if (outputWidth > 0 && outputHeight > 0)
        {
            videoFilters.Add($"scale={outputWidth}:{outputHeight}:force_original_aspect_ratio=decrease");
            videoFilters.Add($"pad={outputWidth}:{outputHeight}:(ow-iw)/2:(oh-ih)/2");
        }

        if (Math.Abs(speed - 1.0) > 0.001)
        {
            var setpts = (1.0 / speed).ToString("F6", System.Globalization.CultureInfo.InvariantCulture);
            videoFilters.Add($"setpts={setpts}*PTS");
        }

        if (effects is not null && !effects.IsNeutral)
        {
            var effectsFilter = BuildVideoEffectsFilter(
                effects, clipDuration: end - start, speed: speed);
            if (!string.IsNullOrEmpty(effectsFilter))
                videoFilters.Add(effectsFilter);
        }

        if (!string.IsNullOrEmpty(extraVf))
            videoFilters.Add(extraVf);

        if (videoFilters.Count > 0)
            args.AddRange(["-filter:v", string.Join(",", videoFilters)]);
        // (no change when list is empty — keeps previous no-filter behaviour)

        args.AddRange(["-c:v", s.VideoCodec]);

        if (s.Fps > 0)
            args.AddRange(["-r", s.Fps.ToString()]);

        if (s.UseCrf)
            args.AddRange(["-crf", s.Crf.ToString()]);
        else
            args.AddRange(["-b:v", $"{s.Bitrate}k"]);

        if (!string.IsNullOrEmpty(s.Preset) &&
            s.VideoCodec is "libx264" or "libx265")
            args.AddRange(["-preset", s.Preset]);

        args.AddRange(["-pix_fmt", s.PixelFormat]);

        // A source with no audio stream is a third case, and treating it as "has audio" is fatal
        // rather than cosmetic: "-filter:a volume=…" against a stream that is not there aborts the
        // whole command. In ffmpeg.wasm that abort is what leaves a preview render apparently
        // frozen. Selecting a clip on the timeline runs exactly this builder.
        if (hasRealAudio)
        {
            // Build composite audio filter chain: [atempo chain] + [volume automation]
            // atempo is limited to [0.5, 2.0] per filter instance.
            var audioFilters = new List<string>();

            if (Math.Abs(speed - 1.0) > 0.001)
                audioFilters.Add(BuildAtempoChain(speed));

            if (!string.IsNullOrEmpty(audioVolumeFilter))
                audioFilters.Add(audioVolumeFilter);

            if (audioFilters.Count > 0)
                args.AddRange(["-filter:a", string.Join(",", audioFilters)]);

            args.AddRange(["-c:a", s.AudioCodec, "-b:a", $"{s.AudioBitrate}k"]);
        }
        else if (s.IncludeAudio)
        {
            // Silence from the anullsrc input above, cut to this segment's length so the streams
            // end together.
            args.AddRange(["-map", "0:v:0", "-map", "1:a:0", "-shortest"]);
            args.AddRange(["-c:a", s.AudioCodec, "-b:a", $"{s.AudioBitrate}k"]);
        }
        else
            args.Add("-an");

        args.Add(output);
        return [.. args];
    }

    // ── Background-render (item #36 phase C) — always-present-audio variants ───────────────────

    /// <summary>
    /// Same trim/effects/scale behavior as <see cref="BuildTrimArgs"/>, but ALWAYS emits an audio
    /// stream — a synthetic silent one (<c>anullsrc</c>) when the clip has none — so every segment
    /// the background render worker produces shares a consistent stream layout and can be
    /// stream-copy concatenated regardless of which clips actually have audio. Never used by the
    /// real export pipeline or the synchronous Preview path (<see cref="BuildTrimArgs"/>) — a
    /// deliberately separate builder so this doesn't risk regressing either of those.
    /// </summary>
    internal static string[] BuildBackgroundRenderVideoArgs(
        string input, string output, double start, double end, double speed, ExportSettings s,
        string? audioVolumeFilter = null, ClipEffects? effects = null, bool muteAudio = false,
        string? extraVf = null, int outputWidth = 0, int outputHeight = 0,
        bool sourceHasAudio = true)
    {
        // Three separate things decide whether real audio is used, and only two of them used to
        // be consulted. A source with no audio stream at all — a screen recording, a trail
        // camera, an exported animation — took the "has audio" branch and ended up with
        // "-map 0:a", which ffmpeg refuses outright: "Stream map '0:a' matches no streams".
        // The command never runs, and in the wasm worker that presents as a background render
        // stuck at a percentage with Export disabled behind it, rather than as an error anyone
        // can see.
        var hasRealAudio = s.IncludeAudio && !muteAudio && sourceHasAudio;

        var args = new List<string> { "-i", input };
        if (!hasRealAudio)
            args.AddRange(["-f", "lavfi", "-i", "anullsrc=channel_layout=stereo:sample_rate=48000"]);

        args.AddRange(["-ss", start.ToString("F3"), "-to", end.ToString("F3")]);

        var videoFilters = new List<string>();
        (outputWidth, outputHeight) = EvenCanvas(outputWidth, outputHeight);

        if (outputWidth > 0 && outputHeight > 0)
        {
            videoFilters.Add($"scale={outputWidth}:{outputHeight}:force_original_aspect_ratio=decrease");
            videoFilters.Add($"pad={outputWidth}:{outputHeight}:(ow-iw)/2:(oh-ih)/2");
        }

        if (Math.Abs(speed - 1.0) > 0.001)
        {
            var setpts = (1.0 / speed).ToString("F6", System.Globalization.CultureInfo.InvariantCulture);
            videoFilters.Add($"setpts={setpts}*PTS");
        }

        if (effects is not null && !effects.IsNeutral)
        {
            var effectsFilter = BuildVideoEffectsFilter(effects, clipDuration: end - start, speed: speed);
            if (!string.IsNullOrEmpty(effectsFilter))
                videoFilters.Add(effectsFilter);
        }

        if (!string.IsNullOrEmpty(extraVf))
            videoFilters.Add(extraVf);

        if (videoFilters.Count > 0)
            args.AddRange(["-filter:v", string.Join(",", videoFilters)]);

        args.AddRange(["-c:v", s.VideoCodec]);
        if (s.Fps > 0)
            args.AddRange(["-r", s.Fps.ToString()]);
        if (s.UseCrf)
            args.AddRange(["-crf", s.Crf.ToString()]);
        else
            args.AddRange(["-b:v", $"{s.Bitrate}k"]);
        if (!string.IsNullOrEmpty(s.Preset) && s.VideoCodec is "libx264" or "libx265")
            args.AddRange(["-preset", s.Preset]);
        args.AddRange(["-pix_fmt", s.PixelFormat]);

        if (hasRealAudio)
        {
            var audioFilters = new List<string>();
            if (Math.Abs(speed - 1.0) > 0.001)
                audioFilters.Add(BuildAtempoChain(speed));
            if (!string.IsNullOrEmpty(audioVolumeFilter))
                audioFilters.Add(audioVolumeFilter);
            if (audioFilters.Count > 0)
                args.AddRange(["-filter:a", string.Join(",", audioFilters)]);
            args.AddRange(["-c:a", s.AudioCodec, "-b:a", $"{s.AudioBitrate}k"]);
            args.AddRange(["-map", "0:v", "-map", "0:a"]);
        }
        else
        {
            args.AddRange(["-c:a", s.AudioCodec, "-b:a", $"{s.AudioBitrate}k"]);
            args.AddRange(["-map", "0:v", "-map", "1:a", "-shortest"]);
        }

        args.Add(output);
        return [.. args];
    }

    /// <summary>Image-clip counterpart to <see cref="BuildBackgroundRenderVideoArgs"/> — image
    /// segments never have their own audio, so this always attaches the same silent
    /// <c>anullsrc</c> track.</summary>
    internal static string[] BuildBackgroundRenderImageArgs(
        string input, string output, double duration, ExportSettings s,
        int outputWidth = 0, int outputHeight = 0, ClipEffects? effects = null, string? extraVf = null)
    {
        var args = new List<string>
        {
            "-loop", "1",
            "-framerate", (s.Fps > 0 ? s.Fps : 30).ToString(),
            "-i", input,
            "-f", "lavfi", "-i", "anullsrc=channel_layout=stereo:sample_rate=48000",
            "-t", duration.ToString("F3", System.Globalization.CultureInfo.InvariantCulture),
        };

        var filterParts = new List<string>();
        (outputWidth, outputHeight) = EvenCanvas(outputWidth, outputHeight);

        if (outputWidth > 0 && outputHeight > 0)
        {
            filterParts.Add($"scale={outputWidth}:{outputHeight}:force_original_aspect_ratio=decrease");
            filterParts.Add($"pad={outputWidth}:{outputHeight}:(ow-iw)/2:(oh-ih)/2");
        }

        if (effects is not null && !effects.IsNeutral)
        {
            var fx = BuildVideoEffectsFilter(effects, clipDuration: duration, speed: 1.0);
            if (!string.IsNullOrEmpty(fx))
                filterParts.Add(fx);
        }

        if (!string.IsNullOrEmpty(extraVf))
            filterParts.Add(extraVf);

        if (filterParts.Count > 0)
            args.AddRange(["-vf", string.Join(",", filterParts)]);

        args.AddRange(["-c:v", s.VideoCodec]);
        if (s.UseCrf)
            args.AddRange(["-crf", s.Crf.ToString()]);
        else
            args.AddRange(["-b:v", $"{s.Bitrate}k"]);
        if (!string.IsNullOrEmpty(s.Preset) && s.VideoCodec is "libx264" or "libx265")
            args.AddRange(["-preset", s.Preset]);
        args.AddRange(["-pix_fmt", s.PixelFormat]);
        args.AddRange(["-c:a", s.AudioCodec, "-b:a", $"{s.AudioBitrate}k"]);
        args.AddRange(["-map", "0:v", "-map", "1:a", "-shortest"]);

        args.Add(output);
        return [.. args];
    }

    /// <summary>
    /// Builds a chained atempo filter string for the given speed multiplier.
    /// atempo accepts values in [0.5, 2.0]; values outside that range are
    /// achieved by chaining multiple instances.
    /// </summary>
    internal static string BuildAtempoChain(double speed)
    {
        speed = Math.Clamp(speed, 0.25, 4.0);
        var filters  = new List<string>();
        var remaining = speed;

        while (remaining > 2.0 + 1e-9)
        {
            filters.Add("atempo=2.0");
            remaining /= 2.0;
        }

        while (remaining < 0.5 - 1e-9)
        {
            filters.Add("atempo=0.5");
            remaining /= 0.5;
        }

        filters.Add($"atempo={remaining.ToString("F6", System.Globalization.CultureInfo.InvariantCulture)}");
        return string.Join(",", filters);
    }


    // -- Video effects filter (eq + fade) --

    /// <summary>
    /// Builds a comma-joined ffmpeg video filter string for a clips ClipEffects.
    /// Filter order: eq (colour grading), fade in, fade out.
    /// </summary>
    internal static string BuildVideoEffectsFilter(
        ClipEffects effects, double clipDuration, double speed = 1.0)
    {
        var vf = new List<string>();
        var ic = System.Globalization.CultureInfo.InvariantCulture;
        bool hb = Math.Abs(effects.Brightness) > 1e-6;
        bool hc = Math.Abs(effects.Contrast - 1.0) > 1e-6;
        bool hs = Math.Abs(effects.Saturation - 1.0) > 1e-6;
        if (hb || hc || hs)
        {
            var bStr   = effects.Brightness.ToString("F4", ic);
            var cStr   = effects.Contrast.ToString("F4", ic);
            var satStr = effects.Saturation.ToString("F4", ic);
            vf.Add("eq=brightness=" + bStr + ":contrast=" + cStr + ":saturation=" + satStr);
        }
        var ed = speed > 0 ? clipDuration / speed : clipDuration;
        if (effects.FadeInSeconds > 0)
        {
            var dStr = Math.Min(effects.FadeInSeconds, ed).ToString("F3", ic);
            vf.Add("fade=t=in:st=0:d=" + dStr);
        }
        if (effects.FadeOutSeconds > 0)
        {
            var fd   = Math.Min(effects.FadeOutSeconds, ed);
            var stStr = Math.Max(0, ed - fd).ToString("F3", ic);
            var dStr  = fd.ToString("F3", ic);
            vf.Add("fade=t=out:st=" + stStr + ":d=" + dStr);
        }
        return string.Join(",", vf);
    }

    // ── Applied effects filter (Phase 29) ──────────────────────────────

    /// <summary>
    /// Builds a comma-joined ffmpeg video filter string from a list of
    /// <see cref="AppliedEffect"/> instances resolved through <paramref name="registry"/>.
    /// Effects whose <see cref="IClipEffect.BuildFilterFragment"/> returns empty are skipped.
    /// Returns an empty string when no filters are produced.
    /// </summary>
    internal static string BuildAppliedEffectsFilter(
        IReadOnlyList<AppliedEffect> effects,
        ClipEffectRegistry registry,
        double clipDuration,
        double speed = 1.0,
        int canvasWidth = 0,
        int canvasHeight = 0)
    {
        if (effects.Count == 0) return string.Empty;

        var fragments = new List<string>(effects.Count);
        foreach (var applied in effects)
        {
            var def = registry.GetById(applied.EffectId);
            if (def is null) continue;
            var frag = def.BuildFilterFragment(
                applied.Parameters, clipDuration, speed, canvasWidth, canvasHeight);
            if (!string.IsNullOrEmpty(frag))
                fragments.Add(frag);
        }
        return string.Join(",", fragments);
    }

    // ── Volume automation filter ──────────────────────────────────────────

    /// <summary>
    /// Builds an ffmpeg <c>volume</c> filter expression for the given clip.
    /// <list type="bullet">
    ///   <item>No keyframes (or 1): returns <c>volume=X</c> using the scalar gain.</item>
    ///   <item>2+ keyframes: returns a piecewise linear <c>volume=eval=frame:volume=if(...)</c>
    ///     expression using the clip's absolute timestamps (seconds).</item>
    /// </list>
    /// <paramref name="clipDurationSeconds"/> is the post-trim, post-speed wall-clock duration.
    /// </summary>
    internal static string BuildVolumeAutomationFilter(
        IHasVolumeAutomation clip, double clipDurationSeconds)
    {
        var ic = System.Globalization.CultureInfo.InvariantCulture;

        if (clip.VolumeAutomation.Count < 2)
            return $"volume={clip.Volume.ToString("F6", ic)}";

        // Convert normalised positions to absolute seconds
        var kfs = clip.VolumeAutomation
            .OrderBy(k => k.Position)
            .Select(k => (t: k.Position * clipDurationSeconds, v: k.Volume))
            .ToList();

        // Build nested if() expression: if(lt(t,t1), lerp0, if(lt(t,t2), lerp1, ...lastVolume))
        // Segments: before first kf → hold first; between kfs → linear; after last kf → hold last
        static string Fmt(double d, System.Globalization.CultureInfo c) => d.ToString("F6", c);

        string BuildSegment(int i)
        {
            if (i >= kfs.Count - 1)
                return Fmt(kfs[^1].v, ic);

            var (t0, v0) = kfs[i];
            var (t1, v1) = kfs[i + 1];
            var span     = t1 - t0;
            // lerp: v0 + ((t - t0) / span) * (v1 - v0)
            var lerpExpr = span > 1e-9
                ? $"{Fmt(v0, ic)}+((t-{Fmt(t0, ic)})/{Fmt(span, ic)})*({Fmt(v1, ic)}-{Fmt(v0, ic)})"
                : Fmt(v0, ic);
            return $"if(lt(t\\,{Fmt(t1, ic)}),{lerpExpr},{BuildSegment(i + 1)})";
        }

        // Outer guard: before the first keyframe, hold the first volume
        var expr = kfs.Count > 0
            ? $"if(lt(t\\,{Fmt(kfs[0].t, ic)}),{Fmt(kfs[0].v, ic)},{BuildSegment(0)})"
            : Fmt(clip.Volume, ic);

        return $"volume=eval=frame:volume={expr}";
    }

    /// <summary>
    /// Builds a <c>pan</c> filter clause that scales the left and right channels of a stereo
    /// stream independently (backlog #10) — a multiplier layered on top of whatever
    /// <see cref="BuildVolumeAutomationFilter"/> already applies to the whole stream. Returns
    /// <c>null</c> when both are unity (1.0), so unbalanced clips add no filter at all.
    /// </summary>
    internal static string? BuildChannelBalanceFilter(double leftVolume, double rightVolume)
    {
        if (Math.Abs(leftVolume - 1.0) < 1e-6 && Math.Abs(rightVolume - 1.0) < 1e-6)
            return null;

        var ic = System.Globalization.CultureInfo.InvariantCulture;
        return $"pan=stereo|c0={leftVolume.ToString("F6", ic)}*c0|c1={rightVolume.ToString("F6", ic)}*c1";
    }

    /// <summary>
    /// Builds <c>afade</c> clause(s) for an <see cref="AudioClip"/>'s fade-in/out. Returns
    /// <c>null</c> when both are zero. <paramref name="clipDurationSeconds"/> is the post-trim
    /// duration — the fade-out start time is computed relative to it.
    /// </summary>
    internal static string? BuildAudioFadeFilter(
        double fadeInSeconds, double fadeOutSeconds, double clipDurationSeconds)
    {
        if (fadeInSeconds <= 0 && fadeOutSeconds <= 0) return null;

        var ic    = System.Globalization.CultureInfo.InvariantCulture;
        var parts = new List<string>();

        if (fadeInSeconds > 0)
            parts.Add($"afade=t=in:st=0:d={fadeInSeconds.ToString("F3", ic)}");

        if (fadeOutSeconds > 0)
        {
            var start = Math.Max(0, clipDurationSeconds - fadeOutSeconds);
            parts.Add($"afade=t=out:st={start.ToString("F3", ic)}:d={fadeOutSeconds.ToString("F3", ic)}");
        }

        return string.Join(",", parts);
    }

    /// <summary>
    /// Composes an <see cref="AudioClip"/>'s full per-clip audio filter chain for export: volume
    /// (scalar or automation), then channel balance, then fade in/out. Always non-empty — volume
    /// alone (<c>volume=1.000000</c>) is a legitimate no-op filter when nothing else applies.
    /// </summary>
    internal static string BuildAudioClipFilterChain(AudioClip clip, double clipDurationSeconds)
    {
        var parts = new List<string> { BuildVolumeAutomationFilter(clip, clipDurationSeconds) };

        var channelFilter = BuildChannelBalanceFilter(clip.LeftVolume, clip.RightVolume);
        if (channelFilter is not null) parts.Add(channelFilter);

        var fadeFilter = BuildAudioFadeFilter(clip.FadeInSeconds, clip.FadeOutSeconds, clipDurationSeconds);
        if (fadeFilter is not null) parts.Add(fadeFilter);

        return string.Join(",", parts);
    }

    /// <summary>
    /// Builds ffmpeg args to trim an audio-only source to [<paramref name="start"/>,
    /// <paramref name="end"/>) and apply <paramref name="audioFilter"/> (typically
    /// <see cref="BuildAudioClipFilterChain"/>'s output, optionally with an <c>adelay</c> clause
    /// appended for the clip's timeline position — see <c>ExportService.MixAudioTracksAsync</c>).
    /// No video stream is expected or produced (<c>-vn</c>).
    ///
    /// <para><b>-ss/-to sit BEFORE -i, and that ordering is load-bearing (item #70 phase 174).</b>
    /// As <i>output</i> options — where they used to be — ffmpeg applies them <i>after</i> the
    /// filter graph, on filtered timestamps. But every clause
    /// <see cref="BuildAudioClipFilterChain"/> produces is anchored to <b>clip-relative</b> time
    /// starting at zero (<c>afade=t=in:st=0</c>, the automation expression's <c>t</c> scaled by the
    /// post-trim duration), and <c>ExportService</c> then prepends the clip's timeline position as
    /// an <c>adelay</c>. Filtering before trimming therefore broke both ends of the chain:</para>
    /// <list type="bullet">
    ///   <item><description>An <c>adelay</c>ed clip was truncated by exactly its own delay:
    ///   <c>-to</c> counted the inserted silence against the clip's length, so a 3s clip placed at
    ///   2s on the timeline emitted 2s of silence and 1s of audio. A clip positioned at or past its
    ///   own end value became pure silence.</description></item>
    ///   <item><description>On any clip with <c>StartTrim &gt; 0</c> the fade-in and the volume
    ///   automation were applied to the discarded head of the source, so they were silently inert
    ///   in the finished export while every unit test still passed.</description></item>
    /// </list>
    /// <para>Seeking on the input instead hands the filter graph a stream that already starts at
    /// the clip's own zero, which is what the chain has always assumed. Measured against real
    /// ffmpeg both ways: for an untrimmed, undelayed clip the two orderings are equivalent (which
    /// is why this went unnoticed — that is the case the browser exercises most), and every other
    /// case only becomes correct with the seek on the input.</para>
    /// </summary>
    /// <param name="lossless">
    /// Write uncompressed PCM instead of the export's own audio codec. These segments exist only to
    /// be fed to the mix, which encodes the result — compressing them first meant every audio clip
    /// went through the codec twice, and lossy twice is audibly worse than lossy once for no gain
    /// (2026-09-05 audit, audio-24). The intermediate is larger, and it is deleted as soon as the
    /// mix has consumed it.
    /// </param>
    internal static string[] BuildAudioClipTrimArgs(
        string input, string output, double start, double end, string audioFilter, ExportSettings s,
        bool lossless = false)
    {
        var ic = System.Globalization.CultureInfo.InvariantCulture;

        var args = new List<string>
        {
            "-ss", start.ToString("F3", ic),
            "-to", end.ToString("F3", ic),
            "-i", input,
            "-vn",
            "-filter:a", audioFilter,
        };

        if (lossless) args.AddRange(["-c:a", "pcm_s16le"]);
        else          args.AddRange(["-c:a", s.AudioCodec, "-b:a", $"{s.AudioBitrate}k"]);

        args.Add(output);
        return [.. args];
    }

    // ── Xfade filter_complex

    /// <summary>
    /// The filter graph that joins every segment into one, blending the junctions that have a
    /// transition and cutting the rest.
    /// </summary>
    /// <param name="segments">The rendered segments, in the order they play.</param>
    /// <param name="segmentDurations">
    /// Parallel to <paramref name="segments"/>: the real encoded duration of each one. Junction
    /// offsets are accumulated from these — the code once assumed every segment was exactly five
    /// seconds long, which put the blend in the wrong place on any other timeline.
    /// </param>
    /// <param name="junctions">
    /// One entry per junction, from <see cref="ExportSegmentPlanner.MatchTransitions"/>. Null is a
    /// cut. Passing the transitions as a plain list instead was the defect: they were paired with
    /// junctions by position, so a single transition anywhere on the track gave every other
    /// junction an unrequested one-second fade (2026-09-05 audit, transitions-2).
    /// </param>
    /// <param name="withAudio">
    /// Whether the segments carry audio streams. When they do, the graph builds a matching audio
    /// chain and labels it <c>[aout]</c>; the caller must map it, or the export is silent.
    /// </param>
    internal static string BuildXfadeFilterComplex(
        IReadOnlyList<string> segments,
        IReadOnlyList<double> segmentDurations,
        IReadOnlyList<Transition?> junctions,
        bool withAudio)
    {
        ArgumentNullException.ThrowIfNull(segments);
        ArgumentNullException.ThrowIfNull(segmentDurations);
        ArgumentNullException.ThrowIfNull(junctions);

        if (segmentDurations.Count != segments.Count)
            throw new ArgumentException(
                $"segmentDurations count ({segmentDurations.Count}) must match segments count ({segments.Count}).",
                nameof(segmentDurations));

        if (segments.Count > 1 && junctions.Count != segments.Count - 1)
            throw new ArgumentException(
                $"junctions count ({junctions.Count}) must be one fewer than segments count ({segments.Count}).",
                nameof(junctions));

        if (segments.Count == 0) return string.Empty;

        var ic = System.Globalization.CultureInfo.InvariantCulture;
        var sb = new System.Text.StringBuilder();

        // A single segment still has to be labelled, because the caller maps [vout]/[aout].
        if (segments.Count == 1)
        {
            sb.Append("[0:v]null[vout]");
            if (withAudio) sb.Append(";[0:a]anull[aout]");
            return sb.ToString();
        }

        var prevV = "[0:v]";
        var prevA = "[0:a]";

        // How long the chain built so far runs. A crossfade overlaps its two clips, so it makes
        // the accumulated stream shorter than the sum of its parts by exactly its own duration —
        // which is also why the timeline shortens when one is added (see TrackLayout.AllowedOverlap).
        var accumulated = segmentDurations[0];

        for (var i = 0; i < segments.Count - 1; i++)
        {
            var last   = i == segments.Count - 2;
            var outV   = last ? "[vout]" : $"[x{i:D2}]";
            var outA   = last ? "[aout]" : $"[a{i:D2}]";
            var t      = junctions[i];

            if (t is null)
            {
                // A plain cut. Joining video and audio in one concat filter keeps the two chains
                // the same length, which is what lets the next junction's crossfade line up.
                sb.Append(withAudio
                    ? $"{prevV}{prevA}[{i + 1}:v][{i + 1}:a]concat=n=2:v=1:a=1{outV}{outA};"
                    : $"{prevV}[{i + 1}:v]concat=n=2:v=1:a=0{outV};");

                accumulated += segmentDurations[i + 1];
            }
            else
            {
                var dur    = t.Duration;
                var offset = accumulated - dur;

                sb.Append($"{prevV}[{i + 1}:v]xfade=transition={XfadeStyle(t.Style)}");
                sb.Append($":duration={dur.ToString("F2", ic)}:offset={offset.ToString("F2", ic)}{outV};");

                // The picture blends and the sound does not: that is what made every export with a
                // transition come out silent, because the caller could only map the one labelled
                // output the graph produced (2026-09-05 audit, transitions-1). acrossfade always
                // works on the tail of its first input, which is exactly where xfade's offset puts
                // the blend, so the two chains stay in step without a second offset to keep.
                if (withAudio)
                    sb.Append($"{prevA}[{i + 1}:a]acrossfade=d={dur.ToString("F2", ic)}{outA};");

                accumulated += segmentDurations[i + 1] - dur;
            }

            prevV = outV;
            prevA = outA;
        }

        return sb.ToString().TrimEnd(';');
    }

    /// <summary>
    /// Filter graph for a cross-track transition: two independently-rendered clip segments
    /// (input 0 = the lower/"from" track's clip, input 1 = the higher/"to" track's clip) are
    /// blended with ffmpeg's <c>xfade</c> filter. Unlike the same-track path
    /// (<see cref="BuildXfadeFilterComplex"/>), there's only ever one pair, so no cumulative
    /// offset bookkeeping across multiple segments is needed — <paramref name="offset"/> is
    /// simply where, within the "from" clip's own rendered/trimmed segment, the overlap begins.
    /// xfade plays input 0 solo up to <paramref name="offset"/>, blends for
    /// <paramref name="duration"/>, then plays input 1 solo for its own remaining length.
    /// </summary>
    internal static string BuildCrossTrackXfadeFilter(TransitionStyle style, double duration, double offset)
    {
        var ic = System.Globalization.CultureInfo.InvariantCulture;
        return $"[0:v][1:v]xfade=transition={XfadeStyle(style)}:duration={duration.ToString("F2", ic)}:offset={offset.ToString("F2", ic)}[vout]";
    }

    internal static string XfadeStyle(TransitionStyle style) => style switch
    {
        TransitionStyle.Fade        => "fade",
        TransitionStyle.Dissolve    => "dissolve",
        TransitionStyle.WipeLeft    => "wipeleft",
        TransitionStyle.WipeRight   => "wiperight",
        TransitionStyle.SlideLeft   => "slideleft",
        TransitionStyle.Zoom        => "zoom",
        // Item #57 T5 — curated extras; values are ffmpeg xfade's own transition names verbatim.
        TransitionStyle.CircleOpen  => "circleopen",
        TransitionStyle.CircleClose => "circleclose",
        TransitionStyle.Radial      => "radial",
        TransitionStyle.SmoothLeft  => "smoothleft",
        TransitionStyle.SmoothRight => "smoothright",
        TransitionStyle.SmoothUp    => "smoothup",
        TransitionStyle.SmoothDown  => "smoothdown",
        TransitionStyle.Pixelize    => "pixelize",
        TransitionStyle.FadeBlack   => "fadeblack",
        TransitionStyle.FadeWhite   => "fadewhite",
        _                           => "fade"
    };

    // ── Quality / codec args ─────────────────────────────────────────────────

    internal static IEnumerable<string> QualityArgs(ExportSettings s)
    {
        yield return "-c:v";
        yield return s.VideoCodec;

        if (s.UseCrf)
        {
            yield return "-crf";
            yield return s.Crf.ToString();
        }
        else
        {
            yield return "-b:v";
            yield return $"{s.Bitrate}k";
        }

        if (!string.IsNullOrEmpty(s.Preset) &&
            s.VideoCodec is "libx264" or "libx265")
        {
            yield return "-preset";
            yield return s.Preset;
        }
    }

    // ── Audio args ───────────────────────────────────────────────────────────

    /// <summary>Audio encode args used when the video is being re-encoded with audio output.</summary>
    internal static IEnumerable<string> AudioOutputArgs(ExportSettings s)
    {
        if (!s.IncludeAudio) { yield return "-an"; yield break; }
        yield return "-c:a";
        yield return s.AudioCodec;
        yield return "-b:a";
        yield return $"{s.AudioBitrate}k";
    }

    /// <summary>
    /// Audio copy args used when only the video stream is being processed (drawtext etc.).
    /// WARNING: any explicit <c>-map</c> disables ffmpeg's default stream selection, so the
    /// caller MUST also map a video stream (<c>-map "[out]"</c> from a filter_complex, or
    /// <c>-map 0:v</c>) — pairing this with a bare <c>-vf</c> produces an audio-only file that
    /// still exits 0 (the actual mechanism behind backlog #29's silent video-less export).
    /// Use <see cref="BuildFilteredVideoArgs"/> for plain filter-chain passes.
    /// </summary>
    internal static IEnumerable<string> AudioPassthroughArgs(ExportSettings s)
    {
        if (!s.IncludeAudio) { yield return "-an"; yield break; }
        yield return "-map";
        yield return "0:a?";
        yield return "-c:a";
        yield return "copy";
    }

    /// <summary>
    /// Full args for a pass that applies a video filter chain to <paramref name="inputName"/>
    /// while passing audio through. The chain is wrapped in a filter_complex with the filtered
    /// video mapped explicitly — never emitted as a bare <c>-vf</c> next to the audio
    /// <c>-map</c>, which would silently drop the video stream (see AudioPassthroughArgs).
    /// </summary>
    internal static string[] BuildFilteredVideoArgs(
        string inputName, string vfChain, ExportSettings s, string outputName) =>
    [
        "-i", inputName,
        "-filter_complex", $"[0:v]{vfChain}[out]",
        "-map", "[out]",
        ..AudioPassthroughArgs(s),
        ..QualityArgs(s),
        outputName,
    ];

    // ── Progress ─────────────────────────────────────────────────────────────

    internal static int ProgressInRange(int index, int total, int rangeStart, int rangeEnd)
    {
        var fraction = total > 1 ? (double)index / total : 0;
        return rangeStart + (int)(fraction * (rangeEnd - rangeStart));
    }

    // ── Output metadata ──────────────────────────────────────────────────────

    internal static string MimeType(string format) => format switch
    {
        "webm" => "video/webm",
        "mov"  => "video/quicktime",
        _      => "video/mp4"
    };

    internal static string SanitiseFilename(string name)
    {
        // Use a fixed cross-platform set instead of Path.GetInvalidFileNameChars(),
        // which only returns '/' and '\0' on macOS but misses Windows-invalid chars.
        var invalid = new HashSet<char>(['<', '>', ':', '"', '/', '\\', '|', '?', '*', '\0']);
        var safe = string.Concat(name.Where(c => !invalid.Contains(c) && !char.IsControl(c)));
        return string.IsNullOrWhiteSpace(safe) ? "output" : safe;
    }

    // ── Chapter metadata ───────────────────────────────────────────────────────

    /// <summary>
    /// Produces an ffmetadata v1 string that encodes the supplied markers as
    /// MP4/MOV chapters.  Time values use TIMEBASE 1/1000 (milliseconds).
    /// The last chapter ends at <paramref name="totalDurationSeconds"/>.
    /// Returns an empty string when <paramref name="markers"/> is empty.
    /// </summary>
    internal static string BuildChapterMetadata(
        IReadOnlyList<TimelineMarker> markers,
        double totalDurationSeconds)
    {
        if (markers.Count == 0) return string.Empty;

        var ic     = System.Globalization.CultureInfo.InvariantCulture;
        var sb     = new System.Text.StringBuilder();
        AppendLf(sb, ";FFMETADATA1");

        var sorted = markers.OrderBy(m => m.TimeSeconds).ToList();

        for (var i = 0; i < sorted.Count; i++)
        {
            var startMs = (long)Math.Round(sorted[i].TimeSeconds * 1000);
            var endMs   = i < sorted.Count - 1
                ? (long)Math.Round(sorted[i + 1].TimeSeconds * 1000)
                : (long)Math.Round(totalDurationSeconds * 1000);

            // clamp: end must be > start
            if (endMs <= startMs) endMs = startMs + 1;

            AppendLf(sb, "[CHAPTER]");
            AppendLf(sb, "TIMEBASE=1/1000");
            AppendLf(sb, $"START={startMs.ToString(ic)}");
            AppendLf(sb, $"END={endMs.ToString(ic)}");
            // Escape '=' and '#' per ffmetadata spec
            var title = EscapeMetadataValue(sorted[i].Label);
            AppendLf(sb, $"title={title}");
        }

        return sb.ToString();
    }

    /// <summary>Appends <paramref name="text"/> followed by a single LF.</summary>
    /// <remarks>
    /// <para>Deliberately not <c>AppendLine</c>, which writes <see cref="Environment.NewLine"/>.
    /// This assembly is the ffmpeg wire contract shared with the sidecar, so the same project has
    /// to produce the same bytes whether it is exported from Blazor Server on Windows (CRLF),
    /// Blazor WebAssembly in the browser (LF), or a sidecar on Windows, macOS or Linux.</para>
    ///
    /// <para>LF rather than CRLF specifically, unlike the subtitle formats: this is what ffmpeg's
    /// own <c>-f ffmetadata</c> muxer emits, so it is the form its demuxer is certain to read
    /// back. A section header that arrived as <c>[CHAPTER]\r</c> and did not match would not be an
    /// error — the chapters would simply not be there, which is a great deal harder to notice.
    /// </para>
    /// </remarks>
    private static void AppendLf(System.Text.StringBuilder sb, string text) => sb.Append(text).Append('\n');

    /// <summary>
    /// ffmpeg args to mux <paramref name="inputFile"/> with the
    /// <paramref name="metadataFile"/> into <paramref name="outputFile"/> using
    /// stream-copy (no re-encode).
    /// </summary>
    internal static string[] BuildChapterEmbedArgs(
        string inputFile, string metadataFile, string outputFile) =>
    [
        "-i",       inputFile,
        "-i",       metadataFile,
        "-map_metadata", "1",
        "-map_chapters", "1",
        "-c",       "copy",
        outputFile
    ];

    /// <summary>
    /// Escapes a value for the ffmetadata text format per ffmpeg's own spec: '=', ';', '#', '\'
    /// and newlines are all special and must be backslash-escaped. The original version of this
    /// method (before item #38 phase 121) omitted the newline case — a chapter title containing
    /// "\n[CHAPTER]\nSTART=..." could inject a whole extra directive block into the metadata
    /// stream. Low real-world impact (chapter titles are metadata, not a code-execution path) but
    /// a genuine spec violation and a real injection, fixed here with a regression test.
    /// </summary>
    private static string EscapeMetadataValue(string value) =>
        value.Replace("\\", "\\\\")   // must be first — escape existing backslashes
             .Replace("=",  "\\=")
             .Replace("#",  "\\#")
             .Replace(";",  "\\;")
             .Replace("\r", "\\\r")
             .Replace("\n", "\\\n");

    // ── Layer compositing ──────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Builds ffmpeg arguments that composite <paramref name="layerFiles"/> into a single output
    /// using a chained <c>overlay</c> filter graph.
    /// </summary>
    /// <remarks>
    /// Layer order: index 0 is the <em>bottom</em> layer (background); the last index is the
    /// top-most layer. This matches the <see cref="TimelineTrack.Order"/> convention where
    /// lower <c>Order</c> = higher in the UI = higher compositing priority, so callers must
    /// pass the list sorted from highest Order (bottom) to lowest Order (top).
    /// </remarks>
    /// <param name="layerFiles">
    /// MEMFS filenames of the pre-rendered video tracks, ordered bottom → top.
    /// Must contain at least 2 entries.
    /// </param>
    /// <param name="outputFile">MEMFS filename for the composited output.</param>
    /// <param name="alphaCompositing">
    /// When <c>true</c>, adds <c>format=yuva420p</c> before each overlay to preserve alpha
    /// transparency. When <c>false</c>, uses a bare <c>overlay</c> (standard blend).
    /// </param>
    /// <param name="settings">Export settings — used for codec and quality flags on the output.</param>
    /// <returns>ffmpeg argument array ready to pass to <c>ffmpeg.exec()</c>.</returns>
    /// <exception cref="ArgumentException">Fewer than 2 layer files provided.</exception>
    internal static string[] BuildOverlayFilterComplex(
        IReadOnlyList<string> layerFiles,
        string                outputFile,
        bool                  alphaCompositing,
        ExportSettings        settings)
    {
        if (layerFiles.Count < 2)
            throw new ArgumentException("At least 2 layer files are required for compositing.", nameof(layerFiles));

        var args = new List<string>();

        // Inputs
        foreach (var f in layerFiles)
            args.AddRange(["-i", f]);

        // Build filter_complex:
        // [0:v][1:v]overlay[v01];[v01][2:v]overlay[v012];...
        // With alpha: [0:v]format=yuva420p[b0];[1:v]format=yuva420p[b1];[b0][b1]overlay[v01];...
        var sb = new System.Text.StringBuilder();

        if (alphaCompositing)
        {
            // Declare format=yuva420p for every input
            for (var i = 0; i < layerFiles.Count; i++)
                sb.Append($"[{i}:v]format=yuva420p[b{i}];");

            // Chain overlays
            for (var i = 1; i < layerFiles.Count; i++)
            {
                var bottom = i == 1 ? $"[b0]" : $"[v{i - 1}]";
                var top    = $"[b{i}]";
                var output = i == layerFiles.Count - 1 ? "[vout]" : $"[v{i}]";
                sb.Append($"{bottom}{top}overlay{output}");
                if (i < layerFiles.Count - 1) sb.Append(';');
            }
        }
        else
        {
            for (var i = 1; i < layerFiles.Count; i++)
            {
                var bottom = i == 1 ? "[0:v]" : $"[v{i - 1}]";
                var top    = $"[{i}:v]";
                var output = i == layerFiles.Count - 1 ? "[vout]" : $"[v{i}]";
                sb.Append($"{bottom}{top}overlay{output}");
                if (i < layerFiles.Count - 1) sb.Append(';');
            }
        }

        args.AddRange(["-filter_complex", sb.ToString(), "-map", "[vout]"]);
        args.AddRange(AudioOutputArgs(settings));
        args.AddRange(QualityArgs(settings));
        args.AddRange(["-pix_fmt", settings.PixelFormat, outputFile]);

        return [.. args];
    }

    /// <summary>
    /// Composites one clip from a track above the primary onto the picture built so far, at the
    /// place on the timeline where it actually sits.
    /// </summary>
    /// <param name="baseFile">The picture assembled so far.</param>
    /// <param name="layerFile">The rendered segment for the clip on the upper track.</param>
    /// <param name="start">Where the clip begins on the timeline, in seconds.</param>
    /// <param name="duration">How long the clip lasts, in seconds.</param>
    /// <param name="layerHasAudio">
    /// Whether the layer segment carries sound of its own to fold into the mix.
    /// </param>
    /// <remarks>
    /// <para>Secondary video tracks reached the output only as the far side of a cross-track
    /// transition, so a clip placed on track 2 was simply absent from the render while the timeline
    /// showed it plainly — multi-track was a feature of the editor and not of the product
    /// (2026-09-05 audit, export-1).</para>
    ///
    /// <para>The composite that existed for this fed every layer in whole and unpositioned, which
    /// would have put a clip from ten seconds in at the very start and then, because overlay repeats
    /// its last frame by default, left it frozen over everything that followed. Each clip is
    /// therefore shifted to its own timeline position and shown only across its own span; outside
    /// it the picture underneath is what plays.</para>
    ///
    /// <para>Until clips carry a position and a size of their own (the plan's later phase), a clip
    /// on an upper track covers the frame for as long as it runs, which is what the preview shows
    /// too.</para>
    /// </remarks>
    internal static string[] BuildLayerCompositeArgs(
        string baseFile, string layerFile, string output,
        double start, double duration, ExportSettings s, bool layerHasAudio)
    {
        var ic    = System.Globalization.CultureInfo.InvariantCulture;
        var from  = start.ToString("F3", ic);
        var to    = (start + duration).ToString("F3", ic);
        var delay = (int)Math.Round(Math.Max(0, start) * 1000.0);

        var sb = new System.Text.StringBuilder();

        // setpts moves the layer to its own start; enable keeps it off screen everywhere else; and
        // eof_action=pass hands the picture back to the layer underneath once the clip ends,
        // rather than repeating its final frame to the end of the export.
        sb.Append($"[1:v]setpts=PTS-STARTPTS+{from}/TB[ov];");
        sb.Append($"[0:v][ov]overlay=enable='between(t,{from},{to})':eof_action=pass[vout]");

        var withAudio = s.IncludeAudio && layerHasAudio;
        if (withAudio)
        {
            sb.Append(';');
            if (delay > 0) sb.Append($"[1:a]adelay={delay}:all=1[oa];");
            else           sb.Append("[1:a]anull[oa];");

            // duration=first: the layer is a piece of a longer timeline, so the mix ends when the
            // picture underneath does.
            sb.Append("[0:a][oa]amix=inputs=2:duration=first:normalize=0:dropout_transition=0[amixed];");
            sb.Append("[amixed]alimiter=limit=0.95[aout]");
        }

        var args = new List<string> { "-i", baseFile, "-i", layerFile };
        args.AddRange(["-filter_complex", sb.ToString(), "-map", "[vout]"]);

        // Without an explicit map the picture is the only thing selected, which is how a
        // transition pass used to strip the sound out of a whole export.
        if (withAudio)           args.AddRange(["-map", "[aout]"]);
        else if (s.IncludeAudio) args.AddRange(["-map", "0:a?"]);

        args.AddRange(AudioOutputArgs(s));
        args.AddRange(QualityArgs(s));
        args.AddRange(["-pix_fmt", s.PixelFormat, output]);
        return [.. args];
    }

    /// <summary>
    /// Returns a copy of <paramref name="clip"/> with its position, size, and opacity overridden by an
    /// interpolated <see cref="MotionFrame"/> — used to render one animated SVG frame per output frame
    /// for a callout with a motion path. <see cref="MotionFrame.X"/>/<see cref="MotionFrame.Y"/> replace
    /// the clip's top-left corner directly (matching conventions); <see cref="MotionFrame.ScaleX"/>/
    /// <see cref="MotionFrame.ScaleY"/> (item #57 P3 — independently multiply
    /// <see cref="CalloutClip.Width"/>/<see cref="CalloutClip.Height"/>; always resolved, defaulting to
    /// the legacy uniform <see cref="MotionFrame.Scale"/> when a keyframe never set per-axis values, so
    /// this is backward compatible with every pre-P3 saved project) — the shape scales from its top-left
    /// corner, i.e. the same point the position keyframe already anchors; <see cref="MotionFrame.Alpha"/>
    /// multiplies the clip's own <see cref="CalloutClip.Opacity"/> rather than overriding it, so the
    /// user's opacity slider still acts as a ceiling.
    /// </summary>
    internal static CalloutClip ApplyMotionFrame(CalloutClip clip, MotionFrame frame)
    {
        var mergedControlPoints = new Dictionary<string, double>(clip.ControlPointValues);
        foreach (var (key, value) in frame.ControlPointValues)
            mergedControlPoints[key] = value;

        return clip with
        {
            X                  = frame.X,
            Y                  = frame.Y,
            Width              = clip.Width  * frame.ScaleX,
            Height             = clip.Height * frame.ScaleY,
            Opacity            = clip.Opacity * frame.Alpha,
            FillColor          = frame.FillColor,
            StrokeColor        = frame.StrokeColor,
            ControlPointValues = mergedControlPoints,
            ShadowColor        = frame.ShadowColor,
            ShadowOffsetX      = frame.ShadowOffsetX,
            ShadowOffsetY      = frame.ShadowOffsetY,
            ShadowBlur         = frame.ShadowBlur,
        };
    }

    /// <summary>
    /// Returns a copy of <paramref name="clip"/> with its position, size, opacity, and rotation
    /// overridden by an interpolated <see cref="MotionFrame"/> — used to render one animated frame per
    /// output frame for a clipart layer with a motion path (see <see cref="RasterClipArtAnimationExporter"/>).
    /// <see cref="MotionFrame.X"/>/<see cref="MotionFrame.Y"/> replace the clip's top-left corner directly;
    /// <see cref="MotionFrame.ScaleX"/>/<see cref="MotionFrame.ScaleY"/> (item #57 P3) independently
    /// multiply <see cref="ClipArtClip.Width"/>/<see cref="ClipArtClip.Height"/> (same top-left anchor
    /// the position keyframe already uses; if <c>Height</c> is <c>-1</c> — preserve-aspect-ratio — it is
    /// left at <c>-1</c>, not scaled, since the renderer already treats that sentinel specially);
    /// <see cref="MotionFrame.Alpha"/> multiplies the clip's own <see cref="ClipArtClip.Opacity"/> rather
    /// than overriding it, matching the CalloutClip/TextOverlay overloads' convention.
    /// <see cref="MotionFrame.Rotation"/> (item #57 P3, ClipArt-only per the arc's locked scope decision)
    /// overrides <see cref="ClipArtClip.Rotation"/> when a keyframe sets it, else the clip's own static
    /// value passes through unchanged (matching pre-P3 behavior). <see cref="ClipArtClip.TintColor"/> is
    /// not part of <see cref="MotionFrame"/> and is left untouched.
    /// </summary>
    internal static ClipArtClip ApplyMotionFrame(ClipArtClip clip, MotionFrame frame) => clip with
    {
        X        = frame.X,
        Y        = frame.Y,
        Width    = clip.Width * frame.ScaleX,
        Height   = clip.Height > 0 ? clip.Height * frame.ScaleY : clip.Height,
        Opacity  = clip.Opacity * frame.Alpha,
        Rotation = frame.Rotation ?? clip.Rotation,
    };

    /// <summary>
    /// Returns a copy of <paramref name="overlay"/> with its position, size, opacity, and shadow overridden
    /// by an interpolated <see cref="MotionFrame"/> — used to render one animated SVG frame per output frame
    /// for a text overlay with a motion path (see <see cref="TextOverlayRenderer.Render"/>).
    /// <see cref="MotionFrame.X"/>/<see cref="MotionFrame.Y"/> unconditionally set
    /// <see cref="TextOverlay.OverrideX"/>/<see cref="TextOverlay.OverrideY"/> — once an overlay has any
    /// motion path, its position is entirely keyframe-driven, matching how animated callouts already work.
    /// <see cref="MotionFrame.Scale"/> multiplies <see cref="TextOverlay.FontSize"/> (text has no
    /// width/height to scale); <see cref="MotionFrame.Alpha"/> multiplies the overlay's own
    /// <see cref="TextOverlay.Opacity"/> rather than overriding it, matching the <see cref="CalloutClip"/>
    /// overload's convention.
    /// </summary>
    internal static TextOverlay ApplyMotionFrame(TextOverlay overlay, MotionFrame frame) => overlay with
    {
        OverrideX     = frame.X,
        OverrideY     = frame.Y,
        FontSize      = (int)Math.Round(overlay.FontSize * frame.Scale),
        Opacity       = overlay.Opacity * frame.Alpha,
        ShadowColor   = frame.ShadowColor,
        ShadowOffsetX = frame.ShadowOffsetX,
        ShadowOffsetY = frame.ShadowOffsetY,
        ShadowBlur    = frame.ShadowBlur,
    };

    /// <summary>
    /// Seconds elapsed at output frame <paramref name="frameIndex"/> for an animated per-frame
    /// SVG export loop. Extracted specifically to prevent the integer-division bug found live:
    /// <c>frameIndex / fps</c> with both operands <c>int</c> truncates to <c>0</c> for every
    /// frame in a typical 1-second-or-shorter clip, silently freezing the whole animation at its
    /// first keyframe's values with no error or warning.
    /// </summary>
    internal static double ElapsedSeconds(int frameIndex, int fps) => frameIndex / (double)fps;

    // ── ClipArt static overlay (rotation + tint, backlog #56) ────────────────

    /// <summary>
    /// Builds the <c>colorchannelmixer</c> argument string that recolors a raster overlay toward
    /// <paramref name="packedTint"/>'s RGB, blended by its own alpha as the tint strength (0 = no
    /// tint / original colors, 1 = full recolor derived purely from the source's alpha shape).
    /// Returns <c>null</c> when there is nothing to apply (no tint set, or its alpha is 0), so
    /// callers can omit the filter step entirely rather than adding a no-op identity matrix.
    /// Per channel: <c>output = original * (1 - t) + (tintChannel/255) * alpha * t</c> — a linear
    /// blend expressible directly as <c>colorchannelmixer</c> coefficients (it has no constant
    /// term, only per-input-channel multipliers, so the tint contribution has to come from the
    /// image's own alpha channel rather than a flat add). The output alpha channel is left
    /// unchanged (<c>aa=1</c>, the filter's default, so it is omitted).
    /// </summary>
    internal static string? BuildClipArtTintMixer(double? packedTint)
    {
        if (packedTint is not { } packed) return null;
        var (r, g, b, a) = ColorHelper.Unpack(packed);
        if (a == 0) return null;

        var ic   = System.Globalization.CultureInfo.InvariantCulture;
        var t    = a / 255.0;
        var keep = (1 - t).ToString("F4", ic);
        var tr   = (t * r / 255.0).ToString("F4", ic);
        var tg   = (t * g / 255.0).ToString("F4", ic);
        var tb   = (t * b / 255.0).ToString("F4", ic);

        return $"rr={keep}:ra={tr}:gg={keep}:ga={tg}:bb={keep}:ba={tb}";
    }

    /// <summary>
    /// The pixel bounding box that fully contains a <paramref name="width"/>×<paramref name="height"/>
    /// rectangle rotated by <paramref name="rotationDegrees"/> around its own center — matches
    /// ffmpeg's own <c>rotw(a)</c>/<c>roth(a)</c> expression functions exactly (same trig), so the
    /// overlay position computed from this in C# lines up with what the <c>rotate</c> filter
    /// actually outputs.
    /// </summary>
    internal static (int Width, int Height) ComputeRotatedBounds(int width, int height, double rotationDegrees)
    {
        var rad = rotationDegrees * Math.PI / 180.0;
        var cos = Math.Abs(Math.Cos(rad));
        var sin = Math.Abs(Math.Sin(rad));
        // Math.Round (not Ceiling) avoids padding out an extra pixel from floating-point noise
        // around exact right angles, e.g. cos(90°) evaluating to ~6e-17 instead of exactly 0.
        return (
            (int)Math.Round(width * cos + height * sin, MidpointRounding.AwayFromZero),
            (int)Math.Round(width * sin + height * cos, MidpointRounding.AwayFromZero));
    }

    /// <summary>
    /// Builds the <c>-filter_complex</c> string for a static (no motion path) <see cref="ClipArtClip"/>
    /// overlay: scale to its on-canvas pixel size, optionally recolor via <see cref="BuildClipArtTintMixer"/>
    /// and/or fade via <c>colorchannelmixer=aa=</c> for <see cref="ClipArtClip.Opacity"/> (both share one
    /// colorchannelmixer call when both apply), optionally rotate around its own center (bounding box
    /// expanded so corners aren't clipped, overlay position recomputed to keep the same center — see
    /// <see cref="ComputeRotatedBounds"/>), then overlay onto the base video for the clip's active time
    /// window. Rotation/tint/opacity apply identically to raster and to a plain SVG-without-control-points
    /// asset (both arrive here as an already-decoded image on <c>[1:v]</c>); an SVG with control points
    /// never reaches this method — see <c>ExportService.ApplyClipArtClipsAsync</c>.
    /// </summary>
    internal static string BuildClipArtStaticOverlayFilter(ClipArtClip clip, int vw, int vh)
    {
        var ic = System.Globalization.CultureInfo.InvariantCulture;

        var ow = Math.Max(1, (int)(clip.Width * vw));
        var oh = clip.Height > 0 ? Math.Max(1, (int)(clip.Height * vh)) : ow;
        var px = (int)(clip.X * vw);
        var py = (int)(clip.Y * vh);

        var chain = new List<string> { $"scale={ow}:{oh}", "format=rgba" };

        var mixerParams = new List<string>();
        var tintMixer = BuildClipArtTintMixer(clip.TintColor);
        if (tintMixer is not null)
            mixerParams.Add(tintMixer);
        if (clip.Opacity < 1.0)
            mixerParams.Add($"aa={clip.Opacity.ToString("F4", ic)}");
        if (mixerParams.Count > 0)
            chain.Add($"colorchannelmixer={string.Join(":", mixerParams)}");

        if (Math.Abs(clip.Rotation) > 0.001)
        {
            var rad = (clip.Rotation * Math.PI / 180.0).ToString("F6", ic);
            chain.Add($"rotate={rad}:ow=rotw({rad}):oh=roth({rad}):c=black@0.0");

            var (rotW, rotH) = ComputeRotatedBounds(ow, oh, clip.Rotation);
            px = px + ow / 2 - rotW / 2;
            py = py + oh / 2 - rotH / 2;
        }

        var startT = clip.TimelinePosition;
        var endT   = clip.TimelinePosition + clip.Duration;

        return $"[1:v]{string.Join(",", chain)}[ov];" +
               $"[0:v][ov]overlay={px}:{py}:enable='between(t,{startT.ToString("F3", ic)},{endT.ToString("F3", ic)})'[out]";
    }

    // ── Static overlay composite (single looped PNG) ─────────────────────────

    /// <summary>
    /// Builds the <c>-filter_complex</c> string that composites a single looped overlay PNG
    /// (input <c>[1:v]</c>, fed via <c>-loop 1 -t</c>) over the base video (<c>[0:v]</c>),
    /// visible only during <paramref name="startT"/>–<paramref name="endT"/>. Optional
    /// fade-in/out is expressed as ffmpeg <c>fade=…:alpha=1</c> filters on the overlay's alpha
    /// channel — the single-PNG replacement for what was previously N per-frame opacity
    /// re-renders. Rendering one PNG instead of <c>duration×fps</c> identical full-canvas
    /// frames is the core memory fix for backlog #29's ffmpeg.wasm OOM crash.
    /// </summary>
    internal static string BuildStaticOverlayFilter(
        int vw, int vh, double startT, double endT, double fadeInSeconds = 0, double fadeOutSeconds = 0)
    {
        var ic = System.Globalization.CultureInfo.InvariantCulture;

        var chain = new List<string> { $"scale={vw}:{vh}", "format=rgba" };

        // The overlay PNG stream starts at output t=0 (no PTS offset), so fade timestamps in
        // its own timebase equal output-time values directly.
        if (fadeInSeconds > 0)
            chain.Add($"fade=t=in:st={startT.ToString("F3", ic)}:d={fadeInSeconds.ToString("F3", ic)}:alpha=1");
        if (fadeOutSeconds > 0)
            chain.Add($"fade=t=out:st={(endT - fadeOutSeconds).ToString("F3", ic)}:d={fadeOutSeconds.ToString("F3", ic)}:alpha=1");

        return $"[1:v]{string.Join(",", chain)}[ov];" +
               $"[0:v][ov]overlay=0:0:enable='between(t,{startT.ToString("F3", ic)},{endT.ToString("F3", ic)})'[out]";
    }
}
