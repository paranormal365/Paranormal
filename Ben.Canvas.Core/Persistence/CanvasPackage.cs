using System.IO.Compression;
using System.Text;
using System.Text.RegularExpressions;
using Ben.Canvas.Core.Model;
using Ben.Canvas.Core.Serialization;
using Ben.Canvas.Core.Text;

namespace Ben.Canvas.Core.Persistence;

/// <summary>A stored picture or file carried in an export.</summary>
public sealed record PackageAsset(Guid AssetId, string Ext, byte[] Bytes);

/// <summary>What reading an export produced, or the sentence that says why it could not be read.</summary>
public sealed record CanvasPackageReadResult(
    CanvasDocument? Document,
    IReadOnlyList<PackageAsset> Assets,
    string? Problem,
    IReadOnlyList<string> Refusals);

/// <summary>
/// The .ishcanvas export: a ZIP holding <c>document.json</c> and <c>assets/{assetId}{ext}</c>.
/// </summary>
/// <remarks>
/// <para>The browser writes and reads this format itself (wwwroot/js/packageInterop.js), so a 300 MB export
/// never passes through .NET memory on a phone. This class is the reference for the same format: the tests
/// and the browser tests prove each side reads what the other writes.</para>
///
/// <para>Pictures are already compressed, so assets are stored; only document.json is deflated. Entry names
/// are rebuilt from the parsed id and extension and never used as paths, so a crafted name such as
/// <c>assets/../../x.png</c> cannot write outside the store.</para>
/// </remarks>
public static partial class CanvasPackage
{
    public const string Extension = ".ishcanvas";
    public const string MimeType = "application/zip";
    public const string DocumentEntry = "document.json";
    public const string AssetFolder = "assets/";

    /// <summary>The most an export may hold. Larger boards are refused with the names of their largest files.</summary>
    public const long MaxExportBytes = 300L * 1024 * 1024;

    public static string AssetEntryName(Guid assetId, string ext) => AssetFolder + assetId.ToString("D") + ext;

    /// <summary>Reads an asset entry's id and extension; anything else in the archive is ignored.</summary>
    public static bool TryParseAssetEntry(string? name, out Guid assetId, out string ext)
    {
        assetId = Guid.Empty;
        ext = "";
        if (name is null) return false;
        var m = AssetName().Match(name);
        if (!m.Success || !Guid.TryParseExact(m.Groups["id"].Value, "D", out assetId)) return false;
        ext = m.Groups["ext"].Value.ToLowerInvariant();
        return true;
    }

    public static byte[] Write(CanvasDocument document, IReadOnlyList<PackageAsset> assets)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(assets);

        using var buffer = new MemoryStream();
        using (var zip = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true, entryNameEncoding: Encoding.UTF8))
        {
            var doc = zip.CreateEntry(DocumentEntry, CompressionLevel.Optimal);
            using (var stream = doc.Open())
                stream.Write(CanvasSerializer.SerializeToUtf8Bytes(document));

            foreach (var asset in assets.DistinctBy(a => a.AssetId))
            {
                var entry = zip.CreateEntry(AssetEntryName(asset.AssetId, asset.Ext), CompressionLevel.NoCompression);
                using var stream = entry.Open();
                stream.Write(asset.Bytes);
            }
        }

        return buffer.ToArray();
    }

    public static CanvasPackageReadResult Read(Stream zip, long maxAssetBytes)
    {
        ArgumentNullException.ThrowIfNull(zip);
        var assets = new List<PackageAsset>();
        var refusals = new List<string>();

        try
        {
            using var archive = new ZipArchive(zip, ZipArchiveMode.Read, leaveOpen: true);
            var docEntry = archive.GetEntry(DocumentEntry);
            if (docEntry is null) return new(null, assets, CanvasCopy.Sentences.ImportNotABoard, refusals);

            string json;
            using (var reader = new StreamReader(docEntry.Open(), Encoding.UTF8))
                json = reader.ReadToEnd();

            var (document, problem) = CanvasSerializer.Parse(json);
            if (document is null) return new(null, assets, problem ?? CanvasCopy.Sentences.ImportNotABoard, refusals);

            foreach (var entry in archive.Entries)
            {
                if (!TryParseAssetEntry(entry.FullName, out var id, out var ext)) continue;
                if (entry.Length > maxAssetBytes)
                {
                    refusals.Add(CanvasCopy.Sentences.ImportAssetTooLarge(id.ToString("D") + ext));
                    continue;
                }

                using var stream = entry.Open();
                using var copy = new MemoryStream();
                stream.CopyTo(copy);
                assets.Add(new PackageAsset(id, ext, copy.ToArray()));
            }

            return new(document, assets, null, refusals);
        }
        catch (InvalidDataException)
        {
            return new(null, [], CanvasCopy.Sentences.ImportNotABoard, refusals);
        }
    }

    /// <summary>A file name made from the board title: no characters any system refuses, at most 80 long.</summary>
    public static string SafeFileName(string? title)
    {
        var invalid = new HashSet<char>(Path.GetInvalidFileNameChars().Concat(['<', '>', ':', '"', '/', '\\', '|', '?', '*']));
        var cleaned = new string((title ?? "").Where(c => !char.IsControl(c) && !invalid.Contains(c)).ToArray()).Trim().TrimEnd('.');
        if (cleaned.Length > 80) cleaned = cleaned[..80].TrimEnd();
        return cleaned.Length == 0 ? CanvasCopy.Titles.DefaultBoardTitle : cleaned;
    }

    [GeneratedRegex(@"^assets/(?<id>[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12})(?<ext>\.[a-zA-Z0-9]{1,8})$", RegexOptions.CultureInvariant)]
    private static partial Regex AssetName();
}

/// <summary>The board list kept under bc-doc-index, read so that a damaged list never loses the boards it can still name.</summary>
public static class DocumentIndex
{
    public const string IndexKey = "bc-doc-index";
    public const string EntryPrefix = "bc-doc-";
    public const string ActiveKey = "bc-doc-active";

    public static string EntryKey(Guid localId) => EntryPrefix + localId.ToString("D");

    public static string Serialise(IEnumerable<DocumentSummary> summaries) =>
        CanvasSerializer.Serialize(summaries.ToList(), compact: true);

    /// <summary>The summaries, or null when the stored text is not a list at all (so callers can refuse to sweep).</summary>
    public static List<DocumentSummary>? Parse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return [];
        try
        {
            var list = CanvasSerializer.Deserialize<List<DocumentSummary?>>(json);
            return list?.Where(s => s is not null && s.LocalId != Guid.Empty).Select(s => s!).DistinctBy(s => s.LocalId).ToList();
        }
        catch (System.Text.Json.JsonException)
        {
            return null;
        }
    }
}
