using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using Microsoft.JSInterop;

namespace Ben.Canvas.Editor.Services;

/// <summary>One stored picture or file, as the device store lists it.</summary>
public sealed record StoredAsset(Guid AssetId, string Ext, long Size, double Modified);

/// <summary>
/// The pictures and files pasted onto boards, kept on this device (wwwroot/js/opfsInterop.js).
/// </summary>
/// <remarks>
/// <para>The bytes never enter .NET: the store hands out blob: addresses for display and the export reads
/// the files in the browser.</para>
///
/// <para>Nothing here starts the browser module on its own. The editor starts it once during startup, and
/// until then every lookup answers null, so a block rendered before startup (or in a static render) shows
/// its placeholder rather than failing. <see cref="Ready"/> tells the blocks to look again.</para>
/// </remarks>
public sealed partial class CanvasAssetStore(IJSRuntime js, ILogger<CanvasAssetStore> log) : IAsyncDisposable
{
    private IJSObjectReference? _module;
    private Task? _init;
    private readonly Dictionary<string, string> _urls = new(StringComparer.Ordinal);

    /// <summary>"opfs", "idb", or empty when this browser keeps nothing.</summary>
    public string Backend { get; private set; } = "";

    public bool IsAvailable => _module is not null && Backend.Length > 0;

    /// <summary>Raised once the store has started, so blocks drawn before then can fetch their pictures.</summary>
    public event Action? Ready;

    public Task StartAsync() => _init ??= StartCoreAsync();

    private async Task StartCoreAsync()
    {
        try
        {
            _module = await CanvasModules.ImportAsync(js, "js/opfsInterop.js");
            Backend = await _module.InvokeAsync<string?>("isAvailable") ?? "";
        }
        catch (Exception ex) when (ex is JSException or InvalidOperationException or TaskCanceledException)
        {
            log.LogWarning(ex, "The device store for pictures could not start.");
            Backend = "";
        }

        Ready?.Invoke();
    }

    public static bool IsValidExt(string? ext) => ext is not null && ExtPattern().IsMatch(ext);

    private static string Key(Guid id, string ext) => id.ToString("D") + ext;

    /// <summary>The picture's display address, or null when it is not on this device.</summary>
    public async ValueTask<string?> GetUrlAsync(Guid assetId, string? ext)
    {
        if (!IsAvailable || !IsValidExt(ext)) return null;
        var key = Key(assetId, ext!);
        if (_urls.TryGetValue(key, out var cached)) return cached;

        try
        {
            var url = await _module!.InvokeAsync<string?>("assetUrl", assetId.ToString("D"), ext);
            if (url is not null) _urls[key] = url;
            return url;
        }
        catch (JSException ex)
        {
            log.LogWarning(ex, "Could not read stored asset {Asset}.", key);
            return null;
        }
    }

    public async Task<IReadOnlyList<StoredAsset>> ListAsync()
    {
        if (!IsAvailable) return [];
        try
        {
            var list = await _module!.InvokeAsync<List<StoredAssetDto>?>("assetList");
            return (list ?? [])
                .Where(a => Guid.TryParseExact(a.AssetId, "D", out _) && IsValidExt(a.Ext))
                .Select(a => new StoredAsset(Guid.ParseExact(a.AssetId!, "D"), a.Ext!, a.Size, a.Modified))
                .ToList();
        }
        catch (JSException ex)
        {
            log.LogWarning(ex, "Could not list stored assets.");
            return [];
        }
    }

    public async Task<bool> DeleteAsync(Guid assetId, string? ext)
    {
        if (!IsAvailable || !IsValidExt(ext)) return false;
        _urls.Remove(Key(assetId, ext!));
        try
        {
            return await _module!.InvokeAsync<bool>("assetDelete", assetId.ToString("D"), ext);
        }
        catch (JSException)
        {
            return false;
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_module is null) return;
        try
        {
            await _module.InvokeVoidAsync("assetRevokeAll");
            await _module.DisposeAsync();
        }
        catch (Exception ex) when (ex is JSDisconnectedException or ObjectDisposedException or JSException) { }
    }

    private sealed record StoredAssetDto(string? AssetId, string? Ext, long Size, double Modified);

    [GeneratedRegex(@"^\.[a-z0-9]{1,8}$", RegexOptions.CultureInvariant)]
    private static partial Regex ExtPattern();
}
