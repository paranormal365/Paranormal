using Ben.Canvas.Core.Model;

namespace Ben.Canvas.Editor.Services;

/// <summary>
/// Filling in a block with a file the case already holds.
/// </summary>
/// <remarks>
/// <para>
/// The one rule worth stating: the block gets the case's <c>UploadFileId</c> and no <c>AssetId</c>.
/// A dropped file is the other way round — it lives on the device until a save sends it up — and a
/// picked file that also claimed an AssetId would be uploaded again on the next save, leaving the
/// case holding the same photograph twice and charging the account for both.
/// </para>
/// <para>
/// Its own class rather than a lambda in the editor so the rule can be read and tested on its own.
/// </para>
/// </remarks>
internal static class CaseFileBlocks
{
    /// <summary>Fills freshly made block data in; null when that block cannot show a file.</summary>
    internal static NodeData? Fill(NodeData data, CanvasCaseFile file)
    {
        switch (data)
        {
            case ImageData image:
                image.AssetId = null;
                image.UploadFileId = file.UploadFileId;
                image.Caption = Words(file.Description);
                return image;

            case AudioData audio:
                audio.AssetId = null;
                audio.UploadFileId = file.UploadFileId;
                audio.FileName = file.FileName;
                audio.Size = file.FileSize;
                audio.ContentType = file.ContentType;
                audio.Caption = Words(file.Description);
                return audio;

            case VideoData video:
                video.AssetId = null;
                video.UploadFileId = file.UploadFileId;
                video.FileName = file.FileName;
                video.Size = file.FileSize;
                video.ContentType = file.ContentType;
                video.Caption = Words(file.Description);
                return video;

            case FileData chip:
                chip.AssetId = null;
                chip.UploadFileId = file.UploadFileId;
                chip.FileName = file.FileName;
                chip.Size = file.FileSize;
                chip.ContentType = file.ContentType;
                return chip;

            default:
                return null;
        }
    }

    /// <summary>A description of nothing but spaces is no caption at all.</summary>
    private static string? Words(string? text) => string.IsNullOrWhiteSpace(text) ? null : text.Trim();
}
