using System.Net;
using System.Text;
using System.Text.Json;
using Ben.Canvas.Core.Paste;

namespace Ben.Canvas.Editor.Services;

/// <summary>
/// Reads the case's message thread and keeps the client's side of it.
/// </summary>
/// <remarks>
/// <para>Ben, 2026-09-17: a board should be able to reach for what the client wrote, "so we can track
/// it back to why we are researching something".</para>
/// <para><b>Whose words count.</b> Only the client's. The group's replies are the group's own thinking,
/// and a board is where thinking is done rather than quoted back.</para>
/// <para><b>Which side is which is read defensively.</b> The API sends the side as a number today
/// (<c>Client = 0</c>) and nothing stops a later converter sending "Client" instead, so both are
/// understood and anything unrecognised is left out rather than guessed at — a group reply mistaken
/// for the client's words would be quoted on a board as the client's.</para>
/// <para>A message written in the website's editor carries HTML; one from the phone or the API carries
/// plain text, which is turned into paragraphs here. Either way it goes through the board's own
/// allow-list, because the board re-normalises message HTML on every read and a block whose markup
/// would not survive that is a block that changes when it is reopened.</para>
/// </remarks>
internal static class ClientNotes
{
    private sealed record Message(
        Guid Id, string? AuthorDisplayName, string? Body, string? BodyHtml,
        JsonElement SenderSide, DateTime DateCreated);

    internal static IReadOnlyList<CanvasClientNote> Read(string? body)
    {
        if (string.IsNullOrWhiteSpace(body)) return [];

        List<Message>? messages;
        try
        {
            messages = JsonSerializer.Deserialize<List<Message>>(body, HttpCanvasServerStore.Json);
        }
        catch (JsonException)
        {
            return [];
        }

        return (messages ?? [])
            .Where(IsTheClients)
            .Select(Note)
            .Where(n => n.PlainText.Length > 0)
            .OrderByDescending(n => n.WrittenUtc)
            .ToList();
    }

    /// <summary>Client is 0, or the word "Client". Anything else is not offered.</summary>
    private static bool IsTheClients(Message m) => m.SenderSide.ValueKind switch
    {
        JsonValueKind.Number => m.SenderSide.TryGetInt32(out var side) && side == 0,
        JsonValueKind.String => string.Equals(m.SenderSide.GetString(), "Client", StringComparison.OrdinalIgnoreCase),
        _ => false,
    };

    private static CanvasClientNote Note(Message m)
    {
        var html = string.IsNullOrWhiteSpace(m.BodyHtml)
            ? Paragraphs(m.Body)
            : PasteHtmlAllowList.Normalize(m.BodyHtml!);

        return new CanvasClientNote(
            m.Id,
            string.IsNullOrWhiteSpace(m.AuthorDisplayName) ? "The client" : m.AuthorDisplayName!.Trim(),
            html,
            PasteHtmlAllowList.VisibleText(html),
            m.DateCreated);
    }

    /// <summary>Plain text as the paragraphs it was typed as; blank lines separate, single ones break.</summary>
    private static string Paragraphs(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return "";

        var blocks = text.Replace("\r\n", "\n").Replace('\r', '\n')
            .Split("\n\n", StringSplitOptions.RemoveEmptyEntries);

        var html = new StringBuilder();
        foreach (var block in blocks)
        {
            var trimmed = block.Trim('\n');
            if (trimmed.Trim().Length == 0) continue;
            html.Append("<p>")
                .Append(WebUtility.HtmlEncode(trimmed).Replace("\n", "<br>"))
                .Append("</p>");
        }

        return html.ToString();
    }
}
