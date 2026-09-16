using System.IO.Compression;
using System.Text;
using Ben.Canvas.Core.Model;
using Ben.Canvas.Core.Persistence;
using Ben.Canvas.Tests.Support;

namespace Ben.Canvas.Tests.Persistence;

/// <summary>
/// The .ishcanvas export format: what goes in, what comes back, and what a crafted file cannot do.
/// </summary>
public sealed class CanvasPackageTests
{
    private static CanvasDocument BoardWithPicture(Guid assetId) => new()
    {
        Title = "Porch hunt",
        Nodes = [Set(TestBoards.Node(CanvasNodeType.Image), new ImageData { AssetId = assetId, OpfsExt = ".png" })],
    };

    private static CanvasNode Set(CanvasNode node, NodeData data)
    {
        node.Data = data;
        return node;
    }

    private static MemoryStream Zip(params (string Name, byte[] Bytes)[] entries)
    {
        var buffer = new MemoryStream();
        using (var zip = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var (name, bytes) in entries)
            {
                using var stream = zip.CreateEntry(name).Open();
                stream.Write(bytes);
            }
        }

        buffer.Position = 0;
        return buffer;
    }

    [Fact]
    public void A_package_round_trips_the_board_and_its_pictures()
    {
        var id = Guid.NewGuid();
        var bytes = CanvasPackage.Write(BoardWithPicture(id), [new PackageAsset(id, ".png", TestBoards.PngHead)]);

        var read = CanvasPackage.Read(new MemoryStream(bytes), 1_000_000);

        Assert.Null(read.Problem);
        Assert.Equal("Porch hunt", read.Document!.Title);
        var asset = Assert.Single(read.Assets);
        Assert.Equal(id, asset.AssetId);
        Assert.Equal(".png", asset.Ext);
        Assert.Equal(TestBoards.PngHead, asset.Bytes);
    }

    [Fact]
    public void Pictures_are_stored_and_only_the_board_is_compressed()
    {
        var id = Guid.NewGuid();
        var bytes = CanvasPackage.Write(BoardWithPicture(id), [new PackageAsset(id, ".png", new byte[4096])]);
        using var zip = new ZipArchive(new MemoryStream(bytes));
        var picture = zip.GetEntry(CanvasPackage.AssetEntryName(id, ".png"))!;
        Assert.Equal(picture.Length, picture.CompressedLength);
        Assert.NotNull(zip.GetEntry(CanvasPackage.DocumentEntry));
    }

    [Fact]
    public void A_zip_without_a_board_is_refused()
    {
        var read = CanvasPackage.Read(Zip(("notes.txt", "hello"u8.ToArray())), 1_000_000);
        Assert.Null(read.Document);
        Assert.Contains("not a board", read.Problem);
    }

    [Fact]
    public void A_text_file_named_ishcanvas_is_refused()
    {
        var read = CanvasPackage.Read(new MemoryStream("hello"u8.ToArray()), 1_000_000);
        Assert.Null(read.Document);
        Assert.Contains("not a board", read.Problem);
    }

    [Fact]
    public void A_crafted_entry_name_is_ignored_never_used_as_a_path()
    {
        var doc = Encoding.UTF8.GetBytes(Core.Serialization.CanvasSerializer.Serialize(new CanvasDocument()));
        var read = CanvasPackage.Read(Zip(
            ("document.json", doc),
            ("assets/../../evil.png", TestBoards.PngHead),
            ("assets/not-a-guid.png", TestBoards.PngHead),
            ($"assets/{Guid.NewGuid():D}.p/g", TestBoards.PngHead)), 1_000_000);

        Assert.Null(read.Problem);
        Assert.Empty(read.Assets);
    }

    [Fact]
    public void An_oversized_picture_is_left_out_by_name()
    {
        var id = Guid.NewGuid();
        var bytes = CanvasPackage.Write(BoardWithPicture(id), [new PackageAsset(id, ".png", new byte[2048])]);
        var read = CanvasPackage.Read(new MemoryStream(bytes), 1024);

        Assert.NotNull(read.Document);
        Assert.Empty(read.Assets);
        Assert.Contains(id.ToString("D"), Assert.Single(read.Refusals));
    }

    [Fact]
    public void An_imported_message_loses_its_script()
    {
        var board = new CanvasDocument { Nodes = [Set(TestBoards.Node(CanvasNodeType.Message), new MessageData { Html = "<p>Hi</p><img src=x onerror=alert(1)>" })] };
        var read = CanvasPackage.Read(new MemoryStream(CanvasPackage.Write(board, [])), 1_000_000);
        var html = ((MessageData)read.Document!.Nodes[0].Data).Html;
        Assert.DoesNotContain("onerror", html);
        Assert.Contains("Hi", html);
    }

    [Theory]
    [InlineData("Porch: hunt/2?", "Porch hunt2")]
    [InlineData("   ", "Untitled board")]
    [InlineData(null, "Untitled board")]
    [InlineData("ab", "ab")]
    public void A_file_name_keeps_only_characters_every_system_accepts(string? title, string expected)
    {
        Assert.Equal(expected, CanvasPackage.SafeFileName(title));
    }

    [Fact]
    public void A_file_name_is_at_most_eighty_characters()
    {
        Assert.Equal(80, CanvasPackage.SafeFileName(new string('x', 200)).Length);
    }

    [Fact]
    public void Asset_entry_names_parse_back_to_their_id_and_extension()
    {
        var id = Guid.NewGuid();
        Assert.True(CanvasPackage.TryParseAssetEntry(CanvasPackage.AssetEntryName(id, ".jpg"), out var parsed, out var ext));
        Assert.Equal(id, parsed);
        Assert.Equal(".jpg", ext);
    }
}

/// <summary>The board list: a damaged list keeps what it can, and an unreadable one says so.</summary>
public sealed class DocumentIndexTests
{
    [Fact]
    public void No_list_yet_is_an_empty_list()
    {
        Assert.Empty(DocumentIndex.Parse(null)!);
    }

    [Fact]
    public void A_list_that_is_not_json_is_unreadable_so_nothing_is_swept()
    {
        Assert.Null(DocumentIndex.Parse("{not json"));
    }

    [Fact]
    public void Blank_and_repeated_entries_are_dropped()
    {
        var id = Guid.NewGuid();
        var json = $$"""[{"localId":"{{id}}","title":"A"},null,{"localId":"{{id}}","title":"B"},{"title":"no id"}]""";
        var list = DocumentIndex.Parse(json)!;
        Assert.Equal("A", Assert.Single(list).Title);
    }

    [Fact]
    public void The_list_round_trips_with_asset_ids()
    {
        var asset = Guid.NewGuid();
        var summary = new DocumentSummary { LocalId = Guid.NewGuid(), Title = "Attic", AssetIds = [asset] };
        var back = DocumentIndex.Parse(DocumentIndex.Serialise([summary]))!;
        Assert.Equal(asset, Assert.Single(Assert.Single(back).AssetIds));
    }
}
