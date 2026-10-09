namespace Ben.Web.Website.Library.Kit;

/// <summary>
/// A page the rich-text editor's "Link to one of your pages" button offers.
/// </summary>
/// <param name="Title">What the list shows, and the link's words when nothing is selected.</param>
/// <param name="Href">Where the link goes — a site-relative address such as <c>/o/ghost-squad/about</c>.</param>
/// <param name="Depth">How far under the top level it sits in the menu, for indenting the list.</param>
public sealed record BenEditorPageLink(string Title, string Href, int Depth = 0);
