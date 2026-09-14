using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Ben.Data.Common.Text;

namespace Ben.Data.Common.Blocks;

/// <summary>A block document that could not be read, with the reason in words.</summary>
public sealed class BlockDocumentFormatException(string message) : Exception(message);

/// <summary>Reading and writing block documents, the one way both the website and the API do it.</summary>
public static class BlockDocumentSerializer
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    /// <summary>
    /// Reads a stored or posted document. Nothing stored is an empty document.
    /// </summary>
    /// <exception cref="BlockDocumentFormatException">
    /// Not JSON, written by a newer version, or holding a block of a kind this build does not know. Refused rather than
    /// guessed at: a block silently dropped is an author's work lost.
    /// </exception>
    public static BlockDocument Parse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return new BlockDocument();

        BlockDocument? doc;
        try
        {
            doc = JsonSerializer.Deserialize<BlockDocument>(json, Options);
        }
        catch (JsonException)
        {
            throw new BlockDocumentFormatException("The page could not be read.");
        }

        if (doc is null) return new BlockDocument();
        if (doc.Version > BlockDocument.CurrentVersion)
            throw new BlockDocumentFormatException("This page was saved by a newer version of the site. Reload to edit it.");

        doc.Blocks ??= [];
        foreach (var block in doc.Blocks)
        {
            if (block is null) throw new BlockDocumentFormatException("The page has an empty block.");
            if (string.IsNullOrWhiteSpace(block.Id)) throw new BlockDocumentFormatException("A block on the page has no id.");
            if (!BlockKinds.All.Contains(block.Kind))
                throw new BlockDocumentFormatException($"The page has a kind of block this site doesn't know (\"{block.Kind}\").");
        }

        var duplicate = doc.Blocks.GroupBy(b => b.Id, StringComparer.Ordinal).FirstOrDefault(g => g.Count() > 1);
        if (duplicate is not null)
            throw new BlockDocumentFormatException("Two blocks on the page have the same id.");

        return doc;
    }

    public static string Serialize(BlockDocument doc) => JsonSerializer.Serialize(doc, Options);

    /// <summary>
    /// A short plain-text summary of the document for lists and the timeline: the words of its text blocks, then — if
    /// there are none — its captions, link titles and map labels. Cut at a word, with an ellipsis, within
    /// <paramref name="maxLength"/>.
    /// </summary>
    public static string PlainTextExcerpt(BlockDocument doc, int maxLength = 300)
    {
        var words = new StringBuilder();
        foreach (var block in doc.Blocks.Where(b => b.Kind == BlockKinds.Text))
        {
            var text = PlainTextHtml.ToText(block.Html).Replace('\n', ' ').Trim();
            if (text.Length == 0) continue;
            if (words.Length > 0) words.Append(' ');
            words.Append(text);
            if (words.Length > maxLength) break;
        }

        if (words.Length == 0)
        {
            var labels = doc.Blocks.SelectMany(b => new[]
                {
                    b.Caption, b.Title ?? b.PreviewTitle,
                    b.MapStops is { Count: > 0 } stops ? string.Join(", ", stops.Select(s => s.Label).Where(l => !string.IsNullOrWhiteSpace(l))) : null,
                })
                .Where(t => !string.IsNullOrWhiteSpace(t));
            words.Append(string.Join(" · ", labels));
        }

        var all = string.Join(' ', words.ToString().Split(' ', StringSplitOptions.RemoveEmptyEntries));
        if (all.Length <= maxLength) return all;

        var cut = all.LastIndexOf(' ', maxLength - 1);
        return (cut > maxLength / 2 ? all[..cut] : all[..(maxLength - 1)]).TrimEnd(',', ';', ':', '.', ' ') + "…";
    }
}
