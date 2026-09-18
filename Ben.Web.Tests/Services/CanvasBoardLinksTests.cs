using Ben.Data.WebApi.Services.Canvas;
using Xunit;

namespace Ben.Web.Tests.Services;

/// <summary>
/// Reading the boards a board's cards point at, on the server, straight from the JSON.
/// </summary>
/// <remarks>
/// <para><b>Why the server reads this at all.</b> Ben, 2026-09-18: "the only way for it to hit the
/// load link to other page is if the other page has been published … it will cause issues if one is
/// not published and one that is published has a link to a page which is not published." A picker
/// that only offers published boards stops the state being CREATED; it does nothing about a target
/// deleted between picking and publishing, or a crafted request. The consequence of getting it wrong
/// is a reader handed a door into somebody's private draft.</para>
///
/// <para>The API deliberately does not reference <c>Ben.Canvas.Core</c> — it is a WebAssembly-facing
/// library — so this walks <c>nodes[].data.kind</c> the same way the document sanitizer already does,
/// and these tests are what keep the two readings honest about the same file format.</para>
/// </remarks>
public sealed class CanvasBoardLinksTests
{
    private static string Board(string kind, string? documentId) =>
        $$"""
          {
            "schemaVersion": 1,
            "nodes": [
              { "id": "11111111-1111-1111-1111-111111111111", "type": "Board",
                "data": { "kind": "{{kind}}"{{(documentId is null ? "" : $", \"documentId\": \"{documentId}\"")}} } }
            ]
          }
          """;

    [Fact]
    public void A_board_card_names_its_target()
    {
        var target = Guid.NewGuid();

        Assert.Equal([target], CanvasBoardLinks.TargetsIn(Board("board", target.ToString())));
    }

    [Fact]
    public void A_card_of_another_kind_names_nothing()
    {
        Assert.Empty(CanvasBoardLinks.TargetsIn(Board("card", Guid.NewGuid().ToString())));
    }

    [Fact]
    public void A_board_card_with_no_target_yet_names_nothing()
    {
        Assert.Empty(CanvasBoardLinks.TargetsIn(Board("board", null)));
    }

    [Fact]
    public void An_empty_target_is_not_a_target()
    {
        Assert.Empty(CanvasBoardLinks.TargetsIn(Board("board", Guid.Empty.ToString())));
    }

    /// <summary>
    /// The same board linked twice is one target. The refusal names boards, and naming one twice
    /// reads as a bug in the message.
    /// </summary>
    [Fact]
    public void The_same_board_linked_twice_is_one_target()
    {
        var target = Guid.NewGuid();
        var json = $$"""
                     { "nodes": [
                       { "type": "Board", "data": { "kind": "board", "documentId": "{{target}}" } },
                       { "type": "Board", "data": { "kind": "board", "documentId": "{{target}}" } }
                     ] }
                     """;

        Assert.Single(CanvasBoardLinks.TargetsIn(json));
    }

    /// <summary>
    /// Property case is tolerated, the same allowance the sanitizer makes — a re-serialized or
    /// hand-written board can carry either.
    /// </summary>
    [Fact]
    public void Property_case_does_not_hide_a_link()
    {
        var target = Guid.NewGuid();
        var json = $$"""{ "Nodes": [ { "Data": { "Kind": "Board", "DocumentId": "{{target}}" } } ] }""";

        Assert.Equal([target], CanvasBoardLinks.TargetsIn(json));
    }

    /// <summary>
    /// An unreadable board has no links worth enforcing, and must not throw out of the publish path:
    /// the save endpoint refuses malformed JSON on its own, with its own sentence.
    /// </summary>
    [Theory]
    [InlineData("")]
    [InlineData(null)]
    [InlineData("not json at all")]
    [InlineData("[]")]
    [InlineData("{}")]
    [InlineData("{ \"nodes\": \"not an array\" }")]
    [InlineData("{ \"nodes\": [ null, 3, \"x\" ] }")]
    [InlineData("{ \"nodes\": [ { \"data\": null } ] }")]
    [InlineData("{ \"nodes\": [ { \"data\": { \"kind\": \"board\", \"documentId\": \"nonsense\" } } ] }")]
    public void Anything_unreadable_names_nothing_and_does_not_throw(string? json)
    {
        Assert.Empty(CanvasBoardLinks.TargetsIn(json));
    }
}
