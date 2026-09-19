using Microsoft.JSInterop;

namespace Ben.Canvas.Editor.Services;

/// <summary>
/// The only way the canvas editor imports its JavaScript.
/// </summary>
/// <remarks>
/// <para>A root-absolute module path breaks under <c>/editors/canvas/</c>, and Blazor's own
/// <c>"import"</c> identifier invites exactly that. Every import goes through
/// <c>window.benImportCanvasModule</c> (wwwroot/js/moduleLoader.js), which resolves against
/// <c>document.baseURI</c>.</para>
///
/// <para>One helper also gives the source-scanning guards one thing to check: no file may import a
/// module any other way.</para>
/// </remarks>
public static class CanvasModules
{
    /// <summary>The global function moduleLoader.js defines.</summary>
    public const string LoaderFunction = "benImportCanvasModule";

    /// <summary>
    /// Imports one of this library's modules.
    /// </summary>
    /// <param name="js">The JS runtime.</param>
    /// <param name="relativePath">A path under the library's wwwroot, e.g. <c>js/modalInterop.js</c>.</param>
    /// <exception cref="ArgumentException">
    /// The path starts with '/' or names <c>_content</c>: both mean the caller is building the URL
    /// the loader exists to build.
    /// </exception>
    public static ValueTask<IJSObjectReference> ImportAsync(IJSRuntime js, string relativePath)
    {
        ArgumentNullException.ThrowIfNull(js);
        ArgumentException.ThrowIfNullOrWhiteSpace(relativePath);

        if (relativePath.StartsWith('/') || relativePath.Contains("_content", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException(
                $"Pass a path relative to the library's wwwroot, such as \"js/modalInterop.js\"; got \"{relativePath}\".",
                nameof(relativePath));

        return js.InvokeAsync<IJSObjectReference>(LoaderFunction, relativePath);
    }
}
