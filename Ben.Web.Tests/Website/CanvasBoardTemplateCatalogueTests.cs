using Ben.Canvas.Core.Templates;
using Ben.Web.Website.Library.Manage.Messenger;
using Xunit;

namespace Ben.Web.Tests.Website;

/// <summary>
/// The site's list of board templates is the canvas's list.
/// </summary>
/// <remarks>
/// <para>The Research tab writes its own list of five ids because this project deliberately does not
/// reference <c>Ben.Canvas.Core</c> — the same rule the API's <c>CanvasBoardLinks</c> follows: the
/// canvas libraries are WebAssembly-facing, and the contract between the site and the editor is the
/// URL.</para>
///
/// <para>So the drift has to be caught somewhere, and <c>Ben.Web.Tests</c> is the one project that
/// references both. Without this, adding a template to Core would leave it unofferable, renaming an id
/// would leave a button that opens a blank board, and dropping one would leave a button that opens a
/// blank board <i>and</i> shows a warning — three failures nobody would see until Ben clicked it.</para>
/// </remarks>
public sealed class CanvasBoardTemplateCatalogueTests
{
    [Fact]
    public void The_sites_template_ids_are_the_canvass_own_in_the_same_order()
    {
        Assert.Equal(
            BoardTemplates.All.Select(t => t.Id),
            CanvasBoardTemplates.All.Select(t => t.Id));
    }

    /// <summary>Blank is first, so the behaviour before templates existed is still one click away.</summary>
    [Fact]
    public void Blank_is_the_first_thing_offered()
    {
        Assert.Equal(BoardTemplates.BlankId, CanvasBoardTemplates.All[0].Id);
    }

    [Fact]
    public void Every_offer_has_a_name_a_line_and_an_icon()
    {
        Assert.All(CanvasBoardTemplates.All, t =>
        {
            Assert.False(string.IsNullOrWhiteSpace(t.Name), $"{t.Id} has no name");
            Assert.False(string.IsNullOrWhiteSpace(t.Summary), $"{t.Id} has no summary");
            Assert.False(string.IsNullOrWhiteSpace(t.IconName), $"{t.Id} has no icon");
        });
    }

    /// <summary>
    /// An id is a URL fragment value, so it stays to the characters that need no escaping — otherwise
    /// a rename could quietly make a link that the parse on the other side reads differently.
    /// </summary>
    [Fact]
    public void Every_id_is_safe_in_a_fragment()
    {
        Assert.All(CanvasBoardTemplates.All, t =>
        {
            Assert.Equal(t.Id, Uri.EscapeDataString(t.Id));
            Assert.Matches("^[a-z0-9-]+$", t.Id);
        });
    }

    /// <summary>
    /// The family tree's line says the photo frame is there.
    /// </summary>
    /// <remarks>
    /// Ben asked for it (2026-09-18: "include a place to put a photo of the person… It should be up to
    /// the end user"), and it is the one thing about that template nobody would guess from its name. A
    /// picker that does not mention it makes the feature invisible until somebody opens the board.
    /// </remarks>
    [Fact]
    public void The_family_tree_offer_mentions_the_photo_frame()
    {
        var tree = Assert.Single(CanvasBoardTemplates.All, t => t.Id == "family-tree");

        Assert.Contains("photo", tree.Summary, StringComparison.OrdinalIgnoreCase);
    }
}
