using Microsoft.JSInterop;

namespace Ben.Video.Editor.Services;

/// <summary>
/// Owns the second, independent ffmpeg.wasm instance (<c>renderWorkerInterop.js</c>) used for
/// item #36's background preview rendering — a slim sibling of <see cref="FfmpegService"/> with
/// only the primitives <see cref="RenderWorkerBackend"/> needs, and its own state entirely
/// separate from the main instance's (so background rendering never shows up as "Processing" on
/// the toolbar, and vice versa).
/// </summary>
public sealed class RenderWorkerService : IAsyncDisposable
{
    private const string ModulePath = "/_content/Ben.Video.Editor/js/renderWorkerInterop.js";

    private readonly IJSRuntime _js;
    private readonly OPFSService _opfs;
    private IJSObjectReference? _module;
    private DotNetObjectReference<RenderWorkerService>? _selfRef;
    private bool _loaded;

    public event Action<int>? OnProgress;

    public RenderWorkerService(IJSRuntime js, OPFSService opfs)
    {
        _js   = js;
        _opfs = opfs;
    }

    public async Task LoadAsync()
    {
        if (_loaded) return;
        _module ??= await _js.InvokeAsync<IJSObjectReference>("import", ModulePath);
        _selfRef ??= DotNetObjectReference.Create(this);
        await _module.InvokeVoidAsync("loadCore", _selfRef);
        _loaded = true;
    }

    [JSInvokable]
    public void OnRenderWorkerProgress(int percent) => OnProgress?.Invoke(percent);

    /// <summary>Zero-copy-mounts an OPFS source clip into this instance's MEMFS and returns the
    /// path to use as an ffmpeg input. Each clip gets its own mount directory (its id) so multiple
    /// sources can be mounted concurrently without colliding.</summary>
    public async Task<string?> MountSourceAsync(Guid clipId, string ext)
    {
        if (!_loaded) return null;
        var fileRef = await _opfs.ReadAsJSFileAsync(clipId, ext);
        if (fileRef is null) return null;
        var mountDir = $"/src_{clipId:N}";
        return await _module!.InvokeAsync<string>("mountWorkerFs", fileRef, mountDir);
    }

    /// <summary>Fallback for sources with no OPFS entry (e.g. clips imported via the Server/media-library
    /// tab, which write straight into the main ffmpeg instance's MEMFS and never touch OPFS at all —
    /// see RenderWorkerBackend). Not zero-copy like <see cref="MountSourceAsync"/>, but correct
    /// for every clip regardless of import path.</summary>
    public async Task WriteBytesAsync(string name, byte[] bytes)
    {
        EnsureLoaded();
        await _module!.InvokeVoidAsync("writeFileFromBytes", name, bytes);
    }

    public async Task UnmountSourceAsync(Guid clipId)
    {
        if (!_loaded || _module is null) return;
        try { await _module.InvokeVoidAsync("unmountWorkerFs", $"/src_{clipId:N}"); } catch { }
    }

    public async Task<int> ExecAsync(string[] args)
    {
        EnsureLoaded();
        return await _module!.InvokeAsync<int>("exec", new object[] { args });
    }

    public async Task<int> ConcatCopyAsync(string[] segmentNames, string outputName)
    {
        EnsureLoaded();
        return await _module!.InvokeAsync<int>("concatCopy", new object[] { segmentNames, outputName });
    }

    public async Task<byte[]> ReadFileAsync(string name)
    {
        EnsureLoaded();
        return await _module!.InvokeAsync<byte[]>("readFile", name);
    }

    public async Task DeleteFileAsync(string name)
    {
        if (!_loaded || _module is null) return;
        try { await _module.InvokeVoidAsync("deleteFile", name); } catch { }
    }

    public async Task TerminateAsync()
    {
        if (_module is not null)
        {
            try { await _module.InvokeVoidAsync("terminate"); } catch { }
        }
        _loaded = false;
    }

    private void EnsureLoaded()
    {
        if (!_loaded || _module is null)
            throw new InvalidOperationException("RenderWorkerService.LoadAsync() must complete before use.");
    }

    public async ValueTask DisposeAsync()
    {
        await TerminateAsync();
        _selfRef?.Dispose();
        if (_module is not null)
        {
            try { await _module.DisposeAsync(); } catch { }
        }
    }
}
