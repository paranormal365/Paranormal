using Microsoft.JSInterop;

namespace Ben.Video.Editor.Services;

/// <summary>
/// Scoped service that registers a document-level <c>keydown</c> listener via
/// <c>keyboardInterop.js</c> and forwards key events to a registered
/// <see cref="IKeyboardCommandTarget"/>.
///
/// Call <see cref="RegisterAsync"/> from the host component's
/// <c>OnAfterRenderAsync(firstRender)</c>. The service disposes the JS listener
/// automatically when disposed.
/// </summary>
public sealed class KeyboardShortcutService : IAsyncDisposable
{
    private const string ModulePath = "js/keyboardInterop.js";

    private readonly IJSRuntime _js;
    private IJSObjectReference? _module;
    private DotNetObjectReference<KeyboardShortcutService>? _selfRef;
    private IKeyboardCommandTarget? _target;

    public KeyboardShortcutService(IJSRuntime js)
    {
        _js = js;
    }

    // ── Registration ──────────────────────────────────────────────────────────

    /// <summary>
    /// Lazy-loads the JS module and installs the document <c>keydown</c> listener.
    /// <paramref name="target"/> receives <see cref="IKeyboardCommandTarget.OnEditorKeyDown"/> calls.
    /// </summary>
    public async Task RegisterAsync(IKeyboardCommandTarget target)
    {
        _target = target;
        try
        {
            _module  ??= await _js.InvokeAsync<IJSObjectReference>("benImportEditorModule", ModulePath);
            _selfRef ??= DotNetObjectReference.Create(this);
            await _module.InvokeVoidAsync("register", _selfRef);
        }
        catch (ObjectDisposedException) { /* component disposed before JS module was ready */ }
        catch (JSDisconnectedException)  { /* circuit disconnected during import */ }
    }

    /// <summary>
    /// Test-only overload: registers a target without touching the JS module.
    /// </summary>
    internal Task RegisterAsync(IKeyboardCommandTarget target, bool skipJs)
    {
        _target = target;
        return Task.CompletedTask;
    }

    /// <summary>Remove the JS listener without disposing the service.</summary>
    public async Task UnregisterAsync()
    {
        if (_module is not null)
        {
            try { await _module.InvokeVoidAsync("unregister"); } catch { }
        }

        _target = null;
    }

    // ── JS Callback ───────────────────────────────────────────────────────────

    /// <summary>Called from JS when a keydown event fires on the document.</summary>
    [JSInvokable]
    public Task OnKeyDown(string key, bool ctrl, bool shift, bool alt)
    {
        if (_target is null) return Task.CompletedTask;
        return _target.OnEditorKeyDown(key, ctrl, shift, alt);
    }

    // ── Dispose ───────────────────────────────────────────────────────────────

    public async ValueTask DisposeAsync()
    {
        await UnregisterAsync();
        _selfRef?.Dispose();
        if (_module is not null)
        {
            try { await _module.DisposeAsync(); } catch { }
        }
    }
}

/// <summary>
/// Implemented by the host component (e.g. <c>VideoEditor</c>) to receive
/// keyboard shortcut events forwarded by <see cref="KeyboardShortcutService"/>.
/// </summary>
public interface IKeyboardCommandTarget
{
    /// <summary>
    /// Called for every <c>keydown</c> event that is not suppressed by the
    /// focus guard (i.e. focus is not in an input/textarea).
    /// </summary>
    Task OnEditorKeyDown(string key, bool ctrl, bool shift, bool alt);
}
