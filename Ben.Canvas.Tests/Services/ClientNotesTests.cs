using System.Text.Json;
using Ben.Canvas.Core.Model;
using Ben.Canvas.Editor.Services;

namespace Ben.Canvas.Tests.Services;

/// <summary>
/// Reading the case's message thread for the client's side of it, and putting one on a board.
/// </summary>
public sealed class ClientNotesTests
{
    private static string Thread(params string[] messages) => "[" + string.Join(",", messages) + "]";

    private static string Message(int side, string body, string? html = null, string author = "Susan Holt",
                                  string when = "2026-09-14T15:04:05Z") =>
        JsonSerializer.Serialize(new
        {
            id = Guid.NewGuid(),
            caseId = Guid.NewGuid(),
            authorAppUserId = Guid.NewGuid(),
            authorDisplayName = author,
            body,
            bodyHtml = html,
            senderSide = side,
            isReadByClient = true,
            isReadByOrg = true,
            dateCreated = when,
        });

    [Fact]
    public void Only_what_the_client_wrote_is_offered()
    {
        var notes = ClientNotes.Read(Thread(
            Message(0, "There are three knocks on the cellar door most nights."),
            Message(1, "Thank you, we will come on Friday.", author: "Paranormal365")));

        var note = Assert.Single(notes);
        Assert.Equal("Susan Holt", note.Author);
        Assert.Contains("three knocks", note.PlainText);
    }

    [Fact]
    public void A_side_sent_as_a_word_is_understood_too()
    {
        var thread = Thread(Message(0, "the cellar door").Replace("\"senderSide\":0", "\"senderSide\":\"Client\""));

        Assert.Single(ClientNotes.Read(thread));
    }

    /// <summary>
    /// A side nobody recognises is left out rather than guessed at: a group reply mistaken for the
    /// client's words would be quoted on a board as the client's.
    /// </summary>
    [Fact]
    public void A_side_nobody_recognises_is_left_out()
    {
        var thread = Thread(Message(0, "the cellar door").Replace("\"senderSide\":0", "\"senderSide\":\"Somebody\""));

        Assert.Empty(ClientNotes.Read(thread));
    }

    [Fact]
    public void Plain_text_becomes_the_paragraphs_it_was_typed_as()
    {
        var notes = ClientNotes.Read(Thread(Message(0, "It started in March.\n\nIt is worse upstairs.\nAlways after midnight.")));

        var html = Assert.Single(notes).Html;
        Assert.Equal(2, html.Split("<p>").Length - 1);
        Assert.Contains("<br>", html);
    }

    [Fact]
    public void Formatted_words_keep_their_formatting_and_lose_anything_dangerous()
    {
        var notes = ClientNotes.Read(Thread(Message(0, "cold in the back bedroom",
            html: "<p><strong>Cold</strong> in the back bedroom.</p><script>alert(1)</script>")));

        var html = Assert.Single(notes).Html;
        Assert.Contains("<strong>", html);
        Assert.DoesNotContain("script", html, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Newest_first_and_empty_messages_are_not_offered()
    {
        var notes = ClientNotes.Read(Thread(
            Message(0, "older", when: "2026-09-01T10:00:00Z"),
            Message(0, "   ", when: "2026-09-05T10:00:00Z"),
            Message(0, "newer", when: "2026-09-10T10:00:00Z")));

        Assert.Equal(["newer", "older"], notes.Select(n => n.PlainText.Trim()));
    }

    [Fact]
    public void A_thread_that_is_not_json_offers_nothing_rather_than_throwing()
    {
        Assert.Empty(ClientNotes.Read("<html>signed out</html>"));
        Assert.Empty(ClientNotes.Read(""));
        Assert.Empty(ClientNotes.Read(null));
    }

    [Fact]
    public void The_block_it_makes_is_a_message_in_their_words()
    {
        var note = new CanvasClientNote(Guid.NewGuid(), "Susan Holt",
            "<p><em>Three</em> knocks.</p>", "Three knocks.", new DateTime(2026, 9, 14, 15, 4, 5, DateTimeKind.Utc));

        var data = Assert.IsType<MessageData>(ClientNoteBlocks.Fill(new MessageData(), note));

        Assert.Contains("<em>", data.Html);
        Assert.Equal("Susan Holt", data.Author);
        Assert.Equal(note.WrittenUtc, data.TimestampUtc);
    }

    /// <summary>With message blocks switched off the words still arrive, with who said them.</summary>
    [Fact]
    public void Without_message_blocks_the_words_become_an_attributed_note()
    {
        var note = new CanvasClientNote(Guid.NewGuid(), "Susan Holt", "<p>Three knocks.</p>", "Three knocks.", DateTime.UtcNow);

        var data = Assert.IsType<TextData>(ClientNoteBlocks.Fill(new TextData(), note));

        Assert.Contains("Three knocks.", data.Text);
        Assert.Contains("Susan Holt", data.Text);
    }

    [Fact]
    public void A_block_that_cannot_hold_words_is_refused() =>
        Assert.Null(ClientNoteBlocks.Fill(new MapData(), new CanvasClientNote(Guid.NewGuid(), "x", "<p>y</p>", "y", DateTime.UtcNow)));
}
