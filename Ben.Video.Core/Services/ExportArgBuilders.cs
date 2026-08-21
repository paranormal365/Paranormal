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
    internal static string[] BuildAmixArgs(
        string videoInput, IReadOnlyList<string> audioSegments, string output, ExportSettings s)
    {
        var args = new List<string> { "-i", videoInput };
        var labels = new List<string>();
        foreach (var segment in audioSegments)
        {
            args.Add("-i");
            args.Add(segment);
            labels.Add($"[{labels.Count + 1}:a]");
        }

        var n = labels.Count;
        args.AddRange([
            "-filter_complex", $"[0:a]{string.Join("", labels)}amix=inputs={n + 1}:duration=longest[aout]",
            "-map", "0:v",
            "-map", "[aout]",
            "-c:v", "copy",
            "-c:a", s.AudioCodec,
            "-b:a", $"{s.AudioBitrate}k",
            output,
        ]);
        return [.. args];
    }

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
            "-t", duration.ToString("F3", System.Globalization.CultureInfo.InvariantCulture),
        };

        var filterParts = new List<string>();
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
        var args = new List<string>
        {
            "-i",   input,
            "-ss",  start.ToString("F3"),
            "-to",  end.ToString("F3"),
        };

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
        if (s.IncludeAudio && !muteAudio && sourceHasAudio)
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
        double speed = 1.0)
    {
        if (effects.Count == 0) return string.Empty;

        var fragments = new List<string>(effects.Count);
        foreach (var applied in effects)
        {
            var def = registry.GetById(applied.EffectId);
            if (def is null) continue;
            var frag = def.BuildFilterFragment(applied.Parameters, clipDuration, speed);
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
    internal static string[] BuildAudioClipTrimArgs(
        string input, string output, double start, double end, string audioFilter, ExportSettings s)
    {
        var ic = System.Globalization.CultureInfo.InvariantCulture;
        return
        [
            "-ss", start.ToString("F3", ic),
            "-to", end.ToString("F3", ic),
            "-i", input,
            "-vn",
            "-filter:a", audioFilter,
            "-c:a", s.AudioCodec,
            "-b:a", $"{s.AudioBitrate}k",
            output,
        ];
    }

    // ── Xfade filter_complex

    /// <summary>
    /// <paramref name="segmentDurations"/> must be parallel to <paramref name="segments"/> — the
    /// real encoded duration of each segment, in the same order. Each junction's offset uses the
    /// standard chained-xfade recurrence (offset_i = offset_{i-1} + segmentDurations[i] -
    /// transitionDuration_i); before this fix the code hardcoded every segment as exactly 5
    /// seconds long (<c>cumOffset += 5.0 - dur</c>), which produced wrong junction offsets — and
    /// therefore visibly/audibly wrong transitions — for any timeline whose clips weren't all
    /// precisely 5 seconds.
    /// </summary>
    internal static string BuildXfadeFilterComplex(
        List<string> segments, List<double> segmentDurations, List<Transition> transitions)
    {
        if (segmentDurations.Count != segments.Count)
            throw new ArgumentException(
                $"segmentDurations count ({segmentDurations.Count}) must match segments count ({segments.Count}).",
                nameof(segmentDurations));

        var sb        = new System.Text.StringBuilder();
        var prev      = "[0:v]";
        var cumOffset = 0.0;

        for (var i = 0; i < segments.Count - 1; i++)
        {
            var t      = i < transitions.Count ? transitions[i] : null;
            var dur    = t?.Duration ?? 1.0;
            var style  = t?.Style ?? TransitionStyle.Fade;
            var outTag = i < segments.Count - 2 ? $"[x{i:D2}]" : "[vout]";

            cumOffset += segmentDurations[i] - dur;

            sb.Append($"{prev}[{i + 1}:v]xfade=transition={XfadeStyle(style)}");
            sb.Append($":duration={dur:F2}:offset={cumOffset:F2}{outTag};");

            prev = outTag;
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

    // ── Callout filter fragment ───────────────────────────────────────────────

    /// <summary>
    /// Builds an ffmpeg video filter fragment for a <see cref="CalloutClip"/>.
    /// The fragment can be inserted into a <c>-vf</c> chain alongside other video filters.
    ///
    /// <para>Coordinates are in canvas fractions (0–1) and converted to pixels using
    /// ffmpeg's <c>W</c> and <c>H</c> variables so the expression is resolution-independent.</para>
    ///
    /// <para><b>Supported shapes:</b> Rectangle and Ellipse via <c>drawbox</c>.
    /// Arrow, Line, Star, and Custom return an empty string because they are routed
    /// through <c>SvgFrameRendererService</c> in <c>ExportService.ApplyCalloutsAsync</c>.</para>
    /// </summary>
    internal static string BuildCalloutFilter(CalloutClip c, ExportSettings s)
    {
        // SVG-rendered shapes are handled separately — return empty so they're excluded from the vf chain
        if (c.Shape is ShapeType.Arrow or ShapeType.Line or ShapeType.Star or ShapeType.Custom)
            return string.Empty;

        var ic   = System.Globalization.CultureInfo.InvariantCulture;
        var fill = ColorHelper.ToFfmpegColor(c.FillColor, includeAlpha: true);
        var st   = c.StrokeWidth > 0
            ? (int)Math.Round(c.StrokeWidth)
            : 0;

        // Canvas-fraction → pixel expression. drawbox's expression language only defines
        // iw/ih (a.k.a. in_w/in_h) for the input frame size — the capital W/H used by the
        // overlay filter are NOT valid here and fail the whole command with exit code 1.
        // (This filter never actually ran before phase 75: the pass that used it dropped
        // its video stream via a bare "-map 0:a?", so ffmpeg never evaluated the chain.)
        var x  = $"(iw*{c.X.ToString("F4", ic)})";
        var y  = $"(ih*{c.Y.ToString("F4", ic)})";
        var w  = $"(iw*{c.Width.ToString("F4", ic)})";
        var h  = $"(ih*{c.Height.ToString("F4", ic)})";

        // Optional shadow. Intentionally a flat, unblurred box — `drawbox` has no blur
        // capability, so `ShadowBlur`'s magnitude is only used as a presence gate here.
        // The SVG-render path (animated callouts, and Arrow/Line/Star always) applies a
        // real `feDropShadow` instead. Do not "fix" this into forcing the SVG path for
        // static Rectangle/Ellipse — that path is materially more expensive (per-clip PNG
        // rasterization + an extra ffmpeg overlay pass) and would regress export
        // performance for the common case for a blur most users won't notice at the
        // default 4px radius.
        var fragments = new List<string>();

        if (c.ShadowBlur > 0 || c.ShadowOffsetX != 0 || c.ShadowOffsetY != 0)
        {
            var shadowCol = ColorHelper.ToFfmpegColor(c.ShadowColor, includeAlpha: true);
            var sx = $"(iw*{c.X.ToString("F4", ic)}+{c.ShadowOffsetX.ToString("F1", ic)})";
            var sy = $"(ih*{c.Y.ToString("F4", ic)}+{c.ShadowOffsetY.ToString("F1", ic)})";
            fragments.Add($"drawbox=x={sx}:y={sy}:w={w}:h={h}:color={shadowCol}:t=fill");
        }

        // Main shape (Rectangle / Ellipse — both render as drawbox; Ellipse will
        // be improved when Blazor previews the SVG renderer in a later phase)
        var thickness = st > 0 ? st.ToString(ic) : "fill";
        fragments.Add($"drawbox=x={x}:y={y}:w={w}:h={h}:color={fill}:t={thickness}");

        return string.Join(",", fragments);
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
