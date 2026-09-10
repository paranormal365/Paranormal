namespace Ben.Web.Website.Library.Kit;

/// <summary>
/// One frame of a <c>BenSlideshow</c>.
/// </summary>
/// <param name="Url">Where the picture is served from. Anything an <c>img</c> can load.</param>
/// <param name="Caption">
/// What the picture shows. Doubles as the alt text unless <paramref name="AltText"/> says
/// otherwise, so a slide is never mute to somebody who cannot see it.
/// </param>
/// <param name="Credit">Whose picture it is — shown in smaller type beneath the caption.</param>
/// <param name="LinkUrl">Where clicking the slide goes, or null for a slide that is just a picture.</param>
/// <param name="AltText">Overrides the caption as the alt text where the two should differ.</param>
public sealed record BenSlide(
    string Url,
    string? Caption = null,
    string? Credit = null,
    string? LinkUrl = null,
    string? AltText = null)
{
    /// <summary>What a screen reader is told. Falls back through caption, credit, then a plain word.</summary>
    public string Describe() =>
        AltText
        ?? (string.IsNullOrWhiteSpace(Caption) ? null : Caption)
        ?? (string.IsNullOrWhiteSpace(Credit) ? null : Credit)
        ?? "Photograph";
}
