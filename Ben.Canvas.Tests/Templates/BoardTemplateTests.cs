using Ben.Canvas.Core.Blocks;
using Ben.Canvas.Core.Model;
using Ben.Canvas.Core.Options;
using Ben.Canvas.Core.Presentation;
using Ben.Canvas.Core.Serialization;
using Ben.Canvas.Core.Templates;

namespace Ben.Canvas.Tests.Templates;

/// <summary>
/// The boards somebody can start from, rather than from nothing.
/// </summary>
/// <remarks>
/// <para><b>Where these came from.</b> Ben sent four boards as pictures on 2026-09-18 — a moodboard of
/// coloured sections with a theme cluster, a research plan of titled tiles holding a quadrant and a
/// calendar grid, a family tree of boxes joined at right angles with a legend, and a presentation deck
/// of slide frames. Each was shown twice: a blank TEMPLATE beside a filled SAMPLE. M9-01 to M9-07 built
/// the pieces; this is the assembly.</para>
///
/// <para><b>What every template must satisfy</b> is asserted for all of them at once, below, because a
/// template that opens a board the editor cannot use is worse than no template at all: it teaches
/// somebody the tool is broken on their first try, which is the one try that counts.</para>
/// </remarks>
public sealed class BoardTemplateTests
{
    public static TheoryData<string> AllIds() => [.. BoardTemplates.All.Select(t => t.Id)];

    private static CanvasDocument Build(string id) =>
        BoardTemplates.Create(id, caseId: Guid.NewGuid());

    // ── what every template owes ──────────────────────────────────────────

    [Theory]
    [MemberData(nameof(AllIds))]
    public void Every_template_opens_with_the_editors_own_reader(string id)
    {
        var (read, problem) = CanvasSerializer.Parse(CanvasSerializer.Serialize(Build(id)));

        Assert.True(problem is null, $"{id} does not open: {problem}");
        Assert.NotNull(read);
        Assert.Equal(Build(id).Nodes.Count, read!.Nodes.Count);
    }

    /// <summary>A template that overflows the board's own limit could not be added to.</summary>
    [Theory]
    [MemberData(nameof(AllIds))]
    public void Every_template_fits_MaxNodes(string id)
    {
        var most = new CanvasEditorOptions().MaxNodes;

        Assert.True(Build(id).Nodes.Count <= most, $"{id} has {Build(id).Nodes.Count} blocks; the board holds {most}");
    }

    /// <summary>
    /// A template names palette tokens, never colours. A literal would be right in one theme and wrong
    /// in the other, and the whole palette exists so that cannot happen.
    /// </summary>
    [Theory]
    [MemberData(nameof(AllIds))]
    public void Templates_use_palette_tokens_only(string id)
    {
        var document = Build(id);

        Assert.All(document.Nodes.Select(n => n.ColorKey)
                .Concat(document.Groups.Select(g => g.ColorKey))
                .Concat(document.Edges.Select(e => e.ColorKey)),
            key => Assert.True(key is null || CanvasPalette.IsValid(key),
                $"{id} names {key}, which is not a palette token"));
    }

    /// <summary>Every block is a kind this build has a descriptor for, and so a renderer.</summary>
    [Theory]
    [MemberData(nameof(AllIds))]
    public void Every_block_in_a_template_is_a_kind_this_build_knows(string id)
    {
        Assert.All(Build(id).Nodes, n => Assert.Equal(n.Type, BlockRegistry.Get(n.Type).Type));
    }

    /// <summary>
    /// Nothing is placed at a negative coordinate or off in the far distance: a template has to land
    /// where Fit will find it, or it opens looking like an empty board.
    /// </summary>
    [Theory]
    [MemberData(nameof(AllIds))]
    public void Every_template_sits_in_reachable_space(string id)
    {
        Assert.All(Build(id).Nodes, n =>
        {
            Assert.InRange(n.X, 0, 20_000);
            Assert.InRange(n.Y, 0, 20_000);
        });

        Assert.All(Build(id).Groups, g =>
        {
            Assert.InRange(g.X, 0, 20_000);
            Assert.InRange(g.Y, 0, 20_000);
        });
    }

    /// <summary>
    /// No block or panel is smaller than its own kind allows, or the first drag on it jumps.
    /// </summary>
    [Theory]
    [MemberData(nameof(AllIds))]
    public void Every_block_is_at_least_its_own_minimum(string id)
    {
        var document = Build(id);

        Assert.All(document.Nodes, n =>
        {
            var descriptor = BlockRegistry.Get(n.Type);
            Assert.True(n.Width >= descriptor.MinWidth, $"{id}: a {n.Type} is {n.Width} wide, under {descriptor.MinWidth}");
            Assert.True(n.Height >= descriptor.MinHeight, $"{id}: a {n.Type} is {n.Height} tall, under {descriptor.MinHeight}");
        });

        Assert.All(document.Groups, g =>
        {
            Assert.True(g.Width >= BlockRegistry.GroupMinWidth, $"{id}: {g.Label} is {g.Width} wide, under {BlockRegistry.GroupMinWidth}");
            Assert.True(g.Height >= BlockRegistry.GroupMinHeight, $"{id}: {g.Label} is {g.Height} tall, under {BlockRegistry.GroupMinHeight}");
        });
    }

