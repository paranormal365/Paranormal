using Ben.Canvas.Core.Model;
using Ben.Canvas.Core.Options;
using Ben.Canvas.Core.Paste;
using Ben.Canvas.Core.Serialization;
using Ben.Canvas.Tests.Support;

namespace Ben.Canvas.Tests.Paste;

/// <summary>Addresses are found the way a person reading the text would find them.</summary>
public sealed class UrlDetectorTests
{
    [Fact]
    public void A_trailing_full_stop_is_not_part_of_the_link()
    {
        var text = "look at https://x.example/a.";
        var span = Assert.Single(UrlDetector.FindUrls(text));
        Assert.Equal("https://x.example/a", text.Substring(span.Start, span.Length));
    }

    [Fact]
    public void A_bare_domain_is_not_a_link() => Assert.Empty(UrlDetector.FindUrls("see example.com for more"));

    [Fact]
    public void A_wikipedia_film_bracket_survives() =>
        Assert.Equal("https://en.wikipedia.org/wiki/The_Others_(film)", UrlDetector.IsSingleUrl("https://en.wikipedia.org/wiki/The_Others_(film)"));

    [Fact]
    public void A_bracket_that_closes_a_sentence_is_not_part_of_the_link()
    {
        var text = "(see https://example.com/a)";
        var span = Assert.Single(UrlDetector.FindUrls(text));
        Assert.Equal("https://example.com/a", text.Substring(span.Start, span.Length));
    }

    [Fact]
    public void Twitter_x_status_urls_are_links() =>
        Assert.NotNull(UrlDetector.IsSingleUrl("  https://x.com/ishaunted/status/1834567890123456789  "));

    [Fact]
    public void Two_urls_are_not_a_single_url() => Assert.Null(UrlDetector.IsSingleUrl("https://a.example https://b.example"));

    [Fact]
    public void A_scheme_alone_is_not_an_address() => Assert.Empty(UrlDetector.FindUrls("https:// and nothing"));

    [Fact]
    public void Linkify_keeps_every_character()
    {
        var text = "Seen at https://a.example/x, then (https://b.example).";
        var segments = UrlDetector.Linkify(text);
        Assert.Equal(text, string.Concat(segments.Select(s => s.Value)));
        Assert.Equal(2, segments.Count(s => s.IsUrl));
    }
}

/// <summary>A pasted place becomes a map box; a list of numbers does not.</summary>
public sealed class CoordinateDetectorTests
{
    [Theory]
    [InlineData("36.1627, -86.7816")]
    [InlineData("36.1627 -86.7816")]
    public void Nashville_decimal_pair_parses(string text)
    {
        Assert.True(CoordinateDetector.TryParse(text, out var lat, out var lng));
        Assert.Equal((36.1627, -86.7816), (lat, lng));
    }

    [Fact]
    public void A_numbered_list_is_not_coordinates() => Assert.False(CoordinateDetector.TryParse("12, 34", out _, out _));

    [Fact]
    public void An_apple_maps_ll_url_parses()
    {
        Assert.True(CoordinateDetector.TryParse("https://maps.apple.com/?ll=36.16,-86.78&q=Porch", out var lat, out var lng));
        Assert.Equal((36.16, -86.78), (lat, lng));
    }

    [Fact]
    public void A_google_maps_at_url_parses() =>
        Assert.True(CoordinateDetector.TryParse("https://www.google.com/maps/@36.1627,-86.7816,15z", out _, out _));

    [Fact]
    public void A_geo_uri_parses() => Assert.True(CoordinateDetector.TryParse("geo:36.16,-86.78", out _, out _));

    [Fact]
    public void Out_of_range_latitude_is_refused() => Assert.False(CoordinateDetector.TryParse("96.1, 10.5", out _, out _));

    [Fact]
    public void Parses_with_a_dot_under_de_DE() => TestBoards.InCulture("de-DE", () =>
    {
        Assert.True(CoordinateDetector.TryParse("36.5, -86.25", out var lat, out var lng));
        Assert.Equal((36.5, -86.25), (lat, lng));
    });
}

