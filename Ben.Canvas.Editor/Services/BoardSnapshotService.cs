using Ben.Canvas.Core.Persistence;
using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;

namespace Ben.Canvas.Editor.Services;

/// <summary>Draws a board's published picture in the browser (wwwroot/js/snapshotInterop.js).</summary>
public sealed class BoardSnapshotService(IJSRuntime js) : IAsyncDisposable
{
    private IJSObjectReference? _module;

    /// <summary>The picture as PNG bytes, or null when the browser could not draw it.</summary>
    /// <param name="themeRoot">The editor root, whose theme tokens colour the picture.</param>
    public async Task<byte[]?> DrawAsync(ElementReference themeRoot, SnapshotScene scene)
    {
        try
        {
            _module ??= await CanvasModules.ImportAsync(js, "js/snapshotInterop.js");
            var bytes = await _module.InvokeAsync<byte[]?>("drawBoard", themeRoot, scene);
            return bytes is { Length: > 0 } ? bytes : null;
        }
        catch (JSException)
        {
            return null;
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_module is null) return;
        try { await _module.DisposeAsync(); }
        catch (Exception ex) when (ex is JSDisconnectedException or ObjectDisposedException) { }
    }
}
