using System.Globalization;
using System.Text.RegularExpressions;

namespace Ben.Canvas.Core.Paste;

/// <summary>A web address found in text.</summary>
public readonly record struct UrlSpan(int Start, int Length);

/// <summary>A run of text that is either a web address or not.</summary>
public sealed record TextSegment(bool IsUrl, string Value);

/// <summary>
/// Finds web addresses in pasted or typed text.
/// </summary>
/// <remarks>
/// The boundary and trimming rules are copied from Ben.Data.Common's FeedTextSegmenter (AtBoundary and
/// UrlLengthAt), not referenced: that library lives in another repository and would pull logging and JSON
/// packages into the browser download. Only addresses with an explicit http or https scheme count - a bare
/// "example.com" in a sentence is a word, not a link.
/// </remarks>
public static class UrlDetector
{
    public static IReadOnlyList<UrlSpan> FindUrls(string? text)
    {
        var spans = new List<UrlSpan>();
        if (string.IsNullOrEmpty(text)) return spans;

        var i = 0;
        while (i < text.Length)
        {
            if (AtBoundary(text, i) && (text[i] is 'h' or 'H') && UrlLengthAt(text, i) is { } length)
            {
                spans.Add(new UrlSpan(i, length));
                i += length;
                continue;
            }

            i++;
        }

        return spans;
    }

    /// <summary>The address when the whole (trimmed) text is exactly one address, otherwise null.</summary>
    public static string? IsSingleUrl(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        var trimmed = text.Trim();
        var spans = FindUrls(trimmed);
        return spans.Count == 1 && spans[0].Start == 0 && spans[0].Length == trimmed.Length ? trimmed : null;
    }

    /// <summary>Splits text into address and non-address runs; joining the values gives the text back.</summary>
    public static IReadOnlyList<TextSegment> Linkify(string? text)
    {
        var segments = new List<TextSegment>();
        if (string.IsNullOrEmpty(text)) return segments;

        var position = 0;
        foreach (var span in FindUrls(text))
        {
            if (span.Start > position) segments.Add(new TextSegment(false, text[position..span.Start]));
            segments.Add(new TextSegment(true, text.Substring(span.Start, span.Length)));
            position = span.Start + span.Length;
        }

        if (position < text.Length) segments.Add(new TextSegment(false, text[position..]));
        return segments;
    }

    private static bool AtBoundary(string body, int i)
        => i == 0
        || char.IsWhiteSpace(body[i - 1])
        || body[i - 1] is '(' or '[' or '<' or '"' or '\'';

    private static int? UrlLengthAt(string body, int i)
    {
        var rest = body.AsSpan(i);

        var scheme = rest.StartsWith("https://", StringComparison.OrdinalIgnoreCase) ? 8
                   : rest.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ? 7
                   : 0;
        if (scheme == 0) return null;

        var end = i + scheme;
        while (end < body.Length && !char.IsWhiteSpace(body[end])) end++;
        if (end == i + scheme) return null;

        while (end > i + scheme)
        {
            var last = body[end - 1];

            if (last is '.' or ',' or ';' or ':' or '!' or '?' or '"' or '\'')
            {
                end--;
                continue;
            }

            if (last is ')' or ']' && !body.AsSpan(i, end - i - 1).Contains(last == ')' ? '(' : '['))
            {
                end--;
                continue;
            }

            break;
        }

        return end == i + scheme ? null : end - i;
    }
}

/// <summary>
/// Recognises a pasted place: a latitude and longitude pair, or a map link that carries one.
/// </summary>
/// <remarks>
/// A decimal point is required in at least one number, so a numbered list ("12, 34") is not a place.
/// Street addresses are handled by <see cref="AddressDetector"/>, which needs a geocoder to finish
/// the job and so is answered outside this pure code.
/// </remarks>
public static partial class CoordinateDetector
{
    public static bool TryParse(string? text, out double latitude, out double longitude)
    {
        latitude = longitude = 0;
        if (string.IsNullOrWhiteSpace(text)) return false;
        var trimmed = text.Trim();

        Match match;
        if ((match = GeoUri().Match(trimmed)).Success
            || (match = AppleLl().Match(trimmed)).Success
            || (match = GoogleAt().Match(trimmed)).Success
            || (match = QueryQ().Match(trimmed)).Success)
        {
            return Accept(match.Groups["lat"].Value, match.Groups["lng"].Value, requireDecimal: false, out latitude, out longitude);
        }

        match = Pair().Match(trimmed);
        return match.Success && Accept(match.Groups["lat"].Value, match.Groups["lng"].Value, requireDecimal: true, out latitude, out longitude);
    }

    private static bool Accept(string lat, string lng, bool requireDecimal, out double latitude, out double longitude)
    {
        latitude = longitude = 0;
        if (requireDecimal && !lat.Contains('.') && !lng.Contains('.')) return false;
        if (!double.TryParse(lat, NumberStyles.Float, CultureInfo.InvariantCulture, out var la)) return false;
        if (!double.TryParse(lng, NumberStyles.Float, CultureInfo.InvariantCulture, out var lo)) return false;
        if (!double.IsFinite(la) || !double.IsFinite(lo) || Math.Abs(la) > 90 || Math.Abs(lo) > 180) return false;
        latitude = la;
        longitude = lo;
        return true;
    }

    private const string Number = @"[-+]?\d{1,3}(?:\.\d+)?";

    [GeneratedRegex(@"^(?<lat>" + Number + @")\s*(?:,\s*|\s+)(?<lng>" + Number + @")$", RegexOptions.CultureInvariant)]
    private static partial Regex Pair();

