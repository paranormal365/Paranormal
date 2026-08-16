namespace Ben.Video.Editor.Effects;

/// <summary>
/// Font-family names offered in text/callout font pickers. Resolved against each user's own browser/OS
/// font stack at render time (see <see cref="Models.TextOverlayRenderer"/>/<see cref="Models.CalloutShapeRenderer"/>)
/// — not tied to any bundled font file or OS-specific path.
/// </summary>
public static class StandardFonts
{
    public static readonly IReadOnlyList<string> Names =
    [
        "Arial", "Helvetica", "Georgia", "Times New Roman",
        "Courier New", "Verdana", "Trebuchet MS"
    ];
}