    /// <summary>Every connector joins two blocks that are actually on the board.</summary>
    [Theory]
    [MemberData(nameof(AllIds))]
    public void Every_connector_joins_blocks_that_exist(string id)
    {
        var document = Build(id);
        var ids = document.Nodes.Select(n => n.Id).ToHashSet();

        Assert.All(document.Edges, e =>
        {
            Assert.Contains(e.FromNodeId, ids);
            Assert.Contains(e.ToNodeId, ids);
            Assert.NotEqual(e.FromNodeId, e.ToNodeId);
        });
    }

    /// <summary>A block claiming a group is in one that exists, and sits inside it.</summary>
    [Theory]
    [MemberData(nameof(AllIds))]
    public void Every_grouped_block_sits_in_a_group_that_exists(string id)
    {
        var document = Build(id);
        var groups = document.Groups.ToDictionary(g => g.Id);

        Assert.All(document.Nodes.Where(n => n.GroupId is not null), n =>
        {
            Assert.Contains(n.GroupId!.Value, groups.Keys);

            var g = groups[n.GroupId.Value];
            Assert.True(n.X >= g.X && n.Y >= g.Y
                     && n.X + n.Width <= g.X + g.Width
                     && n.Y + n.Height <= g.Y + g.Height,
                $"{id}: a block claims {g.Label} but hangs outside it");
        });
    }

    /// <summary>
    /// The paint counter is ahead of everything placed, or the first block somebody adds lands behind
    /// the template.
    /// </summary>
    [Theory]
    [MemberData(nameof(AllIds))]
    public void The_paint_counter_is_ahead_of_the_template(string id)
    {
        var document = Build(id);
        var highest = document.Nodes.Select(n => n.Z).Concat(document.Groups.Select(g => g.Z)).DefaultIfEmpty(-1).Max();

        Assert.True(document.NextZ > highest, $"{id} would put the next block behind itself");
    }

    /// <summary>Every block and panel has its own paint depth; two at one depth draw in load order.</summary>
    [Theory]
    [MemberData(nameof(AllIds))]
    public void Nothing_shares_a_paint_depth(string id)
    {
        var document = Build(id);
        var depths = document.Nodes.Select(n => n.Z).Concat(document.Groups.Select(g => g.Z)).ToList();

        Assert.Equal(depths.Count, depths.Distinct().Count());
    }

    [Theory]
    [MemberData(nameof(AllIds))]
    public void Every_template_keeps_the_case_it_was_made_for(string id)
    {
        var caseId = Guid.NewGuid();

        Assert.Equal(caseId, BoardTemplates.Create(id, caseId).CaseId);
    }

    [Theory]
    [MemberData(nameof(AllIds))]
    public void Every_template_is_titled(string id)
    {
        Assert.False(string.IsNullOrWhiteSpace(Build(id).Title), $"{id} has no title");
    }

    /// <summary>
    /// Nothing carries a board link. A template ships to every case, and a card pointing at a board id
    /// from some other case would be a dead link on arrival — and would refuse to publish.
    /// </summary>
    [Theory]
    [MemberData(nameof(AllIds))]
    public void No_template_carries_a_board_link(string id)
    {
        Assert.DoesNotContain(Build(id).Nodes, n => n.Type == CanvasNodeType.Board || n.Data is BoardData);
    }

    /// <summary>
    /// A template is a pure function: the same board every time, apart from the ids, so two people
    /// starting from one template start from the same thing.
    /// </summary>
    [Theory]
    [MemberData(nameof(AllIds))]
    public void Every_template_builds_the_same_board_twice(string id)
    {
        var first = Build(id);
        var second = Build(id);

        Assert.Equal(first.Nodes.Count, second.Nodes.Count);
        Assert.Equal(first.Groups.Count, second.Groups.Count);
        Assert.Equal(first.Edges.Count, second.Edges.Count);
        Assert.Equal(
            first.Nodes.Select(n => (n.Type, n.X, n.Y, n.Width, n.Height)),
            second.Nodes.Select(n => (n.Type, n.X, n.Y, n.Width, n.Height)));
    }

    // ── the catalogue ─────────────────────────────────────────────────────

    [Fact]
    public void The_five_boards_Ben_asked_for_are_on_offer()
    {
        Assert.Equal(
            ["blank", "moodboard", "research-plan", "family-tree", "deck"],
            BoardTemplates.All.Select(t => t.Id));
    }

