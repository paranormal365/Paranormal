using Ben.Canvas.Core.Model;
using Ben.Canvas.Core.Paste;

namespace Ben.Canvas.Editor.Services;

/// <summary>
/// The address written inside a block, when there is one worth looking up.
/// </summary>
/// <remarks>
/// <para>Ben, 2026-09-17: "If they create a card and paste an address in it, should we ask if they
/// want to convert it to a map card? Would that be helpful?" Helpful, but not as a question: a
/// prompt at the moment of typing interrupts somebody mid-sentence to offer them something they
/// may not want. The block's own menu offers it instead, and only when there is an address in the
/// block to offer it about — so it is there when it is wanted and invisible the rest of the time.
/// </para>
/// <para>An address pasted onto the bare board already becomes a map on its own, which is the
/// other half of the same idea (<see cref="PasteClassifier"/> rule 4b).</para>
/// <para>Every kind that can hold words is read, line by line and field by field, because an
/// address on a card is one field among several and an address in a note is one line among many.
/// <see cref="AddressDetector"/> decides; this only decides what to show it.</para>
/// </remarks>
internal static class AddressInBlock
{
    /// <summary>The first address in a block, or null when it holds none.</summary>
    internal static string? Of(NodeData? data) => Candidates(data)
        .Select(AddressWithin)
        .FirstOrDefault(found => found is not null);

    /// <summary>
    /// How many words may be dropped off the front of a line before giving up on it.
    /// </summary>
    /// <remarks>
    /// An address is rarely the whole of what somebody wrote: "We are at Red Boiling Springs, TN
    /// 37150" and "Lived at 12A Church Street" both have one inside them, a few words in. Dropping
    /// leading words one at a time finds it without teaching the detector to hunt — and the cap
    /// keeps a long paragraph from being tried a hundred ways to no purpose (Ben, 2026-09-17).
    /// </remarks>
    private const int WordsToLookPast = 6;

    /// <summary>The address inside a line, whether it is the whole line or starts a few words in.</summary>
    private static string? AddressWithin(string line)
    {
        var trimmed = line.Trim();
        if (trimmed.Length == 0) return null;
        if (AddressDetector.LooksLikeAddress(trimmed)) return trimmed;

        var words = trimmed.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        for (var skip = 1; skip <= Math.Min(WordsToLookPast, words.Length - 1); skip++)
        {
            var rest = string.Join(' ', words.Skip(skip));
            if (AddressDetector.LooksLikeAddress(rest)) return rest;
        }

        return null;
    }

    private static IEnumerable<string> Candidates(NodeData? data)
    {
        switch (data)
        {
            case TextData text:
                return Lines(text.Text);

            // A card's own title first, then its fields in no particular order: a Person card keeps
            // the address under "Connection to the place", an Article under "Publication".
            case CardData card:
                return Lines(card.Title).Concat(card.Fields.Values.Where(v => v is not null).SelectMany(v => Lines(v!)));

            case MessageData message:
                return Lines(PasteHtmlAllowList.VisibleText(message.Html));

            // A link's own words can name a place, and its address is a web address, never a street.
            case LinkData link:
                return Lines(link.Title).Concat(Lines(link.Description));

            case ImageData image:
                return Lines(image.Caption);

            // A map already knows where it is, and a file is named rather than written.
            default:
                return [];
        }
    }

    /// <summary>
    /// The lines of a piece of text, and the whole of it. Both, because an address may be a line of
    /// its own inside a paragraph or the entire field, and the detector reads at most three lines.
    /// </summary>
    private static IEnumerable<string> Lines(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return [];
        var whole = text.Replace("\r\n", "\n").Replace('\r', '\n');
        return new[] { whole }.Concat(whole.Split('\n', StringSplitOptions.RemoveEmptyEntries));
    }
}
