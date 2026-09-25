using System.Text.RegularExpressions;
using AngleSharp.Dom;
using AngleSharp.Html.Parser;

namespace Ben.Data.WebApi.Services.LinkUnfurl;

/// <summary>What a page says about itself, for a preview card.</summary>
/// <param name="Title">og:title, twitter:title or the title element; decoded, whitespace collapsed.</param>
/// <param name="Description">og:description, twitter:description or meta description; at most 500 characters.</param>
/// <param name="ImageSourceUrl">An absolute https picture address the image proxy would accept, or null.</param>
/// <param name="SiteName">og:site_name, or the page's host.</param>
public sealed record UnfurlData(string? Title, string? Description, string? ImageSourceUrl, string SiteName);

/// <summary>
/// Reads the OpenGraph and Twitter card tags a page publishes about itself.
/// </summary>
/// <remarks>
/// <para><b>Everything here is text, never markup.</b> Values are decoded to the characters the page
/// meant (<c>&amp;amp;</c> becomes <c>&amp;</c>, <c>&amp;lt;script&amp;gt;</c> becomes
/// <c>&lt;script&gt;</c>) and the canvas card renders them as text. Nothing a stranger's page wrote
/// is ever inserted as HTML, so decoding here is correct rather than dangerous.</para>
///
/// <para><b>A picture the proxy would refuse is dropped here</b>, not offered and then refused: an
/// http picture, a <c>data:</c> or <c>javascript:</c> address, or one on a private address. A card
/// that promises a picture and then shows a broken one is worse than a card without one.</para>
///
/// <para>AngleSharp is the same parser the case prose redactor uses; it never runs script and never
/// fetches a subresource.</para>
/// </remarks>
public static class OpenGraphParser
{
    /// <summary>The longest description kept.</summary>
    public const int MaxDescription = 500;

    private const int MaxTitle = 300;
    private const int MaxSiteName = 200;

    private static readonly Regex Whitespace = new(@"\s+", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    /// <summary>Reads <paramref name="html"/>, resolving relative addresses against <paramref name="pageUrl"/>.</summary>
    public static UnfurlData Parse(string html, Uri pageUrl)
    {
        var document = new HtmlParser().ParseDocument(html ?? string.Empty);

        var title = Cut(First(
            Meta(document, "property", "og:title"),
            Meta(document, "name", "twitter:title"),
            Meta(document, "property", "twitter:title"),
            Clean(document.QuerySelector("title")?.TextContent)), MaxTitle);

        var description = Cut(First(
            Meta(document, "property", "og:description"),
            Meta(document, "name", "twitter:description"),
            Meta(document, "property", "twitter:description"),
            Meta(document, "name", "description")), MaxDescription);

        var image = SafeImage(pageUrl, Meta(document, "property", "og:image:secure_url"))
                 ?? SafeImage(pageUrl, Meta(document, "property", "og:image"))
                 ?? SafeImage(pageUrl, Meta(document, "name", "twitter:image"))
                 ?? SafeImage(pageUrl, Meta(document, "property", "twitter:image"));

        var siteName = Cut(Meta(document, "property", "og:site_name"), MaxSiteName) ?? pageUrl.Host;

        return new UnfurlData(title, description, image, siteName);
    }

    private static string? Meta(IDocument document, string attribute, string key)
    {
        foreach (var element in document.QuerySelectorAll("meta"))
        {
            if (string.Equals(element.GetAttribute(attribute), key, StringComparison.OrdinalIgnoreCase)
                && Clean(element.GetAttribute("content")) is { } value)
                return value;
        }
        return null;
    }

    private static string? SafeImage(Uri pageUrl, string? value)
    {
        if (value is null) return null;
        if (!Uri.TryCreate(pageUrl, value, out var resolved)) return null;
        if (resolved.OriginalString.Length > SafeUrlPolicy.MaxUrlLength) return null;
        return SafeUrlPolicy.Refuse(resolved) is null ? resolved.AbsoluteUri : null;
    }

    private static string? First(params string?[] values) => values.FirstOrDefault(v => v is not null);

    private static string? Clean(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var collapsed = Whitespace.Replace(value, " ").Trim();
        return collapsed.Length == 0 ? null : collapsed;
    }

    private static string? Cut(string? value, int max)
        => value is null ? null : value.Length <= max ? value : value[..max].TrimEnd();
}
