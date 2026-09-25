using Microsoft.AspNetCore.Components.Forms;

namespace Ben.Web.Website.Library.Store.Editor;

/// <summary>How the store's editors — the admin's and, since store sellers P3, the seller's — send a chosen file to the API (storefront S1.11).</summary>
/// <remarks>
/// Buffered first, always: a browser file stream handed straight to request content freezes the
/// page and replays the click on reconnect (found on the default-avatar upload). The ceiling is a
/// read limit, not a policy — the API decides what a picture may be.
/// </remarks>
internal static class StoreUploads
{
    public const long MaxBytes = 40L * 1024 * 1024;

    public const string TooLarge = "That file is too large — 40 MB at most.";

    public static async Task<MultipartFormDataContent> ContentAsync(
        IBrowserFile file, IReadOnlyDictionary<string, string?>? fields = null)
    {
        using var buffer = new MemoryStream();
        await using (var browser = file.OpenReadStream(MaxBytes)) await browser.CopyToAsync(buffer);

        var content = new MultipartFormDataContent();
        var bytes = new ByteArrayContent(buffer.ToArray());
        bytes.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(
            string.IsNullOrWhiteSpace(file.ContentType) ? "application/octet-stream" : file.ContentType);
        content.Add(bytes, "file", file.Name);
        foreach (var (name, value) in fields ?? new Dictionary<string, string?>())
            if (!string.IsNullOrWhiteSpace(value)) content.Add(new StringContent(value), name);
        return content;
    }
}