/// <summary>A picture is judged by its bytes, never its name.</summary>
public sealed class ImageSignatureTests
{
    [Fact]
    public void HEIC_bytes_are_refused()
    {
        Assert.False(ImageSignature.IsBrowserDisplayable(TestBoards.HeicHead));
        Assert.True(ImageSignature.LooksLikeHeic(TestBoards.HeicHead));
    }

    [Fact]
    public void WebP_needs_all_twelve_bytes()
    {
        byte[] webp = [.. "RIFF"u8, 0, 0, 0, 0, .. "WEBP"u8];
        Assert.Equal(".webp", ImageSignature.ExtensionFor(webp));
        Assert.Null(ImageSignature.ExtensionFor(webp[..11]));
    }

    [Fact]
    public void Png_bytes_give_png_extension() => Assert.Equal(".png", ImageSignature.ExtensionFor(TestBoards.PngHead));

    [Fact]
    public void Jpeg_bytes_give_jpg_extension() => Assert.Equal(".jpg", ImageSignature.ExtensionFor(TestBoards.JpegHead));

    [Fact]
    public void A_pdf_is_not_a_picture() => Assert.False(ImageSignature.IsBrowserDisplayable(TestBoards.PdfHead));
}

/// <summary>Only allowed markup survives in a message, however it arrived.</summary>
public sealed class PasteHtmlAllowListTests
{
    [Theory]
    [InlineData("<b>Hi</b><script>alert(1)</script>", "<b>Hi</b>")]
    [InlineData("<p onclick=\"x()\">a</p>", "<p>a</p>")]
    [InlineData("<img src=x onerror=alert(1)>after", "after")]
    [InlineData("<a href=\"javascript:alert(1)\">x</a>", "<a target=\"_blank\" rel=\"noopener noreferrer nofollow\">x</a>")]
    [InlineData("<svg><circle onload=alert(1)/><svg></svg></svg>ok", "ok")]
    [InlineData("<style>body{}</style><div class=\"row\" id=\"a\">x</div>", "<div>x</div>")]
    [InlineData("<b>unclosed", "<b>unclosed</b>")]
    [InlineData("1 < 2 & 3", "1 &lt; 2 &amp; 3")]
    [InlineData("<!-- note --><i>x</i>", "<i>x</i>")]
    [InlineData("<figure><figcaption>cap</figcaption></figure>", "cap")]
    public void Only_allowed_markup_survives(string input, string expected) => Assert.Equal(expected, PasteHtmlAllowList.Normalize(input));

    [Fact]
    public void A_safe_link_keeps_its_address_and_opens_safely() =>
        Assert.Equal("<a href=\"https://example.com/a\" target=\"_blank\" rel=\"noopener noreferrer nofollow\">x</a>",
            PasteHtmlAllowList.Normalize("<a href='https://example.com/a' target=_self>x</a>"));

    [Fact]
    public void Encoded_markup_in_text_stays_text() =>
        Assert.Equal("&lt;script&gt;", PasteHtmlAllowList.Normalize("&lt;script&gt;"));

    [Fact]
    public void Normalising_twice_changes_nothing()
    {
        var once = PasteHtmlAllowList.Normalize("<ul><li>a<li>b</ul><table><tr><td colspan=2>c</td></tr></table>");
        Assert.Equal(once, PasteHtmlAllowList.Normalize(once));
    }

    [Fact]
    public void Pictures_are_not_allowed_in_messages()
    {
        Assert.DoesNotContain("img", PasteHtmlAllowList.Tags);
        Assert.DoesNotContain("figure", PasteHtmlAllowList.Tags);
        Assert.DoesNotContain("figcaption", PasteHtmlAllowList.Tags);
    }

    [Fact]
    public void Never_tags_include_script_style_iframe_svg()
    {
        foreach (var tag in new[] { "script", "style", "iframe", "svg" })
            Assert.Contains(tag, PasteHtmlAllowList.NeverTags);
    }

