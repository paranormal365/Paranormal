namespace Ben.Web.Website.Library.Kit;

/// <summary>
/// One labelled figure in a <see cref="BenFactRail"/>.
/// </summary>
/// <param name="Label">What the figure is, in two or three words. Drawn small and above it.</param>
/// <param name="Value">The figure itself — a date, a count, a length. Short enough to read at a glance.</param>
/// <param name="Sub">An optional second line: the time under the date, the unit under the count.</param>
/// <param name="Href">When set, the whole tile is a link to that part of the page.</param>
/// <param name="Emphasis">
/// Marks the one tile that decides whether the reader goes on — the next date, usually. At most one
/// per rail; two emphasised tiles emphasise nothing.
/// </param>
public sealed record BenFactItem(
    string Label,
    string Value,
    string? Sub = null,
    string? Href = null,
    bool Emphasis = false);
