using Microsoft.JSInterop;

namespace Ben.Video.Editor.Services;

/// <summary>
/// Ensures a clip's source file exists in the sidecar's cache before any job that needs it —
/// extracted from <see cref="SidecarSegmentClient"/> in item #70 phase 159, when probe and
/// thumbnail jobs became a second and third caller of the exact same step.
///
/// <para>The HEAD-before-PUT matters more now than it did with one caller: importing a clip
/// runs a probe and then a thumbnail job against the same source, and the background renderer
/// may render segments from it moments later. Without the HEAD short-circuit each of those would
/// re-upload the same (potentially very large) file. With it, only the first pays.</para>
///
/// <para>The upload goes through <c>sidecarInterop.js</c> rather than <see cref="HttpClient"/>
/// because it streams an OPFS <c>File</c> handle straight to the socket — routing it through C#
/// would mean materializing the whole file as a <c>byte[]</c> on the WASM heap first, which is
/// exactly the kind of main-thread/memory pressure this whole arc exists to remove.</para>
/// </summary>
public sealed class SidecarSourceUploader(OPFSService opfs, IJSRuntime js)
{
    private const string ModulePath = "js/sidecarInterop.js";

    public async Task EnsureUploadedAsync(
        string baseUrl, string token, Guid clipId, string ext, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        var url = $"{baseUrl}/v1/sources/{clipId:N}?ext={Uri.EscapeDataString(ext)}";
        var module = await js.InvokeAsync<IJSObjectReference>("benImportEditorModule", ModulePath);
        try
        {
            var alreadyCached = await module.InvokeAsync<bool>("headSource", url, token);
            if (alreadyCached) return;

            var fileRef = await opfs.ReadAsJSFileAsync(clipId, ext)
                ?? throw new InvalidOperationException("OPFS source file missing.");
            await module.InvokeVoidAsync("putSourceFile", url, token, fileRef);
        }
        finally
        {
            await module.DisposeAsync();
        }
    }
}
