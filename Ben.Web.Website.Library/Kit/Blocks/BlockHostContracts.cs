using Ben.Data.Common.Blocks;

namespace Ben.Web.Website.Library.Kit.Blocks;

// The block editor knows about blocks and nothing else (Ben, 2026-09-14: "a set of reusable components … so we might be
// able to use it for more than just research"). Everything it needs from the page it sits on — where an upload goes, how a
// stored file becomes an address, how a pasted link becomes a card, how a place is found — arrives through these.

/// <summary>A file the person pasted, dropped or chose, for the host to store.</summary>
public sealed record BlockUploadRequest(string FileName, string ContentType, long Size, Stream Content);

/// <summary>Where the host stored a file, or why it did not.</summary>
public sealed record BlockUploadResult(Guid? UploadFileId, string FileName, string ContentType, long Size, string? Refusal)
{
    public static BlockUploadResult Refused(string fileName, string refusal) => new(null, fileName, "", 0, refusal);
}

/// <summary>The card for a pasted link, as the host made it, or why it could not.</summary>
public sealed record BlockLinkResult(
    string Url, string? Title, string? Description, string? ImageUrl, string? SiteName, string? Domain, string? Refusal = null);

/// <summary>Which address of a stored file the editor wants.</summary>
public enum BlockFileUrlKind
{
    /// <summary>A shrunken picture, for drawing an image block.</summary>
    Picture,

    /// <summary>The file itself, for downloading.</summary>
    Download,
}

/// <summary>A place found by a search, for a map block.</summary>
public sealed record BlockPlaceResult(double Latitude, double Longitude, string Label, string? Address);

/// <summary>What a map block needs from the page to find places. Without one, the Map block is not offered.</summary>
/// <param name="SearchAsync">A place for what was typed, or nothing.</param>
/// <param name="ReverseAsync">An address for a point tapped on the map, when the host can say.</param>
/// <param name="PlaceUrl">The page for a place this site lists, when a stop is one.</param>
public sealed record BlockMapHost(
    Func<string, Task<BlockPlaceResult?>> SearchAsync,
    Func<double, double, Task<string?>>? ReverseAsync = null,
    Func<Guid, string>? PlaceUrl = null);

/// <summary>One kind of block, as the editor's menus offer it.</summary>
public sealed record BlockKindDescriptor(string Kind, string Label, string Icon, string Help);

/// <summary>The kinds of block the editor offers, in the order it offers them.</summary>
/// <remarks>The table block Ben asked for next is added here and in <see cref="BlockKinds"/>.</remarks>
public static class BlockKindRegistry
{
    private static readonly List<BlockKindDescriptor> Kinds =
    [
        new(BlockKinds.Text, "Text", "type", "Words, with bold, lists, headings and links"),
        new(BlockKinds.Image, "Picture", "image", "A photo or scan, from a file, a paste or a drop"),
        new(BlockKinds.File, "File", "paperclip", "A document to download"),
        new(BlockKinds.Link, "Link", "link", "A web page, shown as a card"),
        new(BlockKinds.Map, "Map", "map-pin", "Places on a map, with a route between them"),
    ];

    public static IReadOnlyList<BlockKindDescriptor> All => Kinds;

    public static BlockKindDescriptor Of(string kind) =>
        Kinds.FirstOrDefault(k => k.Kind == kind) ?? throw new ArgumentOutOfRangeException(nameof(kind), kind, "No such kind of block.");

    /// <summary>Adds a kind, for a block the editor did not ship with.</summary>
    public static void Register(BlockKindDescriptor descriptor)
    {
        if (Kinds.Any(k => k.Kind == descriptor.Kind)) throw new InvalidOperationException($"\"{descriptor.Kind}\" is already a kind of block.");
        Kinds.Add(descriptor);
    }
}

/// <summary>New blocks, each with a fresh id and nothing in it the person did not put there.</summary>
public static class NewBlock
{
    private static string Id() => Guid.NewGuid().ToString("N");

    public static Block Text(string? html = null) => new() { Id = Id(), Kind = BlockKinds.Text, Html = html ?? "" };

    public static Block FromUpload(BlockUploadResult upload) => new()
    {
        Id = Id(),
        Kind = upload.ContentType.StartsWith("image/", StringComparison.OrdinalIgnoreCase) ? BlockKinds.Image : BlockKinds.File,
        UploadFileId = upload.UploadFileId, FileName = upload.FileName, ContentType = upload.ContentType, FileSize = upload.Size,
    };

    public static Block Link(BlockLinkResult link) => new()
    {
        Id = Id(), Kind = BlockKinds.Link, Url = link.Url,
        PreviewTitle = link.Title, PreviewDescription = link.Description, PreviewImageUrl = link.ImageUrl,
        PreviewSiteName = link.SiteName, PreviewDomain = link.Domain, PreviewFetchedUtc = link.Title is null ? null : DateTime.UtcNow,
    };

    /// <summary>
    /// A map with no places and no route. Never pre-filled: the places on a research page's map are ones the author chose,
    /// and the likeliest pre-fill — the case's own address — is exactly what must not appear there (2026-09-14).
    /// </summary>
    public static Block Map() => new() { Id = Id(), Kind = BlockKinds.Map, MapStops = [], MapRoute = BlockMapRoutes.None };
}
