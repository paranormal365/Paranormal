using Ben.Video.Editor.Effects;
using Microsoft.JSInterop;

namespace Ben.Video.Editor.Services;

/// <summary>
/// Wraps <c>googleFontsInterop.js</c>'s <c>ensureFontLoaded</c> — loads a Google Fonts web font on
/// demand so it renders correctly in both the live preview and the SVG-rasterization export
/// pipeline (backlog item #16, phase 116). A no-op for system fonts
/// (<see cref="GoogleFonts.IsGoogleFont"/>), so the common case costs nothing — no JS call, no
/// network request.
///
/// <para>Registered as Scoped, same lazy-JS-module pattern as
/// <see cref="RichTextRunParserService"/>/<see cref="RasterClipArtAnimationExporter"/>.</para>
/// </summary>
public sealed class GoogleFontService : IAsyncDisposable
{
    private readonly IJSRuntime _js;
    private IJSObjectReference? _module;

    private const string ModulePath = "/_content/Ben.Video.Editor/js/googleFontsInterop.js";

    public GoogleFontService(IJSRuntime js)
    {
        _js = js;
    }

    /// <summary>
    /// Ensures <paramref name="fontFamily"/> is loaded and ready to render, if it's a Google Font.
    /// Returns immediately (no JS call, no network request) for a system font. Fails silently on a
    /// slow/offline network — the caller's text still renders, just in the browser's fallback font.
    /// </summary>
    public async Task EnsureLoadedAsync(string fontFamily)
    {
        if (!GoogleFonts.IsGoogleFont(fontFamily)) return;

        var module = await GetModuleAsync();
        await module.InvokeVoidAsync("ensureFontLoaded", fontFamily);
    }

    private async ValueTask<IJSObjectReference> GetModuleAsync()
    {
        _module ??= await _js.InvokeAsync<IJSObjectReference>("import", ModulePath);
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
