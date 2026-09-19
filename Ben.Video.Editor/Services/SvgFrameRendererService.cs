using Ben.Video.Editor.Models;
using Ben.Video.Editor.Models.Assets;
using Microsoft.JSInterop;

namespace Ben.Video.Editor.Services;

/// <summary>
/// Rasterises SVG assets to PNG frames by driving the browser's
/// <c>OffscreenCanvas</c> + <c>DOMParser</c> via the
/// <c>svgFrameRenderer.js</c> JavaScript module.
///
/// <para>For each exported <see cref="ClipArtClip"/> with SVG format the
/// <see cref="ExportService"/> calls <see cref="RenderBatchAsync"/> to
/// produce one PNG per frame, which is then written to ffmpeg MEMFS and
/// composited via an <c>overlay</c> filter.</para>
///
/// <para>Registered as Scoped (same lifetime as <see cref="ExportService"/>).</para>
/// </summary>
public sealed class SvgFrameRendererService : IAsyncDisposable
{
    private readonly IJSRuntime _js;
    private IJSObjectReference? _module;

    private const string ModulePath = "js/svgFrameRenderer.js";

    public SvgFrameRendererService(IJSRuntime js)
    {
        _js = js;
    }

    // ── Public API ────────────────────────────────────────────────────────────

    /// <summary>
    /// Render a single SVG frame with the given control-point patches applied
    /// and return the PNG bytes.
    /// </summary>
    /// <param name="svgSource">UTF-8 SVG markup string read from OPFS.</param>
    /// <param name="patches">Zero or more attribute/style mutations to apply before rasterising.</param>
    /// <param name="width">Output PNG width in pixels.</param>
    /// <param name="height">Output PNG height in pixels.</param>
    public async Task<byte[]> RenderFrameAsync(
        string svgSource,
        IReadOnlyList<SvgControlPointPatch> patches,
        int width, int height)
    {
        var module = await GetModuleAsync();
        // JS receives patches as plain objects via JSON serialisation
        var patchDtos = patches.Select(MapPatch).ToArray();
        return await module.InvokeAsync<byte[]>("renderFrame", svgSource, patchDtos, width, height);
    }

    /// <summary>
    /// Render a sequence of SVG frames in a single JS call.
    /// More efficient than <see cref="RenderFrameAsync"/> called N times
    /// because it avoids N .NET↔JS round-trips.
    /// </summary>
    /// <param name="svgSource">UTF-8 SVG markup string.</param>
    /// <param name="framesPatches">One patch-list per frame, in order.</param>
    /// <param name="width">Output PNG width.</param>
    /// <param name="height">Output PNG height.</param>
    /// <returns>One PNG <c>byte[]</c> per frame in the same order as <paramref name="framesPatches"/>.</returns>
    public async Task<IReadOnlyList<byte[]>> RenderBatchAsync(
        string svgSource,
        IReadOnlyList<IReadOnlyList<SvgControlPointPatch>> framesPatches,
        int width, int height)
    {
        var module     = await GetModuleAsync();
        var framesDtos = framesPatches.Select(f => f.Select(MapPatch).ToArray()).ToArray();
        return await module.InvokeAsync<byte[][]>("renderBatch", svgSource, framesDtos, width, height);
    }

    // ── Module lifecycle ──────────────────────────────────────────────────────

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

    // ── Helpers ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Map a <see cref="SvgControlPointPatch"/> to an anonymous DTO that
    /// serialises cleanly to the shape the JS module expects.
    /// </summary>
    private static object MapPatch(SvgControlPointPatch p) => new
    {
        targetSelector = p.TargetSelector,
        type           = p.Type.ToString(),
        value          = p.Value,
        x              = p.X,
        y              = p.Y,
        color          = p.Color,
    };
}
