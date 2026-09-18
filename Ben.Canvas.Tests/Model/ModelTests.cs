using System.Reflection;
using System.Text.Json.Serialization;
using Ben.Canvas.Core.Blocks;
using Ben.Canvas.Core.Model;
using Ben.Canvas.Core.Options;
using Ben.Canvas.Tests.Support;

namespace Ben.Canvas.Tests.Model;

/// <summary>
/// Core stays a plain library. A browser or ASP.NET reference here would make the engine untestable
/// without a browser and would drag the web framework into every host.
/// </summary>
public sealed class CoreProjectTests
{
    [Fact]
    public void The_core_library_has_no_package_or_browser_references()
    {
        var csproj = File.ReadAllText(Path.Combine(RepoFiles.CoreRoot(), "Ben.Canvas.Core.csproj"));
        var xml = RepoFiles.StripComments(csproj, "xml");
        Assert.DoesNotContain("<PackageReference", xml);
        Assert.DoesNotContain("<FrameworkReference", xml);
        Assert.DoesNotContain("<ProjectReference", xml);

        string[] banned = ["Microsoft.AspNetCore", "Microsoft.JSInterop", "IJSRuntime", "Newtonsoft"];
        var offenders = RepoFiles.Files(RepoFiles.CoreRoot(), "*.cs")
            .Where(f => banned.Any(b => File.ReadAllText(f).Contains(b, StringComparison.Ordinal)))
            .Select(RepoFiles.Relative)
            .ToList();

        Assert.True(offenders.Count == 0, "Core must not reference the browser or ASP.NET: " + string.Join(", ", offenders));
    }
}

/// <summary>A new board and the colour palette.</summary>
public sealed class CanvasModelTests
{
    [Fact]
    public void A_new_document_is_an_untitled_board_at_the_current_format()
    {
        var document = new CanvasDocument();
        Assert.Equal("Untitled board", document.Title);
        Assert.Equal(1, document.SchemaVersion);
        Assert.Empty(document.Nodes);
        Assert.Empty(document.Edges);
        Assert.Empty(document.Groups);
        Assert.Equal(0, document.NextZ);
    }

