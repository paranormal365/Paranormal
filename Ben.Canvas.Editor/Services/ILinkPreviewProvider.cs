using Ben.Canvas.Core.Model;

namespace Ben.Canvas.Editor.Services;

/// <summary>What a link card shows.</summary>
/// <param name="Host">The site's host name, always set for a web address, so a card can fall back to it.</param>
/// <param name="ImageSourceUrl">The page's own https picture address; the card fetches it through the API's image proxy.</param>
/// <param name="Tier">Never <see cref="LinkPreviewTier.None"/>: a card that has been asked is at least <see cref="LinkPreviewTier.HostOnly"/>.</param>
public sealed record LinkPreviewCard(
    string Url,
    string Host,
    string Kind,
    string? Title,
    string? Description,
    string? ImageSourceUrl,
    string? SiteName,
    LinkPreviewTier Tier);

/// <summary>
/// Turns a pasted address into a card like Twitter/X shows: our own records first, then the page's own preview
/// fetched by the API, and when neither answers, the site and address (R34 - never an error block).
/// </summary>
public interface ILinkPreviewProvider
{
    /// <summary>Never throws and never answers null.</summary>
    Task<LinkPreviewCard> GetAsync(string url, CancellationToken ct = default);
}
