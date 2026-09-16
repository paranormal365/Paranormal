using System.Text.Json;
using Ben.Canvas.Core.Options;
using Ben.Canvas.Core.Text;
using Microsoft.Extensions.Options;
using Microsoft.JSInterop;

namespace Ben.Canvas.Editor.Services;

/// <summary>The API's case files behind <see cref="ICanvasMediaStore"/>, fetched and uploaded by wwwroot/js/blobInterop.js.</summary>
public sealed class HttpCanvasMediaStore(IJSRuntime js, IOptions<CanvasEditorOptions> options, ICanvasAccessTokenSource? tokens = null, ICanvasSignInState? signIn = null) : ICanvasMediaStore, IAsyncDisposable
{
    private IJSObjectReference? _module;
    private readonly Dictionary<string, string> _urls = new(StringComparer.Ordinal);

    private sealed record FetchResult(int Status, string? Url);

    private sealed record UploadResult(int Status, string? Body);

    private sealed record CaseFileDto(Guid UploadFileId, string? ContentType, long FileSize);

    private string? Base => string.IsNullOrWhiteSpace(options.Value.ApiBaseUrl) ? null : options.Value.ApiBaseUrl.Trim().TrimEnd('/');

    public bool IsAvailable => Base is not null && tokens is not null && (signIn?.IsSignedIn ?? true);

    public async Task<(CanvasMediaUpload? Upload, string? Problem)> UploadAsync(Guid organizationId, Guid caseId, string sourceUrl, string fileName, string? description, CancellationToken ct = default)
    {
        if (!IsAvailable) return (null, CanvasCopy.Sentences.SignedOutSave);

        var url = $"{Base}/api/orgs/{organizationId}/cases/{caseId}/files";
        var module = await ModuleAsync();
        var result = await module.InvokeAsync<UploadResult>("uploadFromUrl", ct, url, await tokens!.GetAccessTokenAsync(false, ct), sourceUrl, fileName, description ?? "");
        if (result.Status == 401)
            result = await module.InvokeAsync<UploadResult>("uploadFromUrl", ct, url, await tokens.GetAccessTokenAsync(true, ct), sourceUrl, fileName, description ?? "");

        switch (result.Status)
        {
            case 200 or 201:
                var file = JsonSerializer.Deserialize<CaseFileDto>(result.Body ?? "", HttpCanvasServerStore.Json);
                return file is null
                    ? (null, CanvasCopy.Sentences.ServerRefusedBoard)
                    : (new CanvasMediaUpload(file.UploadFileId, file.ContentType ?? "application/octet-stream", file.FileSize), null);
            case 0: return (null, CanvasCopy.Sentences.ServerUnreachable);
            case 401: return (null, CanvasCopy.Sentences.SignInExpired);
            case 403: return (null, CanvasCopy.Sentences.AddFilesForbidden);
        }

        var text = (result.Body ?? "").Trim();
        return (null, text.Length is > 0 and < 400 && !text.StartsWith('{') && !text.StartsWith('<') ? text.Trim('"') : CanvasCopy.Sentences.ServerRefusedBoard);
    }

    public async Task<(IReadOnlyList<CanvasCaseFile> Files, string? Problem)> ListCaseFilesAsync(
        Guid organizationId, Guid caseId, CancellationToken ct = default)
    {
        if (!IsAvailable) return ([], CanvasCopy.Sentences.SignedOutSave);

        var url = $"{Base}/api/orgs/{organizationId}/cases/{caseId}/files";
        var module = await ModuleAsync();
        var result = await module.InvokeAsync<UploadResult>("fetchJson", ct, url, await tokens!.GetAccessTokenAsync(false, ct));
        // One retry on 401 with a fresh token, the same as an upload: a board left open over lunch
        // meets an expired token on its first reach, not a real refusal.
        if (result.Status == 401)
            result = await module.InvokeAsync<UploadResult>("fetchJson", ct, url, await tokens.GetAccessTokenAsync(true, ct));

        return result.Status switch
        {
            200 => (Read(result.Body), null),
            0 => ([], CanvasCopy.Sentences.ServerUnreachable),
            401 => ([], CanvasCopy.Sentences.SignInExpired),
            403 => ([], CanvasCopy.Sentences.AddFilesForbidden),
            _ => ([], CanvasCopy.Sentences.ServerRefusedBoard),
        };

        static IReadOnlyList<CanvasCaseFile> Read(string? body)
        {
            if (string.IsNullOrWhiteSpace(body)) return [];
            try
            {
                var files = JsonSerializer.Deserialize<List<CaseFileListDto>>(body, HttpCanvasServerStore.Json) ?? [];
                return files
                    .Where(f => f.UploadFileId != Guid.Empty)
                    .Select(f => new CanvasCaseFile(
                        f.UploadFileId,
                        string.IsNullOrWhiteSpace(f.FileName) ? "file" : f.FileName,
                        f.ContentType ?? "application/octet-stream",
                        f.FileSize,
                        f.Description))
                    .ToList();
            }
            catch (JsonException)
            {
                // An answer this cannot read is not a list of nothing — but the picker has nothing
                // useful to draw either way, and the empty state says so in words.
                return [];
            }
        }
    }

    private sealed record CaseFileListDto(
        Guid UploadFileId, string? FileName, string? ContentType, long FileSize, string? Description);

    public Task<string?> GetDisplayUrlAsync(Guid uploadFileId, bool thumbnail, CancellationToken ct = default) =>
        Base is null ? Task.FromResult<string?>(null) : FetchAsync($"{Base}/api/upload-files/{uploadFileId}/{(thumbnail ? "thumbnail" : "download")}", ct);

    public Task<string?> GetDisplayUrlAsync(string apiUrl, CancellationToken ct = default)
    {
        // The token goes only to the API (R15): "https://host/webapiX/..." must not pass for "https://host/webapi/...".
        var under = Base is not null && apiUrl.StartsWith(Base + "/", StringComparison.OrdinalIgnoreCase);
        return under ? FetchAsync(apiUrl, ct) : Task.FromResult<string?>(null);
    }

    private async Task<string?> FetchAsync(string url, CancellationToken ct)
    {
        if (!IsAvailable) return null;
        if (_urls.TryGetValue(url, out var cached)) return cached;

        try
        {
            var module = await ModuleAsync();
            var result = await module.InvokeAsync<FetchResult>("fetchAsObjectUrl", ct, url, await tokens!.GetAccessTokenAsync(false, ct));
            if (result.Status == 401)
                result = await module.InvokeAsync<FetchResult>("fetchAsObjectUrl", ct, url, await tokens.GetAccessTokenAsync(true, ct));

            if (result.Status != 200 || string.IsNullOrEmpty(result.Url)) return null;
            _urls[url] = result.Url;
            return result.Url;
        }
        catch (JSException)
        {
            return null;
        }
    }

    private async Task<IJSObjectReference> ModuleAsync() => _module ??= await CanvasModules.ImportAsync(js, "js/blobInterop.js");

    public async ValueTask DisposeAsync()
    {
        if (_module is null) return;
        try
        {
            foreach (var url in _urls.Values) await _module.InvokeVoidAsync("revokeObjectUrl", url);
            _urls.Clear();
            await _module.DisposeAsync();
        }
        catch (Exception ex) when (ex is JSDisconnectedException or ObjectDisposedException or JSException)
        {
            // The page is going away; the browser frees object URLs with it.
        }
    }
}
