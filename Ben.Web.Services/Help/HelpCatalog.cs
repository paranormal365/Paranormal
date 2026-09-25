using Ben.Data.Common.Enums;

namespace Ben.Web.Services.Help;

/// <summary>What a given reader is entitled to see.</summary>
/// <param name="Highest">
/// The reader's ceiling. Everything at or below this is visible.
/// </param>
/// <param name="Roles">
/// The site roles the reader holds that a document can ask for — Seller, Moderator. A role is
/// sideways to the audience ladder: a seller is not "above" a group member, so it cannot be one
/// more rung (store sellers, backlog 251).
/// </param>
public readonly record struct HelpViewer(HelpAudience Highest, IReadOnlyCollection<string>? Roles = null)
{
    /// <summary>Anonymous visitor — the floor, and the default for a signed-out reader.</summary>
    public static readonly HelpViewer Anonymous = new(HelpAudience.Everyone);

    public bool CanSee(HelpAudience audience) => audience <= Highest;

    /// <summary>
    /// The audience floor, and — for a document written for a role — that role. An app
    /// administrator reads every role's document, since they answer every role's questions.
    /// </summary>
    public bool CanSee(HelpDocument document)
        => CanSee(document.Audience)
           && (document.Role is null
               || Highest >= HelpAudience.AppAdministrator
               || (Roles?.Contains(document.Role, StringComparer.OrdinalIgnoreCase) ?? false));
}

/// <summary>One help document: its metadata and the markdown that makes it up.</summary>
/// <param name="Slug">URL segment. Stable — help links in the app point at it.</param>
/// <param name="Title">Shown in navigation and as the page heading.</param>
/// <param name="Summary">One line, shown under the title in the index.</param>
/// <param name="Section">Grouping in the collapsible index, e.g. "Getting Started".</param>
/// <param name="Audience">The lowest audience that should see this.</param>
/// <param name="Order">Sort within the section.</param>
/// <param name="Markdown">The body.</param>
/// <param name="Role">A site role the reader must also hold (front matter <c>role:</c>), e.g. Seller.</param>
public sealed record HelpDocument(
    string Slug,
    string Title,
    string Summary,
    string Section,
    HelpAudience Audience,
    int Order,
    string Markdown,
    string? Feature = null,
    string? Role = null);

/// <summary>A section of the index, with the documents this reader may see inside it.</summary>
public sealed record HelpSection(string Name, IReadOnlyList<HelpDocument> Documents);