    [Fact]
    public void Visible_text_reads_like_the_page() =>
        Assert.Equal("Seen at the porch", PasteHtmlAllowList.VisibleText("<p>Seen <b>at</b></p><div>the porch</div><script>x</script>"));
}

/// <summary>
/// A paste becomes what the person meant: their own copy, a picture, a link card, a map, a message or a
/// note - with a plain sentence for anything refused.
/// </summary>
public sealed class PasteClassifierTests
{
    private static readonly CanvasEditorOptions Options = new();

    private static PasteEnvelope Envelope(params PasteItem[] items) => new([.. items], 0, 0, "paste");

    private static PasteItem Plain(string text) => new("string", "text/plain", text);

    private static PasteItem Html(string html) => new("string", "text/html", html);

    private static PasteItem File(byte[] head, string name, long size = 2048, string mime = "application/octet-stream") =>
        new("file", mime, FileName: name, Size: size, AssetId: Guid.NewGuid(), Ext: Path.GetExtension(name), Head: head, Width: 1600, Height: 1200);

    [Fact]
    public void Our_own_json_wins_over_everything_else()
    {
        var payload = new CanvasClipboardPayload { Nodes = [TestBoards.Node()] };
        var plan = PasteClassifier.Classify(Envelope(Html("<b>x</b>"), Plain(CanvasSerializer.Serialize(payload, compact: true)), File(TestBoards.PngHead, "a.png")), Options);
        Assert.IsType<PasteIntent.Internal>(Assert.Single(plan.Intents));
        Assert.Single(plan.UnusedAssets);
    }

    [Fact]
    public void A_pasted_screenshot_becomes_an_image_and_its_placeholder_html_is_ignored()
    {
        var plan = PasteClassifier.Classify(Envelope(Html("<img src=\"blob:x\">"), File(TestBoards.PngHead, "image.png", mime: "image/png")), Options);
        var image = Assert.IsType<PasteIntent.Image>(Assert.Single(plan.Intents));
        Assert.Equal(".png", image.Ext);
        Assert.Empty(plan.Refusals);
    }

    [Fact]
    public void A_lone_url_is_a_link_not_text() =>
        Assert.Equal("https://x.com/ishaunted/status/1", Assert.IsType<PasteIntent.Link>(Assert.Single(
            PasteClassifier.Classify(Envelope(Plain(" https://x.com/ishaunted/status/1 ")), Options).Intents)).Url);

    [Fact]
    public void Text_with_two_urls_is_a_note() =>
        Assert.IsType<PasteIntent.Text>(Assert.Single(PasteClassifier.Classify(Envelope(Plain("https://a.example and https://b.example")), Options).Intents));

    [Fact]
    public void Coordinates_become_a_map()
    {
        var map = Assert.IsType<PasteIntent.Map>(Assert.Single(PasteClassifier.Classify(Envelope(Plain("36.1627, -86.7816")), Options).Intents));
        Assert.Equal(36.1627, map.Latitude);
    }

    [Fact]
    public void Html_with_real_content_becomes_a_message()
    {
        var plan = PasteClassifier.Classify(Envelope(Plain("Heard footsteps"), Html("<p>Heard <b>footsteps</b></p>")), Options);
        var message = Assert.IsType<PasteIntent.Html>(Assert.Single(plan.Intents));
        Assert.Equal("Heard footsteps", message.PlainText);
    }

    [Fact]
    public void Html_that_is_only_a_link_becomes_a_link() =>
        Assert.Equal("https://example.com/story", Assert.IsType<PasteIntent.Link>(Assert.Single(
            PasteClassifier.Classify(Envelope(Html("<a href=\"https://example.com/story\"><img src=\"https://example.com/t.png\"></a>")), Options).Intents)).Url);

    [Fact]
    public void Plain_text_becomes_a_note() =>
        Assert.Equal("one\n\ntwo", Assert.IsType<PasteIntent.Text>(Assert.Single(PasteClassifier.Classify(Envelope(Plain("one\n\ntwo")), Options).Intents)).Value);