    [Fact]
    public void Every_template_says_what_it_is_and_who_it_is_for()
    {
        Assert.All(BoardTemplates.All, t =>
        {
            Assert.False(string.IsNullOrWhiteSpace(t.Name), $"{t.Id} has no name");
            Assert.False(string.IsNullOrWhiteSpace(t.Summary), $"{t.Id} has no summary");
            Assert.False(string.IsNullOrWhiteSpace(t.IconName), $"{t.Id} has no icon");
        });
    }

    /// <summary>
    /// An id this build does not know opens a blank board rather than throwing. The id travels in the
    /// address fragment, where anything can arrive, and a stale link must not be a broken editor.
    /// </summary>
    [Theory]
    [InlineData("not-a-template")]
    [InlineData("")]
    [InlineData(null)]
    public void An_unknown_template_opens_a_blank_board(string? id)
    {
        var document = BoardTemplates.Create(id, caseId: null);

        Assert.Empty(document.Nodes);
        Assert.Empty(document.Groups);
        Assert.Equal("Untitled board", document.Title);
    }

    [Fact]
    public void Blank_is_blank_and_keeps_the_usual_title()
    {
        var document = Build("blank");

        Assert.Empty(document.Nodes);
        Assert.Empty(document.Edges);
        Assert.Empty(document.Groups);
        Assert.Equal("Untitled board", document.Title);
    }

    [Fact]
    public void A_named_template_titles_the_board_after_itself()
    {
        Assert.Equal("Family tree", Build("family-tree").Title);
    }

    [Fact]
    public void An_id_is_matched_whatever_its_case()
    {
        Assert.NotEmpty(BoardTemplates.Create("Family-Tree", null).Nodes);
    }

    // ── what each one is ──────────────────────────────────────────────────

    /// <summary>
    /// The moodboard is coloured sections with a cluster of themes — the picture Ben sent, blank.
    /// </summary>
    [Fact]
    public void The_moodboard_is_titled_panels_and_a_cluster_of_themes()
    {
        var document = Build("moodboard");

        Assert.Equal(4, document.Groups.Count);
        Assert.All(document.Groups, g => Assert.Equal(GroupFill.Panel, g.Fill));
        Assert.All(document.Groups, g => Assert.False(string.IsNullOrWhiteSpace(g.Label)));

        // Each section a different hue, as in the picture.
        Assert.Equal(4, document.Groups.Select(g => g.ColorKey).Distinct().Count());

        var bubbles = document.Nodes.Where(n => n.Type == CanvasNodeType.Shape).ToList();
        Assert.Equal(4, bubbles.Count);
        Assert.All(bubbles, s => Assert.Equal(ShapeKind.Ellipse, ((ShapeData)s.Data).Kind));

        // A centre joined to every satellite: a theme with nothing attached is just a circle.
        Assert.Equal(3, document.Edges.Count);
        Assert.Single(document.Edges.Select(e => e.FromNodeId).Distinct());
    }

    /// <summary>The research plan carries the quadrant and the grid the picture shows.</summary>
    [Fact]
    public void The_research_plan_has_a_quadrant_and_a_grid()
    {
        var document = Build("research-plan");

        Assert.True(document.Groups.Count >= 4, "the quadrant alone is four panels");
        Assert.Equal(4, document.Groups.Count(g => g.Fill == GroupFill.Outline));

        var table = Assert.Single(document.Nodes, n => n.Type == CanvasNodeType.Table);
        var grid = (TableData)table.Data;

        Assert.True(grid.HasHeaderRow);
        Assert.True(grid.Rows.Count > 1, "a grid with only a header has nothing to fill in");
        Assert.All(grid.Rows, r => Assert.Equal(grid.ColumnCount, r.Count));
    }

