using Ben.Canvas.Core.Options;
using Ben.Canvas.Core.Persistence;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.JSInterop;

namespace Ben.Canvas.Editor.Services;

/// <summary>
/// Which layout the editor is in (phone, iPad portrait, wide), the input it has, and the panel choices kept
/// on this device.
/// </summary>
/// <remarks>
/// <para>CSS already decides what is visible at first paint, so nothing flashes before this service starts.
/// This service feeds behaviour: which component hosts the properties (a side panel or a bottom sheet),
/// which sheet is open, and what to remember under <see cref="LayoutSnapshot.StorageKey"/>.</para>
/// <para>The media queries match the CSS breakpoints exactly: below 768 px is a phone, 768 to 1023.98 px is
/// an iPad in portrait.</para>
/// </remarks>
public sealed class CanvasLayoutState(IJSRuntime js, IOptions<CanvasEditorOptions> options, ILogger<CanvasLayoutState> log) : IAsyncDisposable
{
    internal static readonly string[] Queries =
    [
        "(max-width: 767.98px)",
        "(min-width: 768px) and (max-width: 1023.98px)",
        "(pointer: coarse)",
        "(prefers-reduced-motion: reduce)",
    ];

    public const string SheetProperties = "properties";
    public const string SheetAdd = "add";
    public const string SheetMore = "more";
    public const string SheetPaste = "paste";

    private IJSObjectReference? _dom;
    private IJSObjectReference? _storage;
    private IJSObjectReference? _watch;
    private DotNetObjectReference<CanvasLayoutState>? _self;

    public bool IsPhone { get; private set; }
    public bool IsTabletPortrait { get; private set; }
    public bool IsCoarsePointer { get; private set; }
    public bool ReducedMotion { get; private set; }

    public bool PropsOpen { get; private set; } = true;

    /// <summary>"half" or "full"; a sheet never reopens collapsed.</summary>
    public string SheetSnap { get; private set; } = "half";

    /// <summary>The open sheet on a phone, or null.</summary>
    public string? OpenSheet { get; private set; }

    public event Action? Changed;

    public async Task StartAsync()
    {
        _storage = await CanvasModules.ImportAsync(js, "js/storageInterop.js");
        var snapshot = (LayoutSnapshot.Deserialise(await _storage.InvokeAsync<string?>("getItem", LayoutSnapshot.StorageKey)) ?? new LayoutSnapshot()).Apply(options.Value);
        PropsOpen = snapshot.PropsOpen ?? true;
        SheetSnap = snapshot.SheetSnap is "full" ? "full" : "half";

        _dom = await CanvasModules.ImportAsync(js, "js/domInterop.js");
        _self = DotNetObjectReference.Create(this);
        _watch = await _dom.InvokeAsync<IJSObjectReference>("watchMedia", Queries, _self);
        Changed?.Invoke();
    }

    [JSInvokable]
    public void OnMediaChanged(bool[] matches)
    {
        if (matches is not { Length: 4 }) return;
        var wasPhone = IsPhone;
        IsPhone = matches[0];
        IsTabletPortrait = matches[1];
        IsCoarsePointer = matches[2];
        ReducedMotion = matches[3];

        // A sheet only exists on a phone. Leaving phone width turns the properties sheet into the side panel.
        if (wasPhone && !IsPhone && OpenSheet is not null)
        {
            if (OpenSheet == SheetProperties) PropsOpen = true;
            OpenSheet = null;
        }

        Changed?.Invoke();
    }

    public async Task SetPropsOpenAsync(bool open)
    {
        if (PropsOpen == open) return;
        PropsOpen = open;
        Changed?.Invoke();
        await PersistAsync();
    }

    public async Task SetSnapAsync(string snap)
    {
        if (snap is not ("half" or "full")) return;
        SheetSnap = snap;
        Changed?.Invoke();
        await PersistAsync();
    }

    public void Open(string sheet)
    {
        OpenSheet = sheet;
        Changed?.Invoke();
    }

    public void Close()
    {
        if (OpenSheet is null) return;
        OpenSheet = null;
        Changed?.Invoke();
    }

    private IJSObjectReference? _gestures;

    /// <summary>Lets the sheet's grip be dragged (wwwroot/js/boardGestures.js).</summary>
    public async Task AttachSheetAsync(Microsoft.AspNetCore.Components.ElementReference sheet)
    {
        try
        {
            _gestures ??= await CanvasModules.ImportAsync(js, "js/boardGestures.js");
            _self ??= DotNetObjectReference.Create(this);
            await _gestures.InvokeVoidAsync("attachSheet", sheet, _self);
        }
        catch (Exception ex) when (ex is JSException or JSDisconnectedException or ObjectDisposedException) { }
    }

    public async Task DetachSheetAsync(Microsoft.AspNetCore.Components.ElementReference sheet)
    {
        if (_gestures is null) return;
        try { await _gestures.InvokeVoidAsync("detachSheet", sheet); }
        catch (Exception ex) when (ex is JSException or JSDisconnectedException or ObjectDisposedException) { }
    }

    /// <summary>Where a grip drag ended: "half", "full" or "closed".</summary>
    [JSInvokable]
    public async Task OnSheetSnap(string snap)
    {
        if (snap == "closed")
        {
            Close();
            return;
        }

        await SetSnapAsync(snap);
    }

    /// <summary>The grip's click, for keyboard and VoiceOver users: half and full take turns.</summary>
    public Task CycleSnapAsync() => SetSnapAsync(SheetSnap == "full" ? "half" : "full");

    private async Task PersistAsync()
    {
        if (_storage is null) return;
        try
        {
            var snapshot = new LayoutSnapshot { PropsOpen = PropsOpen, SheetSnap = SheetSnap, ShowMinimap = options.Value.ShowMinimap };
            await _storage.InvokeAsync<bool>("setItem", LayoutSnapshot.StorageKey, snapshot.Serialise());
        }
        catch (JSException ex)
        {
            log.LogWarning(ex, "Could not keep the panel layout.");
        }
    }

    public async ValueTask DisposeAsync()
    {
        try
        {
            if (_dom is not null && _watch is not null) await _dom.InvokeVoidAsync("unwatchMedia", _watch);
            foreach (var module in new[] { _watch, _dom, _storage, _gestures })
                if (module is not null) await module.DisposeAsync();
        }
        catch (Exception ex) when (ex is JSDisconnectedException or ObjectDisposedException or JSException) { }

        _self?.Dispose();
    }
}
