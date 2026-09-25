using System.Text;
using System.Text.RegularExpressions;

namespace Ben.Web.Playwright.Capture;

/// <summary>
/// Every visual fault grouped by what CAUSES it, for the top of a report.
/// </summary>
/// <remarks>
/// <para><b>Why.</b> The every-seat sweep of 2026-09-22 produced 792 rows from about ten causes:
/// the same selector at the same ratio, over and over — one <c>a.small</c> at 3.77:1 on sixty-odd
/// screens. Read screen by screen that is unreadable, and a report nobody reads is a report nobody
/// re-runs. Worse, the length hides how few things are actually wrong, so it reads as a site in
/// trouble rather than a short list of variables.</para>
///
/// <para>Grouping by (kind, selector, reading) turns it back into the list it always was. The
/// screen-by-screen detail stays underneath for anyone chasing a single row.</para>
///
/// <para><b>Shared deliberately.</b> Both <see cref="VisualAuditWalk"/> and <c>SiteSweep</c> run
/// the same auditor and both want this table; written twice it would be two tables that slowly
/// stopped agreeing about what a cause is.</para>
///
/// <para><b>Disabled controls are not in the table.</b> They are exempt from the contrast minimum
/// (WCAG 1.4.3 excludes inactive components) and the auditor reports them as their own kind.
/// Counting them here would put the very rows the exemption exists to remove back at the top of
/// the report. They get one line at the end instead, so that "checked and exempt" cannot be read
/// as "never looked at".</para>
/// </remarks>
internal static class VisualRollUp
{
    internal const string ExemptKind = "contrast exempt (disabled)";

    /// <summary>One finding and the screen it was seen on. Screen is anything unique per screen.</summary>
    internal sealed record Row(string Screen, string Kind, string El, string Detail);

    /// <summary>The leading contrast reading in a detail line, if it has one.</summary>
    internal static string ReadingOf(string detail)
    {
        var m = Regex.Match(detail, @"^(\d+\.\d+):1");
        return m.Success ? m.Groups[1].Value + ":1" : "";
    }

    /// <summary>The roll-up, as markdown, ending in a blank line.</summary>
    internal static string Render(IReadOnlyCollection<Row> all, int maxCauses = 40)
    {
        var faults = all.Where(r => r.Kind != ExemptKind).ToList();

        var causes = faults
            .GroupBy(r => (r.Kind, r.El, Reading: ReadingOf(r.Detail)))
            .Select(g => new
            {
                g.Key.Kind,
                g.Key.El,
                g.Key.Reading,
                Rows = g.Count(),
                Screens = g.Select(r => r.Screen).Distinct().Count()
            })
            .OrderByDescending(c => c.Rows).ThenByDescending(c => c.Screens)
            .ToList();

        var sb = new StringBuilder("## Every fault, by cause\n\n");

        if (causes.Count == 0)
        {
            sb.Append("Nothing found on any screen walked.\n");
        }
        else
        {
            var screens = faults.Select(r => r.Screen).Distinct().Count();
            sb.Append($"**{faults.Count} rows on {screens} screens, from {causes.Count} causes.** ")
              .Append("The rows are symptoms. Fix one row of this table and every screen carrying ")
              .Append("it is fixed with it — so work this list, not the one below.\n\n")
              .Append("| rows | screens | kind | selector | reading |\n")
              .Append("|---:|---:|---|---|---|\n");

            foreach (var c in causes.Take(maxCauses))
                sb.Append($"| {c.Rows} | {c.Screens} | {c.Kind} | `{c.El}` | {c.Reading} |\n");

            if (causes.Count > maxCauses)
                sb.Append($"\n…and {causes.Count - maxCauses} more causes of one or two rows each.\n");
        }

        var exempt = all.Where(r => r.Kind == ExemptKind).ToList();
        if (exempt.Count > 0)
            sb.Append("\nAlso measured and NOT counted above: disabled controls under the contrast ")
              .Append($"minimum, on {exempt.Select(r => r.Screen).Distinct().Count()} screens. They ")
              .Append("are exempt, and are reported so that \"checked and exempt\" cannot be mistaken ")
              .Append("for \"never looked at\".\n");

        return sb.Append('\n').ToString();
    }
}