    [Fact]
    public void Long_text_is_truncated_and_says_so()
    {
        var options = new CanvasEditorOptions { MaxTextChars = 10 };
        var plan = PasteClassifier.Classify(Envelope(Plain(new string('a', 25))), options);
        Assert.Equal(10, Assert.IsType<PasteIntent.Text>(Assert.Single(plan.Intents)).Value.Length);
        Assert.Equal(PasteRefusalKind.TextTruncated, Assert.Single(plan.Refusals).Kind);
    }

    [Fact]
    public void An_oversized_image_is_refused_by_name()
    {
        var plan = PasteClassifier.Classify(Envelope(File(TestBoards.JpegHead, "big.jpg", size: 30L * 1024 * 1024)), Options);
        Assert.Empty(plan.Intents);
        Assert.Contains("30 MB", Assert.Single(plan.Refusals).Message);
        Assert.Single(plan.UnusedAssets);
    }

    [Fact]
    public void A_heic_is_refused_with_the_iphone_sentence()
    {
        var plan = PasteClassifier.Classify(Envelope(File(TestBoards.HeicHead, "IMG_0001.JPG", mime: "image/jpeg")), Options);
        Assert.Empty(plan.Intents);
        Assert.Contains("Share", Assert.Single(plan.Refusals).Message);
    }

    [Fact]
    public void A_non_image_file_becomes_a_file()
    {
        var file = Assert.IsType<PasteIntent.File>(Assert.Single(PasteClassifier.Classify(Envelope(File(TestBoards.PdfHead, "notes.pdf", mime: "application/pdf")), Options).Intents));
        Assert.Equal("notes.pdf", file.FileName);
    }

    [Fact]
    public void A_file_that_could_not_be_stored_is_reported_by_name()
    {
        var item = new PasteItem("file", "application/pdf", FileName: "notes.pdf", Size: 10, Head: TestBoards.PdfHead);
        var plan = PasteClassifier.Classify(Envelope(item), Options);
        Assert.StartsWith("notes.pdf could not be kept", Assert.Single(plan.Refusals).Message);
    }

    [Fact]
    public void Too_many_items_keeps_the_first_twenty()
    {
        var items = Enumerable.Range(0, 25).Select(i => File(TestBoards.PdfHead, $"f{i}.pdf")).ToArray();
        var plan = PasteClassifier.Classify(Envelope(items), Options);
        Assert.Equal(20, plan.Intents.Count);
        Assert.Equal(PasteRefusalKind.TooManyItems, Assert.Single(plan.Refusals).Kind);
        Assert.Equal(5, plan.UnusedAssets.Count);
    }

    [Fact]
    public void An_empty_clipboard_says_nothing_to_paste()
    {
        var plan = PasteClassifier.Classify(Envelope(Plain("   ")), Options);
        Assert.Empty(plan.Intents);
        Assert.Equal(PasteRefusalKind.NothingToPaste, Assert.Single(plan.Refusals).Kind);
    }

    [Fact]
    public void Json_without_the_marker_is_ordinary_text() =>
        Assert.IsType<PasteIntent.Text>(Assert.Single(PasteClassifier.Classify(Envelope(Plain("{\"nodes\":[]}")), Options).Intents));
}

/// <summary>Placed blocks land at the paste point with fresh identities.</summary>
public sealed class PastePlacerTests
{
    private static readonly DateTime Now = new(2026, 9, 14, 18, 0, 0, DateTimeKind.Utc);

    private static PastePlan Plan(params PasteIntent[] intents) => new([.. intents], []);

    [Fact]
    public void Pasting_the_same_payload_twice_gives_new_ids()
    {
        var source = TestBoards.Node();
        var payload = new CanvasClipboardPayload { Nodes = [source] };
        var first = PastePlacer.Place(Plan(new PasteIntent.Internal(payload)), 0, 0, Now, h => h).Nodes.Single();
        var second = PastePlacer.Place(Plan(new PasteIntent.Internal(payload)), 0, 0, Now, h => h).Nodes.Single();
        Assert.NotEqual(source.Id, first.Id);
        Assert.NotEqual(first.Id, second.Id);
    }

