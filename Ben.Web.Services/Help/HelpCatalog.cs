using Ben.Data.Common.Enums;

namespace Ben.Web.Services.Help;

/// <summary>What a given reader is entitled to see.</summary>
/// <param name="Highest">
/// The reader's ceiling. Everything at or below this is visible.
/// </param>
public readonly record struct HelpViewer(HelpAudience Highest)
{
    /// <summary>Anonymous visitor — the floor, and the default for a signed-out reader.</summary>
    public static readonly HelpViewer Anonymous = new(HelpAudience.Everyone);

    public bool CanSee(HelpAudience audience) => audience <= Highest;
}

/// <summary>One help document: its metadata and the markdown that makes it up.</summary>
/// <param name="Slug">URL segment. Stable — help links in the app point at it.</param>
/// <param name="Title">Shown in navigation and as the page heading.</param>
/// <param name="Summary">One line, shown under the title in the index.</param>
/// <param name="Section">Grouping in the collapsible index, e.g. "Getting Started".</param>
/// <param name="Audience">The lowest audience that should see this.</param>
/// <param name="Order">Sort within the section.</param>
/// <param name="Markdown">The body.</param>
public sealed record HelpDocument(
    string Slug,
    string Title,
    string Summary,
    string Section,
    HelpAudience Audience,
    int Order,
    string Markdown);

/// <summary>A section of the index, with the documents this reader may see inside it.</summary>
public sealed record HelpSection(string Name, IReadOnlyList<HelpDocument> Documents);
