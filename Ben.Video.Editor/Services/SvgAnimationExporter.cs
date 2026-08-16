using Ben.Video.Editor.Models;
using Ben.Video.Editor.Models.Assets;

namespace Ben.Video.Editor.Services;

/// <summary>
/// Orchestrates per-frame SVG rendering for an animated <see cref="ClipArtClip"/>.
///
/// <para>For each frame in the clip's timeline window:</para>
/// <list type="number">
///   <item>Evaluates the clip's <see cref="ClipArtClip.ControlPoints"/> against
///   the user's <see cref="ClipArtClip.ControlPointValues"/> / <see cref="ClipArtClip.ControlPointColors"/>
///   — or, if motion keyframes exist for a point, interpolates the keyframed value
///   via <see cref="MotionKeyframeService"/>.</item>
///   <item>Builds a <see cref="SvgControlPointPatch"/> list for each frame.</item>
///   <item>Passes all frames to <see cref="SvgFrameRendererService.RenderBatchAsync"/>.</item>
///   <item>Writes the resulting PNG sequence to ffmpeg MEMFS.</item>
///   <item>Returns the ffmpeg <c>overlay</c> input args and filter fragment.</item>
/// </list>
/// </summary>
public sealed class SvgAnimationExporter
{
    private readonly SvgFrameRendererService _renderer;
    private readonly OPFSService             _opfs;
    private readonly FfmpegService           _ffmpeg;

    public SvgAnimationExporter(
        SvgFrameRendererService renderer,
        OPFSService             opfs,
        FfmpegService           ffmpeg)
    {
        _renderer = renderer;
        _opfs     = opfs;
        _ffmpeg   = ffmpeg;
    }

    // ── Public API ────────────────────────────────────────────────────────────

    /// <summary>
    /// Render a sequence of SVG frames from a pre-built SVG string (used for callout shapes).
    /// Each element in <paramref name="framesPatches"/> is a list of patches for that frame
    /// (empty list = no patches, shape is fully described by the SVG string).
    /// </summary>
    public Task<IReadOnlyList<byte[]>> RenderBatchFromSvgAsync(
        string svgSource,
        IReadOnlyList<IReadOnlyList<SvgControlPointPatch>> framesPatches,
        int width, int height)
        => _renderer.RenderBatchAsync(svgSource, framesPatches, width, height);

    /// <summary>
    /// Render a single, fully self-contained SVG string (used for one frame of an animated callout,
    /// where each frame's position/size/opacity is already baked into a distinct SVG document rather
    /// than expressed as patches against a shared base document).
    /// </summary>
    public Task<byte[]> RenderFrameFromSvgAsync(string svgSource, int width, int height)
        => _renderer.RenderFrameAsync(svgSource, [], width, height);

