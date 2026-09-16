using Ben.Canvas.Core.Model;

namespace Ben.Canvas.Core.Blocks;

/// <summary>
/// Which block a file the case already holds becomes when somebody picks it onto a board.
/// </summary>
/// <remarks>
/// <para>
/// A dropped file is read here, so paste decides what it is from its first bytes
/// (<see cref="Paste.ImageSignature"/>) and never has to trust a name. A picked file is on the
/// server and all the picker has is the row: a name and a declared content type. So this judges by
/// both, and falls back to a file chip — a picture shown as a chip is a disappointment, a picture
/// box fed a spreadsheet is a broken box.
/// </para>
/// <para>
/// HEIC is deliberately a picture here although paste refuses it: paste would have to store bytes no
/// browser can draw, whereas a HEIC already on the case is served through the API's picture route,
/// which hands back something a browser can show.
/// </para>
/// </remarks>
public static class CaseFileBlockKind
{
    /// <summary>Image, Audio, Video, or File for everything else. Never null: every file can be a chip.</summary>
    public static CanvasNodeType For(string? fileName, string? contentType)
    {
        var ext = System.IO.Path.GetExtension(fileName ?? "").ToLowerInvariant();
        var mime = (contentType ?? "").Split(';')[0].Trim().ToLowerInvariant();

        if (ext is ".jpg" or ".jpeg" or ".png" or ".gif" or ".bmp" or ".webp" or ".heic" or ".heif"
            || mime.StartsWith("image/", StringComparison.Ordinal))
        {
            return CanvasNodeType.Image;
        }

        return Paste.PasteClassifier.MediaKind(fileName, contentType) ?? CanvasNodeType.File;
    }
}