    [Theory]
    [InlineData("1", true)]
    [InlineData("2", true)]
    [InlineData("3", true)]
    [InlineData("4", true)]
    [InlineData("5", true)]
    [InlineData("6", true)]
    [InlineData("0", false)]
    [InlineData("7", false)]
    [InlineData("#fff", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void Palette_accepts_one_to_six_and_nothing_else(string? key, bool valid) =>
        Assert.Equal(valid, CanvasPalette.IsValid(key));
}

/// <summary>Block data copies deeply, and every kind has its own name in the file.</summary>
public sealed class NodeDataTests
{
    public static TheoryData<CanvasNodeType> AllTypes() => [.. Enum.GetValues<CanvasNodeType>()];

    [Theory]
    [MemberData(nameof(AllTypes))]
    public void Clone_is_deep(CanvasNodeType type)
    {
        var original = BlockRegistry.Get(type).CreateDefaultData(DateTime.UtcNow);
        switch (original)
        {
            case CardData card: card.Fields["description"] = "cold spot"; break;
            case MapData map: map.Pins.Add(new MapPin { Title = "porch" }); break;
        }

        var copy = original.Clone();
        Assert.NotSame(original, copy);

        switch (copy)
        {
            case CardData card:
                card.Fields["description"] = "changed";
                Assert.Equal("cold spot", ((CardData)original).Fields["description"]);
                break;
            case MapData map:
                map.Pins[0].Title = "changed";
                map.Pins.Add(new MapPin());
                Assert.Equal("porch", ((MapData)original).Pins[0].Title);
                Assert.Single(((MapData)original).Pins);
                break;
            case LinkData link:
                link.Url = "https://changed.example";
                Assert.Equal("", ((LinkData)original).Url);
                break;
            case TextData text:
                text.Text = "changed";
                Assert.Equal("", ((TextData)original).Text);
                break;
        }
    }

    [Fact]
    public void Every_kind_has_a_distinct_discriminator()
    {
        var names = typeof(NodeData).GetCustomAttributes<JsonDerivedTypeAttribute>()
            .Select(a => (string)a.TypeDiscriminator!)
            .OrderBy(n => n, StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(["audio", "card", "file", "image", "link", "map", "message", "table", "text", "video"], names);
    }

    /// <summary>
    /// A link card keeps two picture addresses and no third: the page's own (loaded only through the
    /// API's proxy, never stored as a proxy address) and our own kept copy, which any browser can load.
    /// </summary>
    /// <remarks>
    /// This test used to say <c>ImageUrl</c> must not exist at all, which was the right rule while the
    /// only alternative to the page's own address was a proxy address with a token in it — that would
    /// have been stored and then died. The kept copy is not that: the API serves it to anybody at a
    /// stable path, which is what lets a published board keep its pictures for the people it was
    /// published for (Ben, 2026-09-17). The rule the old assertion protected is now held by
    /// <c>TieredLinkPreviewProviderTests.Only_a_kept_previews_own_path_becomes_a_picture</c>, which
    /// refuses every address shape but that one.
    /// </remarks>
    [Fact]
    public void A_link_stores_the_pages_own_picture_and_our_kept_copy_of_it()
    {
        Assert.NotNull(typeof(LinkData).GetProperty("ImageSourceUrl"));
        Assert.NotNull(typeof(LinkData).GetProperty("ImageUrl"));

        // Nothing ticketed or proxied: those are built when a card renders and never written down.
        Assert.Null(typeof(LinkData).GetProperty("ImageProxyUrl"));
        Assert.Null(typeof(LinkData).GetProperty("ImageTicket"));
    }
}

/// <summary>The block registry's sizes are the designed ones and every default is usable.</summary>
public sealed class BlockRegistryTests
{
    [Fact]
    public void Every_node_type_has_a_descriptor()
    {
        foreach (var type in Enum.GetValues<CanvasNodeType>())
            Assert.Equal(type, BlockRegistry.Get(type).Type);
        Assert.Equal(Enum.GetValues<CanvasNodeType>().Length, BlockRegistry.All.Count);
    }

    [Fact]
    public void Every_default_size_meets_its_minimum()
    {
        foreach (var d in BlockRegistry.All)
        {
            Assert.True(d.DefaultWidth >= d.MinWidth, $"{d.Type} default width is below its minimum.");
            Assert.True(d.DefaultHeight >= d.MinHeight, $"{d.Type} default height is below its minimum.");
        }
    }

    [Theory]
    [InlineData(CanvasNodeType.Card, 220, 140)]
    [InlineData(CanvasNodeType.Message, 240, 120)]
    [InlineData(CanvasNodeType.Map, 240, 180)]
    [InlineData(CanvasNodeType.Image, 120, 90)]
    [InlineData(CanvasNodeType.Link, 220, 110)]
    [InlineData(CanvasNodeType.Text, 160, 80)]
    [InlineData(CanvasNodeType.File, 200, 72)]
    public void Minimums_match_the_designed_values(CanvasNodeType type, double width, double height)
    {
        var d = BlockRegistry.Get(type);
        Assert.Equal(width, d.MinWidth);
        Assert.Equal(height, d.MinHeight);
    }

    [Fact]
    public void Disabled_blocks_are_not_offered_in_the_palette()
    {
        var options = new CanvasEditorOptions();
        options.EnabledBlocks.Remove(CanvasNodeType.Map);
        Assert.DoesNotContain(BlockRegistry.Enabled(options), d => d.Type == CanvasNodeType.Map);
        Assert.Equal(9, BlockRegistry.Enabled(options).Count);
    }

    [Fact]
    public void Default_data_is_a_fresh_instance_each_call()
    {
        var d = BlockRegistry.Get(CanvasNodeType.Card);
        Assert.NotSame(d.CreateDefaultData(DateTime.UtcNow), d.CreateDefaultData(DateTime.UtcNow));
    }

    [Theory]
    [MemberData(nameof(NodeDataTests.AllTypes), MemberType = typeof(NodeDataTests))]
    public void Default_data_kind_matches_the_type(CanvasNodeType type)
    {
        var data = BlockRegistry.Get(type).CreateDefaultData(DateTime.UtcNow);
        Assert.Equal(type.ToString() + "Data", data.GetType().Name);
    }
}

/// <summary>A host's configuration is checked when it is made, not when a board misbehaves later.</summary>
public sealed class CanvasEditorOptionsTests
{
    [Fact]
    public void Duplicate_field_keys_are_refused()
    {
        var options = new CanvasEditorOptions();
        options.CardTemplates[0].Fields.Add(new CardField("date", "Again", CardFieldKind.Text));
        Assert.Contains("date", Assert.Throws<ArgumentException>(options.Validate).Message);
    }

    [Fact]
    public void Duplicate_template_ids_are_refused()
    {
        var options = new CanvasEditorOptions();
        options.CardTemplates.Add(new CardTemplate("evidence", "Copy", []));
        Assert.Contains("evidence", Assert.Throws<ArgumentException>(options.Validate).Message);
    }

    [Fact]
    public void A_select_field_needs_options()
    {
        var options = new CanvasEditorOptions();
        options.CardTemplates.Add(new CardTemplate("witness", "Witness", [new CardField("mood", "Mood", CardFieldKind.Select)]));
        Assert.Contains("mood", Assert.Throws<ArgumentException>(options.Validate).Message);
    }

    [Fact]
    public void The_default_template_is_evidence()
    {
        var template = new CanvasEditorOptions().CardTemplates[0];
        Assert.Equal("evidence", template.Id);
        Assert.Equal(["description", "date", "category", "verified"], template.Fields.Select(f => f.Key));
    }

    [Fact]
    public void Research_is_more_than_evidence_so_there_are_cards_for_the_rest_of_it()
    {
        var templates = new CanvasEditorOptions().CardTemplates;

        Assert.Equal(["evidence", "historical", "article", "experience", "quote", "person"], templates.Select(t => t.Id));
        Assert.Equal(["Evidence", "Historical note", "Article", "Experience", "Quote", "Person"],
                     templates.Select(t => t.Name));
        new CanvasEditorOptions().Validate();
    }

    [Fact]
    public void No_card_asks_how_to_reach_anybody_because_a_board_can_be_published()
    {
        var fields = new CanvasEditorOptions().CardTemplates.SelectMany(t => t.Fields).ToList();

        foreach (var word in new[] { "phone", "telephone", "email", "e-mail", "address", "contact", "mobile" })
        {
            Assert.DoesNotContain(fields, f => f.Key.Contains(word, StringComparison.OrdinalIgnoreCase)
                                            || f.Label.Contains(word, StringComparison.OrdinalIgnoreCase));
        }
    }

    [Fact]
    public void Only_evidence_asks_for_a_date_control_because_history_is_written_not_dated()
    {
        var templates = new CanvasEditorOptions().CardTemplates;

        Assert.All(templates.Where(t => t.Id != "evidence"),
            t => Assert.DoesNotContain(t.Fields, f => f.Kind == CardFieldKind.Date));
    }

    [Fact]
    public void No_endpoint_url_properties_exist()
    {
        var names = typeof(CanvasEditorOptions).GetProperties().Select(p => p.Name).ToList();
        Assert.DoesNotContain(names, n => n.Contains("UrlTemplate", StringComparison.Ordinal));
        Assert.DoesNotContain("LinkUnfurlUrl", names);
        Assert.DoesNotContain("DocumentApiBaseUrl", names);
    }

    [Fact]
    public void The_minimap_is_on_by_default() => Assert.True(new CanvasEditorOptions().ShowMinimap);

    [Fact]
    public void The_defaults_validate() => new CanvasEditorOptions().Validate();
}