    /// <summary>
    /// Render an SVG <see cref="ClipArtClip"/> to a PNG frame sequence in MEMFS
    /// and return the ffmpeg input file list and overlay filter fragment needed
    /// to composite it over the current <paramref name="baseInput"/> stream.
    /// </summary>
    /// <param name="clip">The SVG clip to render.</param>
    /// <param name="baseInput">Name of the current composited video in MEMFS (e.g. "base.mp4").</param>
    /// <param name="fps">Frames per second matching the export settings.</param>
    /// <param name="videoWidth">Frame width in pixels.</param>
    /// <param name="videoHeight">Frame height in pixels.</param>
    /// <param name="outputName">MEMFS file name for the rendered output.</param>
    /// <param name="tempFiles">List to append MEMFS temp file names for cleanup.</param>
    /// <returns>
    /// A tuple of (ffmpeg arguments array, MEMFS prefix for cleanup) ready
    /// to be appended to the export command.
    /// </returns>
    public async Task<(string[] args, IReadOnlyList<string> writtenFiles)> RenderAsync(
        ClipArtClip clip,
        string      baseInput,
        double      fps,
        int         videoWidth,
        int         videoHeight,
        List<string> tempFiles)
    {
        // Read SVG source from OPFS
        if (!Guid.TryParse(clip.AssetId, out var assetGuid))
            return ([], []);

        var svgSource = await _opfs.ReadAsTextAsync(assetGuid, ".svg");
        if (string.IsNullOrEmpty(svgSource))
            return ([], []);

        // Calculate frame count and render dimensions
        var frameCount = Math.Max(1, (int)Math.Round(clip.Duration * fps));
        var dt         = clip.Duration / frameCount;

        // Render width/height: use the clip's canvas-fraction × video dimensions
        var renderW = Math.Max(1, (int)(clip.Width * videoWidth));
        // Height: if -1, preserve aspect ratio — use same as width (square; SVG viewBox handles it)
        var renderH = clip.Height > 0
            ? Math.Max(1, (int)(clip.Height * videoHeight))
            : renderW;  // square default; SVG viewBox preserves ratio internally

        // Item #59-#65 flakiness investigation, phase 146 (MEMFS pressure) — batches the JS
        // render calls instead of rendering every frame in one call (bounds the JS/.NET marshal
        // peak, the same risk RasterClipArtAnimationExporter had). Unlike that path, this one
        // does NOT also bound MEMFS residency — every batch's PNGs still accumulate in MEMFS
        // until the single caller-side exec (ApplyClipArtClipsAsync) consumes the whole sequence,
        // since restructuring THIS path's caller into the trim/splice/concat shape
        // ApplyAnimatedClipArtAsync now uses is real, separate scope. Still a genuine
        // improvement for the common case (SVG clipart render dimensions are usually a small
        // canvas-fraction, not the full frame, unlike the raster path's full-canvas PNGs) and
        // was cheap to do alongside the real fix.
        var prefix    = $"svgclip_{clip.Id:N}";
        var written   = new List<string>(frameCount);
        var batchSize = AnimatedOverlayBatchPlanner.BatchSize(renderW, renderH);
        foreach (var (_, batchStart, batchCount) in AnimatedOverlayBatchPlanner.Batches(frameCount, batchSize))
        {
            var batchPatches = new List<IReadOnlyList<SvgControlPointPatch>>(batchCount);
            for (var i = 0; i < batchCount; i++)
                batchPatches.Add(BuildPatches(clip, (batchStart + i) * dt));

            var batchPngs = await _renderer.RenderBatchAsync(svgSource, batchPatches, renderW, renderH);
            for (var i = 0; i < batchPngs.Count; i++)
            {
                var fname = $"{prefix}_{batchStart + i:D4}.png";
                await _ffmpeg.WriteFileFromBytesAsync(fname, batchPngs[i]);
                written.Add(fname);
                tempFiles.Add(fname);
            }
        }

        // Canvas position in pixels
        var px = (int)(clip.X * videoWidth);
        var py = (int)(clip.Y * videoHeight);

        // ffmpeg args: read the image sequence as a second input, then overlay
        // The caller uses filter_complex; we return the args to insert before -i baseInput
        var seqInput = $"{prefix}_%04d.png";
        var args     = new[]
        {
            "-framerate", fps.ToString("F2"),
            "-i",         seqInput,
        };

        // overlay filter fragment — caller inserts into filter_complex
        // Format: [1:v]scale={w}:{h}[ov];[0:v][ov]overlay={x}:{y}:enable='between(t,{s},{e})'
        var startT = clip.TimelinePosition;
        var endT   = clip.TimelinePosition + clip.Duration;
        var filter = $"[1:v]scale={renderW}:{renderH}[ov];[0:v][ov]overlay={px}:{py}:enable='between(t,{startT:F3},{endT:F3})'[out]";

        return (args.Concat(new[] { "-filter_complex", filter, "-map", "[out]" }).ToArray(), written);
    }

    // ── Patch building ────────────────────────────────────────────────────────

    /// <summary>
    /// Build the list of <see cref="SvgControlPointPatch"/> for a single frame
    /// at time offset <paramref name="t"/> within the clip.
    /// </summary>
    private IReadOnlyList<SvgControlPointPatch> BuildPatches(ClipArtClip clip, double t)
    {
        if (clip.ControlPoints is not { Count: > 0 })
            return [];

        var patches = new List<SvgControlPointPatch>(clip.ControlPoints.Count);
        foreach (var pt in clip.ControlPoints)
        {
            patches.Add(BuildPatch(clip, pt, t));
        }
        return patches;
    }

    private SvgControlPointPatch BuildPatch(ClipArtClip clip, SvgControlPoint pt, double t)
    {
        // Phase 51: static values from the clip's ControlPointValues/Colors.
        // Per-point keyframe animation (MotionKeyframeService per composite id) is Phase 51b.
        if (IsColorType(pt.Type))
        {
            var color = clip.ControlPointColors.TryGetValue(pt.PointId, out var c) ? c : pt.DefaultColor;
            return new SvgControlPointPatch
            {
                PointId        = pt.PointId,
                TargetSelector = pt.TargetSelector,
                Type           = pt.Type,
                Color          = color,
            };
        }

        var value = clip.ControlPointValues.TryGetValue(pt.PointId, out var v) ? v : pt.DefaultValue;
        return new SvgControlPointPatch
        {
            PointId        = pt.PointId,
            TargetSelector = pt.TargetSelector,
            Type           = pt.Type,
            Value          = value,
        };
    }

    private static bool IsColorType(SvgControlPointType type) =>
        type is SvgControlPointType.StrokeColor or SvgControlPointType.FillColor;
}
