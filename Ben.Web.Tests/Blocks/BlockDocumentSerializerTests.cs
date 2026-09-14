using Ben.Data.Common.Blocks;
using Xunit;

namespace Ben.Web.Tests.Blocks;

public sealed class BlockDocumentSerializerTests
{
    [Fact]
    public void A_document_round_trips_every_kind()
    {
        var doc = new BlockDocument
        {
            Blocks =
            [
                new Block { Id = "a", Kind = BlockKinds.Text, Html = "<p>Footsteps at <strong>2am</strong>.</p>" },
                new Block { Id = "b", Kind = BlockKinds.Image, UploadFileId = Guid.NewGuid(), Alt = "The hallway", Caption = "Upstairs" },
                new Block { Id = "c", Kind = BlockKinds.File, UploadFileId = Guid.NewGuid(), FileName = "deed.pdf", ContentType = "application/pdf", FileSize = 1234 },
                new Block { Id = "d", Kind = BlockKinds.Link, Url = "https://example.com/", PreviewTitle = "Example Domain", PreviewDomain = "example.com" },
                new Block
                {
                    Id = "e", Kind = BlockKinds.Map, MapRoute = BlockMapRoutes.Walking, Zoom = 15,
                    MapStops = [new BlockMapStop(35.9251, -86.8689, "Rest Haven Cemetery"), new BlockMapStop(35.9265, -86.8700, "The old house")],
                },
            ],
        };

        var back = BlockDocumentSerializer.Parse(BlockDocumentSerializer.Serialize(doc));

        Assert.Equal(5, back.Blocks.Count);
        Assert.Equal(doc.Blocks.Select(b => (b.Id, b.Kind)), back.Blocks.Select(b => (b.Id, b.Kind)));
        Assert.Equal("<p>Footsteps at <strong>2am</strong>.</p>", back.Blocks[0].Html);
        Assert.Equal(doc.Blocks[1].UploadFileId, back.Blocks[1].UploadFileId);
        Assert.Equal(1234, back.Blocks[2].FileSize);
        Assert.Equal("example.com", back.Blocks[3].PreviewDomain);
        Assert.Equal(BlockMapRoutes.Walking, back.Blocks[4].MapRoute);
        Assert.Equal("Rest Haven Cemetery", back.Blocks[4].MapStops![0].Label);
        Assert.Equal(-86.8700, back.Blocks[4].MapStops![1].Longitude);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Nothing_stored_is_an_empty_document(string? json)
    {
        var doc = BlockDocumentSerializer.Parse(json);
        Assert.Equal(BlockDocument.CurrentVersion, doc.Version);
        Assert.Empty(doc.Blocks);
    }

    [Fact]
    public void A_kind_this_build_does_not_know_is_refused_not_dropped()
    {
        var ex = Assert.Throws<BlockDocumentFormatException>(() =>
            BlockDocumentSerializer.Parse("""{"version":1,"blocks":[{"id":"x","kind":"table"}]}"""));
        Assert.Contains("table", ex.Message);
    }

    [Fact]
    public void A_document_from_a_newer_version_is_refused()
    {
        Assert.Throws<BlockDocumentFormatException>(() =>
            BlockDocumentSerializer.Parse("""{"version":2,"blocks":[]}"""));
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("""{"version":1,"blocks":[{"kind":"text"}]}""")]
    [InlineData("""{"version":1,"blocks":[{"id":"a","kind":"text"},{"id":"a","kind":"text"}]}""")]
    public void A_malformed_document_is_refused_with_a_sentence(string json)
    {
        var ex = Assert.Throws<BlockDocumentFormatException>(() => BlockDocumentSerializer.Parse(json));
        Assert.False(string.IsNullOrWhiteSpace(ex.Message));
    }

    [Fact]
    public void Nulls_are_not_written()
    {
        var json = BlockDocumentSerializer.Serialize(new BlockDocument { Blocks = [new Block { Id = "a", Html = "<p>x</p>" }] });
        Assert.DoesNotContain("uploadFileId", json);
        Assert.DoesNotContain("mapStops", json);
    }

    [Fact]
    public void The_excerpt_is_the_words_of_the_text_blocks()
    {
        var doc = new BlockDocument
        {
            Blocks =
            [
                new Block { Id = "a", Html = "<p>Deed search at the county office.</p>" },
                new Block { Id = "b", Kind = BlockKinds.Image, UploadFileId = Guid.NewGuid(), Caption = "Deed page" },
                new Block { Id = "c", Html = "<ul><li>1911 owner</li><li>1932 owner</li></ul>" },
            ],
        };
        Assert.Equal("Deed search at the county office. 1911 owner · 1932 owner", BlockDocumentSerializer.PlainTextExcerpt(doc));
    }

    [Fact]
    public void A_heading_and_list_items_do_not_run_into_the_words_after_them()
    {
        var doc = new BlockDocument
        {
            Blocks =
            [
                new Block { Id = "a", Html = "<h3>Who lived here before</h3><p>Four owners since 1924.</p><ul><li>Deed book 1162</li><li>Obituary, March 1982</li></ul>" },
            ],
        };
        Assert.Equal("Who lived here before · Four owners since 1924. Deed book 1162 · Obituary, March 1982",
            BlockDocumentSerializer.PlainTextExcerpt(doc));
    }

    [Fact]
    public void With_no_text_the_excerpt_falls_back_to_captions_titles_and_places()
    {
        var doc = new BlockDocument
        {
            Blocks =
            [
                new Block { Id = "a", Kind = BlockKinds.Link, Url = "https://example.com", PreviewTitle = "Census 1910" },
                new Block { Id = "b", Kind = BlockKinds.Map, MapStops = [new BlockMapStop(1, 2, "Cemetery")] },
            ],
        };
        Assert.Equal("Census 1910 · Cemetery", BlockDocumentSerializer.PlainTextExcerpt(doc));
    }

    [Fact]
    public void A_long_excerpt_is_cut_at_a_word_with_an_ellipsis()
    {
        var words = string.Join(' ', Enumerable.Repeat("haunting", 60));
        var excerpt = BlockDocumentSerializer.PlainTextExcerpt(
            new BlockDocument { Blocks = [new Block { Id = "a", Html = $"<p>{words}</p>" }] }, maxLength: 50);
        Assert.True(excerpt.Length <= 50, excerpt);
        Assert.EndsWith("…", excerpt);
        Assert.DoesNotContain("hauntin…", excerpt);
    }
}
