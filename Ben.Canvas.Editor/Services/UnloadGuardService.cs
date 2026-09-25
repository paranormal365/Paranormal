using Ben.Canvas.Core.Persistence;
using Microsoft.JSInterop;

namespace Ben.Canvas.Editor.Services;

/// <summary>
/// Asks before the tab closes with work not yet stored, and writes a pending autosave when the page is hidden.
/// </summary>
/// <remarks>
/// Moved out of the component, where the video editor kept it, so the embedded board (M8) cleans up after
/// itself: a guard left installed by a disposed editor would ask about work that no longer exists. The
/// browser is told only when the answer changes.
/// </remarks>
public sealed class UnloadGuardService(CanvasDocumentStore documents, IJSRuntime js) : IAsyncDisposable
{
    private IJSObjectReference? _module;
    private DotNetObjectReference<UnloadGuardService>? _self;
    private bool _guarding;
    private bool _publishRunning;

    public async Task StartAsync()
    {
        _module = await CanvasModules.ImportAsync(js, "js/domInterop.js");
        _self = DotNetObjectReference.Create(this);
        await _module.InvokeVoidAsync("flushOnPageHide", _self);
        documents.Changed += OnDocumentsChanged;
        await UpdateAsync();
    }

    /// <summary>Set while a publish runs in this tab (M6).</summary>
    public bool PublishRunning
    {
        get => _publishRunning;
        set
        {
            _publishRunning = value;
            _ = UpdateAsync();
        }
    }

    [JSInvokable]
    public Task OnPageHiding() => documents.FlushAsync();

    private void OnDocumentsChanged() => _ = UpdateAsync();

    internal async Task UpdateAsync()
    {
        if (_module is null) return;
        var guard = UnloadGuardPolicy.ShouldGuard(documents.IsDirty, documents.AutosavePending, PublishRunning);
        if (guard == _guarding) return;
        _guarding = guard;
        try
        {
            await _module.InvokeVoidAsync("setUnloadGuard", guard, UnloadGuardPolicy.Reason(documents.IsDirty, PublishRunning));
        }
        catch (Exception ex) when (ex is JSDisconnectedException or ObjectDisposedException or JSException) { }
    }

    public async ValueTask DisposeAsync()
    {
        documents.Changed -= OnDocumentsChanged;
        if (_module is not null)
        {
            try
            {
                await _module.InvokeVoidAsync("setUnloadGuard", false, "");
                await _module.InvokeVoidAsync("stopFlushOnPageHide");
                await _module.DisposeAsync();
            }
            catch (Exception ex) when (ex is JSDisconnectedException or ObjectDisposedException or JSException) { }
        }

        _self?.Dispose();
    }
}
