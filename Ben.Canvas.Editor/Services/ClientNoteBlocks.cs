using Ben.Canvas.Core.Model;
using Ben.Canvas.Core.Paste;

namespace Ben.Canvas.Editor.Services;

/// <summary>
/// Filling in a block with something the client wrote on the case.
/// </summary>
/// <remarks>
/// <para>A message block, because that is what it is: somebody's words, their name, and the day they
/// wrote them. A card would ask the researcher to retype it into fields, and the point of reaching for
/// the client's own sentence is that it is theirs (Ben, 2026-09-17).</para>
/// <para>The markup goes through the board's allow-list on the way in. The server has sanitised it
/// already, but the board re-normalises message HTML on every read, and a block whose markup would not
/// survive that read is a block that changes the next time it is opened.</para>
/// <para>Its own class rather than a lambda in the editor so the rule can be read and tested alone.</para>
/// </remarks>
internal static class ClientNoteBlocks
{
    /// <summary>Fills freshly made block data in; null when that block cannot hold words.</summary>
    internal static NodeData? Fill(NodeData data, CanvasClientNote note)
    {
        switch (data)
        {
            case MessageData message:
                message.Html = PasteHtmlAllowList.Normalize(note.Html);
                message.Author = note.Author;
                message.TimestampUtc = note.WrittenUtc;
                return message;

            // A host with messages switched off still gets the words, as a note. What is lost is who
            // said them and when, so both are written into the text rather than dropped.
            case TextData text:
                text.Text = Attributed(note);
                return text;

            default:
                return null;
        }
    }

    private static string Attributed(CanvasClientNote note)
    {
        var words = note.PlainText.Trim();
        return words.Length == 0 ? note.Author : $"{words}\n\n— {note.Author}";
    }
}