    [GeneratedRegex(@"^geo:(?<lat>" + Number + @"),(?<lng>" + Number + @")", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase)]
    private static partial Regex GeoUri();

    [GeneratedRegex(@"^https?://maps\.apple\.com/\S*[?&]ll=(?<lat>" + Number + @"),(?<lng>" + Number + @")", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase)]
    private static partial Regex AppleLl();

    [GeneratedRegex(@"^https?://(?:www\.)?google\.[a-z.]+/maps/\S*@(?<lat>" + Number + @"),(?<lng>" + Number + @"),", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase)]
    private static partial Regex GoogleAt();

    [GeneratedRegex(@"^https?://\S*[?&]q=(?<lat>" + Number + @"),\s*(?<lng>" + Number + @")(?:$|&)", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase)]
    private static partial Regex QueryQ();
}

/// <summary>
/// Whether a file's first bytes are a picture a browser can draw.
/// </summary>
/// <remarks>
/// Copied from Ben.Data.Common's ImageSignature, with its reasoning: a name and a content type are both
/// guesses, and an iPhone HEIC renamed .JPG passes every name check and then renders nowhere. HEIC is not
/// accepted. The stored extension comes from the bytes, never from the pasted name.
/// </remarks>
public static class ImageSignature
{
    public const int BytesNeeded = 12;

    public static bool IsBrowserDisplayable(ReadOnlySpan<byte> head) => ExtensionFor(head) is not null;

    public static string? ExtensionFor(ReadOnlySpan<byte> head)
    {
        if (Starts(head, [0xFF, 0xD8, 0xFF])) return ".jpg";
        if (Starts(head, [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A])) return ".png";
        if (Starts(head, "GIF8"u8)) return ".gif";
        if (Starts(head, "BM"u8)) return ".bmp";
        if (head.Length >= 12 && Starts(head, "RIFF"u8) && head[8..12].SequenceEqual("WEBP"u8)) return ".webp";
        return null;
    }

    /// <summary>An ISO media file whose brand is one of the HEIF family iPhones write.</summary>
    public static bool LooksLikeHeic(ReadOnlySpan<byte> head)
    {
        if (head.Length < 12 || !head[4..8].SequenceEqual("ftyp"u8)) return false;
        var brand = head[8..12];
        return brand.SequenceEqual("heic"u8) || brand.SequenceEqual("heix"u8) || brand.SequenceEqual("hevc"u8)
            || brand.SequenceEqual("heim"u8) || brand.SequenceEqual("heis"u8) || brand.SequenceEqual("mif1"u8)
            || brand.SequenceEqual("msf1"u8);
    }

    private static bool Starts(ReadOnlySpan<byte> head, ReadOnlySpan<byte> prefix)
        => head.Length >= prefix.Length && head[..prefix.Length].SequenceEqual(prefix);
}

/// <summary>
/// Recognises a written street address, so that pasting one can become a map of it.
/// </summary>
/// <remarks>
/// <para>Ben, 2026-09-17: "Be able to look up address to make the map." Coordinates were the only
/// way to put a map box somewhere, and nobody writes down coordinates — they write down an address.</para>
/// <para>This says "worth looking up", not "is an address": the geocoder decides. Being wrong is
/// cheap in one direction and dear in the other, so the rules are tight. A line that turns out not
/// to be a place becomes the note it would have been anyway, and the person sees no difference.</para>
/// <para>Two shapes pass. A house number followed somewhere by a street word ("1425 Old Highway 31W");
/// or a two-letter state and a ZIP after a comma ("Red Boiling Springs, TN 37150"). A page reference
/// ("Chapter 3, page 41"), a score, a date and a phone number all fail both.</para>
/// </remarks>
public static partial class AddressDetector
{
    /// <summary>How many lines an address may run to before it is prose instead.</summary>
    private const int MaxLines = 3;

    /// <summary>The longest text still worth asking a geocoder about.</summary>
    private const int MaxLength = 200;

    public static bool LooksLikeAddress(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return false;

        var trimmed = text.Trim();
        if (trimmed.Length > MaxLength) return false;
        if (trimmed.Split('\n').Length > MaxLines) return false;

        // Anything the earlier rules already claim is not ours: a link is a link, and a coordinate
        // pair is already a place that needs no looking up.
        if (UrlDetector.IsSingleUrl(trimmed) is not null) return false;
        if (CoordinateDetector.TryParse(trimmed, out _, out _)) return false;

        var line = trimmed.Replace('\n', ' ').Replace('\r', ' ');
        if (StateAndZip().IsMatch(line)) return true;

        var number = HouseNumber().Match(line);
        return number.Success && StreetWord().IsMatch(line[number.Length..]);
    }

    /// <summary>", TN 37150" — a state's two letters and a ZIP, which together name almost nothing else.</summary>
    [GeneratedRegex(@",\s*[A-Za-z]{2}\.?\s+\d{5}(?:-\d{4})?\b", RegexOptions.CultureInvariant)]
    private static partial Regex StateAndZip();

    /// <summary>A house number at the front: "1425", "12A", never "2026" alone (a street word must follow).</summary>
    [GeneratedRegex(@"^\d{1,6}[A-Za-z]?\s+", RegexOptions.CultureInvariant)]
    private static partial Regex HouseNumber();

    /// <summary>The word that makes a number and some words into a street.</summary>
    [GeneratedRegex(@"\b(?:st|street|rd|road|ave|avenue|blvd|boulevard|ln|lane|dr|drive|ct|court|way|hwy|highway|pike|circle|cir|trail|trl|place|pl|terrace|ter|parkway|pkwy|square|sq|route|rt)\b\.?",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex StreetWord();
}
