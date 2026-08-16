using Ben.Video.Editor.Services;

namespace Ben.Video.Tests.Services;

/// <summary>
/// The actual `&lt;link&gt;`-injection/`document.fonts` behavior in googleFontsInterop.js is
/// browser-only and verified live (see README-phase-116.md) — these tests cover the one piece
/// that's genuinely C#-testable: GoogleFontService.EnsureLoadedAsync must skip the JS call
/// entirely for a system font. Uses the same NoOpJSRuntime fake KeyboardShortcutServiceTests
/// already established: InvokeAsync always returns default(TValue) (null for a reference type),
/// so a system font's early return proves itself by NOT throwing, while a Google Font's attempt to
/// actually use the (null) imported module proves itself by throwing.
/// </summary>
public sealed class GoogleFontServiceTests
{
    [Fact]
    public async Task EnsureLoadedAsync_SystemFont_ReturnsWithoutCallingJS()
    {
        var svc = new GoogleFontService(new NoOpJSRuntime());
        // Would throw (NullReferenceException on the null module) if it attempted a JS call —
        // completing cleanly proves the IsGoogleFont(false) guard short-circuited first.
        await svc.EnsureLoadedAsync("Arial");
    }

    [Fact]
    public async Task EnsureLoadedAsync_GoogleFont_AttemptsJSCall()
    {
        var svc = new GoogleFontService(new NoOpJSRuntime());
        // NoOpJSRuntime's "import" call returns a null IJSObjectReference, so actually trying to
        // invoke ensureFontLoaded on it throws (InvokeVoidAsync's own null-argument guard) — proof
        // the Google Font branch was taken, unlike the system-font case above.
        await Assert.ThrowsAsync<ArgumentNullException>(() => svc.EnsureLoadedAsync("Roboto"));
    }

    [Fact]
    public async Task DisposeAsync_DoesNotThrow_WhenNeverRegistered()
    {
        var svc = new GoogleFontService(new NoOpJSRuntime());
        await svc.DisposeAsync();
    }

    private sealed class NoOpJSRuntime : Microsoft.JSInterop.IJSRuntime
    {
        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args)
            => ValueTask.FromResult(default(TValue)!);

        public ValueTask<TValue> InvokeAsync<TValue>(string identifier,
            System.Threading.CancellationToken cancellationToken, object?[]? args)
            => ValueTask.FromResult(default(TValue)!);
    }
}
