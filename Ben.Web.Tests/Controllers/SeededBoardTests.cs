using Ben.Canvas.Core.Model;
using Ben.Canvas.Core.Presentation;
using Ben.Canvas.Core.Serialization;
using Ben.Data.WebApi.SeedData;
using Xunit;

namespace Ben.Web.Tests.Controllers;

/// <summary>
/// The research board the development seeder writes is one the canvas can actually open.
/// </summary>
/// <remarks>
/// <para>
/// The seeder builds the document by hand: it lives in Ben.Data.WebApi, which does not reference
/// Ben.Canvas.Core, so nothing over there can check its own work. Hand-built JSON goes stale in
/// silence — the board would simply refuse to open, on a fresh install, in front of whoever was
/// being shown the product.
/// </para>
/// <para>
/// So it is read here with the editor's real reader. If the format moves and the seed does not, this
/// fails in the same commit rather than on somebody's first run.
/// </para>
/// </remarks>
public sealed class SeededBoardTests
{
    private static readonly Guid Case = new("11111111-2222-3333-4444-555555555555");

    private static CanvasDocument Seeded()
    {
        var json = DevelopmentDataSeeder.ResearchBoardJson(Case, new DateTime(2026, 9, 16, 12, 0, 0, DateTimeKind.Utc));
        var (document, problem) = CanvasSerializer.Parse(json);
        Assert.True(problem is null, $"the seeded board does not open: {problem}");
        return document!;
    }

    [Fact]
    public void It_reads_back_with_the_editors_own_reader()
    {
        var document = Seeded();

        Assert.Equal(CanvasDocument.CurrentSchemaVersion, document.SchemaVersion);
        Assert.Equal("Previous owners and where they are buried", document.Title);
        Assert.Equal(Case, document.CaseId);
    }

    /// <summary>
    /// Four cards joined in a chain, and a note beside them.
    /// </summary>
    /// <remarks>
    /// The shape the help pictures and the product walk are written against — and a chain on purpose,
    /// because that is what the side handles make and what presenting walks in order.
    /// </remarks>
    [Fact]
    public void It_is_a_chain_of_four_cards_with_a_note()
    {
        var document = Seeded();

        Assert.Equal(4, document.Nodes.Count(n => n.Type == CanvasNodeType.Card));
        Assert.Single(document.Nodes.Where(n => n.Type == CanvasNodeType.Text));
        Assert.Equal(3, document.Edges.Count);
        Assert.All(document.Edges, e => Assert.Equal(EdgeArrow.End, e.Arrow));
    }

    [Fact]
    public void Every_card_carries_the_words_somebody_wrote()
    {
        foreach (var card in Seeded().Nodes.Select(n => n.Data).OfType<CardData>())
        {
            Assert.False(string.IsNullOrWhiteSpace(card.Title), "a seeded card has no title, so the board reads as empty boxes");
            Assert.True(card.Fields.TryGetValue("description", out var body) && !string.IsNullOrWhiteSpace(body),
                $"the seeded card \"{card.Title}\" has no description");
        }
    }

    /// <summary>Every block is a stop, and the chain is walked in the order its arrows were drawn.</summary>
    [Fact]
    public void It_presents_in_the_order_the_arrows_were_drawn()
    {
        var document = Seeded();

        // Named the way the editor names them, which is how the counter reads during a walk.
        var slides = SlideOrder.For(document, n => n.Data is CardData card ? card.Title : "note");

        Assert.Equal(document.Nodes.Count, slides.Count);
        var titles = slides.Select(s => s.Title).ToList();
        Assert.Equal("Built 1924", titles[0]);
        Assert.Equal("Sold 1951", titles[1]);
        Assert.Equal("Obituary, March 1982", titles[2]);
        Assert.Equal("Mount Olivet", titles[3]);
    }
}
