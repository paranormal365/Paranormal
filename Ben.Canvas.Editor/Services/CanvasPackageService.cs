using Ben.Canvas.Core.Commands;
using Ben.Canvas.Core.Model;
using Ben.Canvas.Core.Options;
using Ben.Canvas.Core.Persistence;
using Ben.Canvas.Core.Serialization;
using Ben.Canvas.Core.Text;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.JSInterop;

namespace Ben.Canvas.Editor.Services;

/// <summary>What an export produced: problems to say, and on a touch device the link a tap must start.</summary>
/// <param name="DownloadUrl">Set when the download waits for a tap (iOS starts a download only from one).</param>
public sealed record ExportOutcome(bool Exported, IReadOnlyList<string> Problems, string? DownloadUrl, string? FileName);

/// <summary>What an import produced.</summary>
public sealed record ImportOutcome(bool Imported, IReadOnlyList<string> Problems);

/// <summary>
/// Export and import of .ishcanvas files (wwwroot/js/packageInterop.js builds and reads the ZIP).
/// </summary>
/// <remarks>
/// Only document.json crosses into .NET. An imported board goes through the same reader as every other board,
/// so its message HTML and link addresses are cleaned before anything renders them.
/// </remarks>
public sealed class CanvasPackageService(
    CanvasStore store,
    CanvasDocumentStore documents,
    IOptions<CanvasEditorOptions> options,
    IJSRuntime js,
    ILogger<CanvasPackageService> log) : IAsyncDisposable
{
    private IJSObjectReference? _module;
    private IJSObjectReference? _dom;

    private async Task<IJSObjectReference> ModuleAsync() => _module ??= await CanvasModules.ImportAsync(js, "js/packageInterop.js");

    private async Task<IJSObjectReference> DomAsync() => _dom ??= await CanvasModules.ImportAsync(js, "js/domInterop.js");

    public async Task<ExportOutcome> ExportAsync()
    {
        try
        {
            var document = store.Document;
            var json = CanvasSerializer.Serialize(document);
            var fileName = CanvasPackage.SafeFileName(document.Title) + CanvasPackage.Extension;
            var assets = AssetsOf(document);

            var module = await ModuleAsync();
            var result = await module.InvokeAsync<ExportResultDto>("exportPackage", json, assets, fileName, CanvasPackage.MaxExportBytes);

            var problems = new List<string>();
            if (result.TooLarge)
            {
                var mb = (int)Math.Ceiling(result.TotalBytes / 1024d / 1024d);
                problems.Add(CanvasCopy.Sentences.ExportTooLarge(mb, string.Join(", ", result.Largest ?? [])));
                return new ExportOutcome(false, problems, null, null);
            }

            if (result.Missing is { Count: > 0 } missing) problems.Add(CanvasCopy.Sentences.ExportAssetsMissing(missing.Count));
            if (result.Url is null) return new ExportOutcome(false, [CanvasCopy.Sentences.ExportFailed], null, null);

            var dom = await DomAsync();
            if (await dom.InvokeAsync<bool>("isCoarsePointer"))
                return new ExportOutcome(true, problems, result.Url, fileName);

            await dom.InvokeAsync<bool>("downloadUrl", result.Url, fileName);
            await dom.InvokeVoidAsync("revokeUrlLater", result.Url, 60_000);
            return new ExportOutcome(true, problems, null, fileName);
        }
        catch (JSException ex)
        {
            log.LogError(ex, "Export failed.");
            return new ExportOutcome(false, [CanvasCopy.Sentences.ExportFailed], null, null);
        }
    }

    /// <summary>Opens the file picker for a keyboard user; a pointer uses the label, which the browser opens itself.</summary>
    public async Task ChooseFileAsync(ElementReference input)
    {
        try { await (await DomAsync()).InvokeVoidAsync("clickElement", input); }
        catch (JSException) { }
    }

    /// <summary>Forgets a Download link the person closed without using.</summary>
    public async Task ReleaseDownloadAsync(string? url)
    {
        if (url is null) return;
        try { await (await DomAsync()).InvokeVoidAsync("revokeUrlLater", url, 60_000); }
        catch (JSException) { }
    }

    public async Task<ImportOutcome> ImportAsync(ElementReference input)
    {
        try
        {
            var module = await ModuleAsync();
            var result = await module.InvokeAsync<ImportResultDto>("importPackage", input, options.Value.MaxFileBytes);
            if (result.Problem == "none") return new ImportOutcome(false, []);
            if (result.Problem is not null || result.DocumentJson is null)
                return new ImportOutcome(false, [CanvasCopy.Sentences.ImportNotABoard]);

            var (document, problem) = CanvasSerializer.Parse(result.DocumentJson);
            if (document is null) return new ImportOutcome(false, [problem ?? CanvasCopy.Sentences.ImportNotABoard]);

            var problems = new List<string>();
            problems.AddRange((result.TooLarge ?? []).Select(CanvasCopy.Sentences.ImportAssetTooLarge));
            problems.AddRange((result.NotStored ?? []).Select(CanvasCopy.Sentences.AssetNotStored));

            if (!await documents.AdoptImportedAsync(document)) problems.Add(CanvasCopy.Sentences.StorageRefused);
            return new ImportOutcome(true, problems);
        }
        catch (JSException ex)
        {
            log.LogError(ex, "Import failed.");
            return new ImportOutcome(false, [CanvasCopy.Sentences.ImportNotABoard]);
        }
    }

    internal static List<ExportAsset> AssetsOf(CanvasDocument document)
    {
        var list = new List<ExportAsset>();
        foreach (var node in document.Nodes)
        {
            switch (node.Data)
            {
                case ImageData { AssetId: { } id, OpfsExt: { } ext } image:
                    list.Add(new ExportAsset(id.ToString("D"), ext, string.IsNullOrWhiteSpace(image.Caption) ? "picture" + ext : image.Caption));
                    break;
                case FileData { AssetId: { } id, OpfsExt: { } ext } file:
                    list.Add(new ExportAsset(id.ToString("D"), ext, string.IsNullOrWhiteSpace(file.FileName) ? "file" + ext : file.FileName));
                    break;
            }
        }

        return list.DistinctBy(a => a.AssetId + a.Ext).ToList();
    }

    public async ValueTask DisposeAsync()
    {
        foreach (var module in new[] { _module, _dom })
        {
            if (module is null) continue;
            try { await module.DisposeAsync(); }
            catch (Exception ex) when (ex is JSDisconnectedException or ObjectDisposedException) { }
        }
    }

    internal sealed record ExportAsset(string AssetId, string Ext, string Name);

    private sealed record ExportResultDto(string? Url, string? FileName, long Bytes, List<string>? Missing, bool TooLarge, long TotalBytes, List<string>? Largest);

    private sealed record ImportResultDto(string? Problem, string? DocumentJson, int Stored, List<string>? TooLarge, List<string>? NotStored);
}
