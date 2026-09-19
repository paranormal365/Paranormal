using Ben.Video.Editor.Effects;
using Ben.Video.Editor.Models;
using Microsoft.JSInterop;

namespace Ben.Video.Editor.Services;

/// <summary>
/// Renders an animated (motion-keyframed) raster <see cref="ClipArtClip"/> to a sequence of
/// full-canvas-size PNG frames via the <c>rasterClipArtRenderer.js</c> JavaScript module, mirroring
/// <see cref="SvgAnimationExporter"/>'s proven pattern for the SVG+<see cref="ClipArtClip.ControlPoints"/>
/// case. Unlike a static raster overlay (one ffmpeg <c>overlay</c> filter for the whole clip), a clip
/// with a motion path needs its position/size/opacity to vary every frame — expressing that as ffmpeg
/// time-varying expressions would need to replicate <see cref="MotionKeyframeService"/>'s full easing
/// and bezier math inline in ffmpeg's expression language, which is fragile and hard to verify; instead
/// this decodes the source raster image once in JS and re-draws it onto a canvas per frame at the
/// C#-computed (and already-proven-correct) interpolated geometry, so the resulting PNG sequence just
/// needs one trivial static overlay at export time — no per-frame ffmpeg expressions at all.
///
/// <para>Registered as Scoped (same lifetime as <see cref="ExportService"/>).</para>
/// </summary>
public sealed class RasterClipArtAnimationExporter : IAsyncDisposable
{
    private readonly IJSRuntime _js;
    private IJSObjectReference? _module;

    private const string ModulePath = "js/rasterClipArtRenderer.js";

    public RasterClipArtAnimationExporter(IJSRuntime js)
    {
        _js = js;
    }

    /// <summary>
    /// Render <paramref name="frames"/> (one per output frame, each already the effective
    /// pixel-space X/Y/Width/Height/Opacity for that frame) against <paramref name="sourceFile"/>,
    /// returning one full-canvas-size PNG per frame in the same order.
    /// </summary>
    /// <param name="sourceFile">The raster asset's bytes, as returned by <see cref="OPFSService.ReadAsJSFileAsync"/>.</param>
    /// <param name="canvasWidth">Full output frame width in pixels.</param>
    /// <param name="canvasHeight">Full output frame height in pixels.</param>
    /// <param name="frames">Per-frame pixel-space geometry, in output order.</param>
    public async Task<IReadOnlyList<byte[]>> RenderBatchAsync(
        IJSObjectReference sourceFile,
        int canvasWidth, int canvasHeight,
        IReadOnlyList<RasterClipArtFrame> frames)
    {
        var module   = await GetModuleAsync();
        var frameDtos = frames.Select(f =>
        {
            string? tintCss = null;
            var tintAlpha = 0.0;
            if (f.TintColor is { } packed)
            {
                var (r, g, b, a) = ColorHelper.Unpack(packed);
                if (a > 0)
                {
                    tintCss   = $"rgb({r},{g},{b})";
                    tintAlpha = a / 255.0;
                }
            }

            return new
            {
                x         = f.X,
                y         = f.Y,
                w         = f.Width,
                h         = f.Height,
                alpha     = f.Alpha,
                rotation  = f.Rotation,
                tintCss,
                tintAlpha,
            };
        }).ToArray();
        return await module.InvokeAsync<byte[][]>("renderBatch", sourceFile, canvasWidth, canvasHeight, frameDtos);
    }

    private async ValueTask<IJSObjectReference> GetModuleAsync()
    {
        _module ??= await _js.InvokeAsync<IJSObjectReference>("benImportEditorModule", ModulePath);
        return _module;
    }

    public async ValueTask DisposeAsync()
    {
        if (_module is not null)
        {
            try { await _module.DisposeAsync(); } catch { }
            _module = null;
        }
    }
}

/// <summary>
/// Effective pixel-space geometry for one output frame of an animated raster clipart layer.
/// <paramref name="Rotation"/> (degrees) and <paramref name="TintColor"/> (packed ARGB double, see
/// <see cref="ColorHelper"/>) are static per-clip fields, not motion-animated — every frame of a
/// given clip carries the same value — but travel per-frame here since <c>renderBatch</c> has no
/// separate "constant for this whole call" parameter.
/// </summary>
public readonly record struct RasterClipArtFrame(
    double X, double Y, double Width, double Height, double Alpha,
    double Rotation = 0, double? TintColor = null);