    [Fact]
    public void Internal_edges_follow_their_nodes()
    {
        var a = TestBoards.Node();
        var b = TestBoards.Node(x: 400);
        var payload = new CanvasClipboardPayload { Nodes = [a, b], Edges = [new CanvasEdge { FromNodeId = a.Id, ToNodeId = b.Id }] };
        var (nodes, edges) = PastePlacer.Place(Plan(new PasteIntent.Internal(payload)), 0, 0, Now, h => h);
        var edge = Assert.Single(edges);
        Assert.Equal(nodes[0].Id, edge.FromNodeId);
        Assert.Equal(nodes[1].Id, edge.ToNodeId);
    }

    [Fact]
    public void An_edge_to_a_node_not_copied_is_dropped()
    {
        var a = TestBoards.Node();
        var payload = new CanvasClipboardPayload { Nodes = [a], Edges = [new CanvasEdge { FromNodeId = a.Id, ToNodeId = Guid.NewGuid() }] };
        Assert.Empty(PastePlacer.Place(Plan(new PasteIntent.Internal(payload)), 0, 0, Now, h => h).Edges);
    }

    [Fact]
    public void The_payload_top_left_lands_on_the_paste_point()
    {
        var a = TestBoards.Node(x: 100, y: 300);
        var b = TestBoards.Node(x: 500, y: 200);
        var payload = new CanvasClipboardPayload { Nodes = [a, b] };
        var nodes = PastePlacer.Place(Plan(new PasteIntent.Internal(payload)), 1000, 1000, Now, h => h).Nodes;
        Assert.Equal(1000, nodes.Min(n => n.X));
        Assert.Equal(1000, nodes.Min(n => n.Y));
        Assert.Equal(400, nodes[1].X - nodes[0].X);
    }

    [Fact]
    public void Three_intents_cascade_by_24px()
    {
        var nodes = PastePlacer.Place(Plan(new PasteIntent.Text("a"), new PasteIntent.Text("b"), new PasteIntent.Link("https://example.com")), 10, 20, Now, h => h).Nodes;
        Assert.Equal([10d, 34d, 58d], nodes.Select(n => n.X));
        Assert.Equal([20d, 44d, 68d], nodes.Select(n => n.Y));
    }

    [Fact]
    public void A_tall_image_keeps_its_aspect_inside_the_default_box()
    {
        var node = PastePlacer.Place(Plan(new PasteIntent.Image(Guid.NewGuid(), ".jpg", 1000, 2000)), 0, 0, Now, h => h).Nodes.Single();
        Assert.Equal(0.5, node.Width / node.Height, 6);
        Assert.True(node.Width <= 320 && node.Height <= 240);
    }

    [Fact]
    public void Html_is_passed_through_the_sanitiser()
    {
        var node = PastePlacer.Place(Plan(new PasteIntent.Html("<b onclick=x>hi</b>", "hi")), 0, 0, Now, _ => "<b>clean</b>").Nodes.Single();
        Assert.Equal("<b>clean</b>", ((MessageData)node.Data).Html);
        Assert.Equal(Now, ((MessageData)node.Data).TimestampUtc);
    }

    [Fact]
    public void Pasted_nodes_leave_their_old_group()
    {
        var a = TestBoards.Node();
        a.GroupId = Guid.NewGuid();
        var node = PastePlacer.Place(Plan(new PasteIntent.Internal(new CanvasClipboardPayload { Nodes = [a] })), 0, 0, Now, h => h).Nodes.Single();
        Assert.Null(node.GroupId);
    }

    [Fact]
    public void Our_copy_round_trips_through_text()
    {
        var payload = new CanvasClipboardPayload { Nodes = [TestBoards.Node(CanvasNodeType.Map)] };
        var json = CanvasSerializer.Serialize(payload, compact: true);
        Assert.StartsWith("{\"$ishcanvas\":1", json);
        Assert.True(CanvasClipboardPayload.TryReadPayload(json, out var read));
        Assert.IsType<MapData>(Assert.Single(read!.Nodes).Data);
    }
}
