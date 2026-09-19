using Microsoft.JSInterop;

namespace Ben.Canvas.Editor.Services;

/// <summary>
/// Forwards document key presses from keyboardInterop.js to the editor.
/// </summary>
/// <remarks>Copied from Ben.Video.Editor's KeyboardShortcutService, importing through the base-aware loader.</remarks>
public sealed class KeyboardShortcutService(IJSRuntime js) : IAsyncDisposable
{
    public const string ModulePath = "js/keyboardInterop.js";

    private IJSObjectReference? _module;
    private DotNetObjectReference<KeyboardShortcutService>? _self;
    private IKeyboardCommandTarget? _target;

    public async Task RegisterAsync(IKeyboardCommandTarget target)
    {
        _target = target;
        try
        {
            _module ??= await CanvasModules.ImportAsync(js, "js/keyboardInterop.js");
            _self ??= DotNetObjectReference.Create(this);
            await _module.InvokeVoidAsync("register", _self);
        }
        catch (ObjectDisposedException) { }
        catch (JSDisconnectedException) { }
    }

    /// <summary>Test-only: registers a target without the script.</summary>
    internal Task RegisterAsync(IKeyboardCommandTarget target, bool skipJs)
    {
        _target = target;
        return Task.CompletedTask;
    }

    public async Task UnregisterAsync()
    {
        if (_module is not null)
        {
            try { await _module.InvokeVoidAsync("unregister"); }
            catch (JSDisconnectedException) { }
            catch (ObjectDisposedException) { }
            catch (JSException) { }
        }

        _target = null;
    }

    [JSInvokable]
    public Task OnKeyDown(string key, bool ctrl, bool shift, bool alt, bool onBoard) =>
        _target is null ? Task.CompletedTask : _target.OnEditorKeyDown(key, ctrl, shift, alt, onBoard);

    public async ValueTask DisposeAsync()
    {
        await UnregisterAsync();
        _self?.Dispose();
        if (_module is not null)
        {
            try { await _module.DisposeAsync(); }
            catch (JSDisconnectedException) { }
            catch (ObjectDisposedException) { }
        }
    }
}

/// <summary>The component that receives forwarded key presses.</summary>
public interface IKeyboardCommandTarget
{
    Task OnEditorKeyDown(string key, bool ctrl, bool shift, bool alt, bool onBoard);
}