    /// <summary>The family tree is right angles, and says what its line styles mean.</summary>
    /// <remarks>
    /// The legend is not decoration: the picture Ben sent has one, because dashes-mean-siblings is not
    /// guessable, and a tree without it is a diagram only its author can read.
    /// </remarks>
    [Fact]
    public void The_family_tree_runs_in_right_angles_and_carries_a_legend()
    {
        var document = Build("family-tree");

        // One marriage, three parent-to-child, two sibling links and one grandchild. Hard-coded on
        // purpose: the e2e counts the drawn lines, and the two numbers must move together.
        Assert.Equal(7, document.Edges.Count);
        Assert.All(document.Edges, e => Assert.Equal(EdgeRoute.Elbow, e.Route));

        Assert.Contains(document.Edges, e => e.Line == EdgeLine.Dashed);
        Assert.Contains(document.Edges, e => e.EffectiveFromMarker == EdgeMarker.Diamond
                                          && e.EffectiveToMarker == EdgeMarker.Diamond);

        var legend = Assert.Single(document.Groups);
        Assert.Contains("Legend", legend.Label, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(document.Nodes, n => n.GroupId == legend.Id
            && n.Data is TextData t && t.Text.Contains("sibling", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Every person has an empty picture frame above their name.
    /// </summary>
    /// <remarks>
    /// <b>Ben, 2026-09-18:</b> "include a place to put a photo of the person - if they want to do that or
    /// even just using a male and female icon. It should be up to the end user." So the frame ships
    /// EMPTY, and empty is a finished state: the block draws "No picture yet. Paste or drop one here."
    /// A template that pre-filled anything — a stock silhouette, a gendered icon — would be making the
    /// choice Ben asked to leave open, so this asserts nothing is pre-filled as firmly as it asserts the
    /// frame is there.
    /// </remarks>
    [Fact]
    public void Every_person_in_the_family_tree_has_a_picture_frame_that_starts_empty()
    {
        var document = Build("family-tree");

        var frames = document.Nodes.Where(n => n.Type == CanvasNodeType.Image).ToList();
        var names = document.Nodes.Where(n => n.Type == CanvasNodeType.Text && n.GroupId is null).ToList();

        Assert.Equal(names.Count, frames.Count);
        Assert.Equal(6, frames.Count);

        Assert.All(frames, f =>
        {
            var picture = (ImageData)f.Data;

            Assert.Null(picture.AssetId);
            Assert.Null(picture.UploadFileId);
            Assert.True(string.IsNullOrEmpty(picture.Caption), "a template must not caption a photo nobody has chosen");

            // Cover, so whatever is dropped in fills the frame at the same size as everybody else's
            // rather than each portrait being as tall as its own file.
            Assert.Equal(ImageFit.Cover, picture.Fit);
        });

        // Small, and no smaller than the block allows. Ben's call of three, 2026-09-18: a frame is a
        // real block whether a photograph lands in it or not, so an unused one is the smallest tile
        // the editor permits rather than a box the width of the name.
        var smallest = BlockRegistry.Get(CanvasNodeType.Image);
        Assert.All(frames, f =>
        {
            Assert.Equal(smallest.MinWidth, f.Width);
            Assert.Equal(smallest.MinHeight, f.Height);
        });

        // Each frame is centred directly above the name it belongs to, and the lines join the NAMES:
        // a line into a photo would move the moment somebody decided to go without one.
        Assert.All(frames, f => Assert.Contains(names, n =>
            Math.Abs(n.X + n.Width / 2 - (f.X + f.Width / 2)) < 0.001 && n.Y == f.Y + f.Height));

        var joined = document.Edges.SelectMany(e => new[] { e.FromNodeId, e.ToNodeId }).ToHashSet();
        Assert.DoesNotContain(frames, f => joined.Contains(f.Id));
    }

    /// <summary>The legend says the photo is optional, because otherwise an empty frame looks unfinished.</summary>
    [Fact]
    public void The_family_trees_legend_says_the_photo_is_the_users_choice()
    {
        var legend = Build("family-tree").Nodes
            .Select(n => n.Data).OfType<TextData>()
            .Single(t => t.Text.Contains("sibling", StringComparison.OrdinalIgnoreCase));

        Assert.Contains("photo", legend.Text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Leave it empty", legend.Text, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// The deck is panels, and it presents with nothing else built — which is the whole reason a slide
    /// is a group rather than a new kind of block.
    /// </summary>
    [Fact]
    public void The_deck_template_is_panels_in_reading_order()
    {
        var document = Build("deck");

        Assert.True(document.Groups.Count >= 4, "a deck is several slides");
        Assert.All(document.Groups, g => Assert.Equal(GroupFill.Panel, g.Fill));
        Assert.All(document.Groups, g => Assert.Contains(document.Nodes, n => n.GroupId == g.Id));
    }

    /// <summary>
    /// And the walk comes out in the order the template writes the slides.
    /// </summary>
    /// <remarks>
    /// <c>SlideOrder</c> sorts a board's groups down the page and then across, so this holds only if the
    /// deck is laid out in reading order. Asserting the TITLES rather than the coordinates is what makes
    /// it discriminate: a slide 3 placed above slide 2 sorts into second place and the coordinates would
    /// still be ascending.
    /// </remarks>
    [Fact]
    public void Slide_frames_in_a_deck_template_present_top_to_bottom()
    {
        var document = Build("deck");

        var walked = SlideOrder.For(document).ToList();

        Assert.Equal(document.Groups.Count, walked.Count);
        Assert.All(walked, s => Assert.True(s.IsGroup, "a deck's slides are its frames"));
        Assert.Equal(document.Groups.Select(g => g.Label), walked.Select(s => s.Title));
    }
}
