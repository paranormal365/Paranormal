using Ben.Video.Editor.Models;
using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;

namespace Ben.Video.Editor.Services;

/// <summary>
/// Scoped service that measures the preview screen element's actual rendered box (via JS
/// <c>getBoundingClientRect</c>) and converts pointer coordinates into canvas fractions, correctly
/// accounting for <c>object-fit: contain</c> letterboxing — something no on-canvas overlay did before
/// this phase (each assumed the container box equalled the video's actual displayed content box).
/// Callers (drag-handle overlays) call <see cref="RefreshAsync"/> once at the start of a drag gesture,
/// then <see cref="ClientToFraction"/>/<see cref="DeltaToFraction"/> for the duration of that drag —
/// re-measuring on every pointermove isn't needed since the container doesn't resize mid-drag.
/// </summary>
public sealed class PreviewGeometryService : IAsyncDisposable
{
    private const string ModulePath = "/_content/Ben.Video.Editor/js/previewGeometryInterop.js";

    private readonly IJSRuntime _js;
    private IJSObjectReference? _module;

    private double _screenLeft, _screenTop;
    private double _contentOffsetX, _contentOffsetY, _contentWidth, _contentHeight;
    private bool   _measured;

    public PreviewGeometryService(IJSRuntime js) => _js = js;

    private sealed record ElementRect(double Left, double Top, double Width, double Height);

    /// <summary>Measures <paramref name="screenElement"/>'s current rendered box and computes the
    /// letterboxed content box within it for a canvas of size (<paramref name="canvasWidth"/>,
    /// <paramref name="canvasHeight"/>). Call before a drag gesture starts.</summary>
    public async Task RefreshAsync(ElementReference screenElement, int canvasWidth, int canvasHeight)
    {
        _module ??= await _js.InvokeAsync<IJSObjectReference>("import", ModulePath);
        var rect = await _module.InvokeAsync<ElementRect>("getElementRect", screenElement);

        _screenLeft = rect.Left;
        _screenTop  = rect.Top;
        (_contentOffsetX, _contentOffsetY, _contentWidth, _contentHeight) =
            PreviewGeometry.ComputeContentBox(rect.Width, rect.Height, canvasWidth, canvasHeight);
        _measured = true;
    }

    /// <summary>Converts viewport-absolute client coordinates (e.g. <c>PointerEventArgs.ClientX/Y</c>)
    /// into a clamped 0..1 canvas fraction. Returns (0.5, 0.5) if <see cref="RefreshAsync"/> hasn't been
    /// called yet.</summary>
    public (double FractionX, double FractionY) ClientToFraction(double clientX, double clientY)
    {
        if (!_measured) return (0.5, 0.5);
        return PreviewGeometry.ToFraction(
            clientX - _screenLeft, clientY - _screenTop,
            _contentOffsetX, _contentOffsetY, _contentWidth, _contentHeight);
    }

    /// <summary>Converts a pixel delta (between two pointer events) into a canvas-fraction delta —
    /// the correct replacement for dividing by a fixed/native canvas dimension, since the on-screen
    /// rendered size is very rarely equal to it. Returns (0, 0) if never measured.</summary>
    public (double FractionDeltaX, double FractionDeltaY) DeltaToFraction(double deltaX, double deltaY)
    {
        if (!_measured) return (0, 0);
        return PreviewGeometry.DeltaToFraction(deltaX, deltaY, _contentWidth, _contentHeight);
    }

    public async ValueTask DisposeAsync()
    {
        if (_module is not null)
        {
            try { await _module.DisposeAsync(); } catch (JSDisconnectedException) { }
        }
    }
}
