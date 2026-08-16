using Ben.Video.Editor.Models;
using Microsoft.JSInterop;

namespace Ben.Video.Editor.Services;

/// <summary>
/// Wraps <c>richTextRunsInterop.js</c>'s <c>htmlToRuns</c> — converts a <c>TelerikEditor</c>'s
/// rich-text HTML <c>Value</c> into a <see cref="List{TextRun}"/> (item #16). Parsing arbitrary
/// contentEditable HTML needs the browser's own DOM parser to match what the editor actually
/// produced; the reverse direction is plain C# — see <see cref="TextRun.ToHtml"/>.
///
/// <para>Registered as Scoped, following the same lazy-JS-module pattern as
/// <see cref="RasterClipArtAnimationExporter"/>/<see cref="SvgFrameRendererService"/>.</para>
/// </summary>
public sealed class RichTextRunParserService : IAsyncDisposable
{
    private readonly IJSRuntime _js;
    private IJSObjectReference? _module;

    private const string ModulePath = "/_content/Ben.Video.Editor/js/richTextRunsInterop.js";

    public RichTextRunParserService(IJSRuntime js)
    {
        _js = js;
    }

    /// <summary>Parses rich-text HTML into an ordered list of styled runs.</summary>
    public async Task<List<TextRun>> HtmlToRunsAsync(string html)
    {
        var module = await GetModuleAsync();
        var dtos   = await module.InvokeAsync<RunDto[]>("htmlToRuns", html);
        return [.. dtos.Select(d => new TextRun
        {
            Text        = d.Text,
            Bold        = d.Bold,
            Underline   = d.Underline,
            Subscript   = d.Sub,
            Superscript = d.Sup,
            Color       = d.Color,
        })];
    }

    // Matches richTextRunsInterop.js's htmlToRuns return shape exactly (System.Text.Json is
    // case-insensitive by default for JSInterop, so lowercase JS keys map to these PascalCase names).
    private sealed record RunDto(string Text, bool Bold, bool Underline, bool Sub, bool Sup, string? Color);

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
