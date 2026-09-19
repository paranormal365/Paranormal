namespace Ben.Service.Models.Support;

/// <summary>
/// What a pasted https link says about itself, for a preview card on a case canvas.
/// </summary>
/// <param name="Url">The link as the server normalised it (lower-case scheme and host, no fragment).</param>
/// <param name="Host">The link's host name, for the card's footer.</param>
/// <param name="Title">The page's <c>og:title</c>, <c>twitter:title</c> or <c>&lt;title&gt;</c>.</param>
/// <param name="Description">The page's description, at most 500 characters.</param>
/// <param name="ImageSourceUrl">
/// The page's own https <c>og:image</c> — a third party's address, never a proxy address. The client
/// builds <c>{ApiBaseUrl}/api/link-unfurl/image?url=</c> from it at render time, so a card saved on
/// one deployment still draws on another.
/// </param>
/// <param name="SiteName">The page's <c>og:site_name</c>, or its host.</param>
public sealed record LinkUnfurlRecord(
    string Url,
    string Host,
    string? Title,
    string? Description,
    string? ImageSourceUrl,
    string? SiteName);
