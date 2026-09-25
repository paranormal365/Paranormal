using System.Globalization;
using System.Text;

namespace Ben.Canvas.Playwright.Capture;

/// <summary>One picture in the walk, and what the page was doing when it was taken.</summary>
/// <param name="Device">desktop, ipad or iphone.</param>
/// <param name="Theme">dark or light.</param>
/// <param name="Shot">The file name without its folder or extension.</param>
/// <param name="Proves">The sentence the shot is there to show.</param>
/// <param name="ConsoleErrors">Console errors seen since the previous shot.</param>
/// <param name="ServerErrors">Responses of 500 or worse seen since the previous shot.</param>
/// <param name="ThirdPartyHosts">Hosts contacted that are neither the canvas host nor the API.</param>
public sealed record WalkRow(
    string Device,
    string Theme,
    string Shot,
    string Proves,
    IReadOnlyList<string> ConsoleErrors,
    IReadOnlyList<string> ServerErrors,
    IReadOnlyList<string> ThirdPartyHosts);

/// <summary>
/// Collects the walk's rows across its six fixtures and writes one report.md beside the pictures.
/// </summary>
/// <remarks>
/// <para>Each fixture appends its rows to its own file as it goes, because NUnit gives fixtures no shared
/// state and a half-finished walk should still leave readable evidence. <see cref="WalkReportAssembler"/>
/// turns those files into report.md once every fixture in this namespace has finished.</para>
///
/// <para>A row is written even when its shot found problems: the report is the record of what the walk saw,
/// not a pass list. Ben reads the totals at the bottom.</para>
/// </remarks>
public static class WalkReport
{
    private const char Separator = '';

    /// <summary>The folder the pictures and the report are written to.</summary>
    public static string Folder =>
        Environment.GetEnvironmentVariable("BEN_CANVAS_WALK_OUT")
        ?? Path.Combine(TestContext.CurrentContext.WorkDirectory, "canvas-walk");

    /// <summary>Appends one row to this fixture's own file.</summary>
    public static void Append(WalkRow row)
    {
        Directory.CreateDirectory(Folder);
        var line = string.Join(Separator,
            row.Device,
            row.Theme,
            row.Shot,
            row.Proves,
            string.Join(" | ", row.ConsoleErrors),
            string.Join(" | ", row.ServerErrors),
            string.Join(" | ", row.ThirdPartyHosts));
        File.AppendAllText(Path.Combine(Folder, $"rows-{row.Device}-{row.Theme}.tsv"), line + Environment.NewLine, Encoding.UTF8);
    }

    /// <summary>Removes the row files for one fixture, so a rerun does not double its rows.</summary>
    public static void Reset(string device, string theme)
    {
        var path = Path.Combine(Folder, $"rows-{device}-{theme}.tsv");
        if (File.Exists(path)) File.Delete(path);
    }

    /// <summary>Reads every fixture's rows back, in the order the files were written.</summary>
    public static IReadOnlyList<WalkRow> ReadAll()
    {
        if (!Directory.Exists(Folder)) return [];

        var rows = new List<WalkRow>();
        foreach (var file in Directory.EnumerateFiles(Folder, "rows-*.tsv").OrderBy(f => f, StringComparer.Ordinal))
        {
            foreach (var line in File.ReadAllLines(file))
            {
                var parts = line.Split(Separator);
                if (parts.Length != 7) continue;
                rows.Add(new WalkRow(parts[0], parts[1], parts[2], parts[3], Split(parts[4]), Split(parts[5]), Split(parts[6])));
            }
        }

        return rows;
    }

    private static string[] Split(string value) =>
        string.IsNullOrEmpty(value) ? [] : value.Split(" | ", StringSplitOptions.RemoveEmptyEntries);

    /// <summary>Writes report.md from the rows on disk, and returns its path.</summary>
    public static string Write(IReadOnlyList<WalkRow> rows)
    {
        var report = new StringBuilder();
        report.AppendLine("# Canvas screenshot walk");
        report.AppendLine();
        var host = (Environment.GetEnvironmentVariable("BEN_CANVAS_URL") ?? "http://localhost:5125").TrimEnd('/');
        report.AppendLine(CultureInfo.InvariantCulture, $"Host: {host}");
        report.AppendLine(CultureInfo.InvariantCulture, $"Pictures: {rows.Count}");
        report.AppendLine();

        foreach (var group in rows.GroupBy(r => (r.Device, r.Theme)))
        {
            report.AppendLine(CultureInfo.InvariantCulture, $"## {group.Key.Device}, {group.Key.Theme}");
            report.AppendLine();
            report.AppendLine("| Picture | Proves | Console errors | 500s | Third-party hosts |");
            report.AppendLine("| --- | --- | --- | --- | --- |");
            foreach (var row in group)
            {
                report.AppendLine(CultureInfo.InvariantCulture,
                    $"| {row.Shot}.png | {row.Proves} | {Cell(row.ConsoleErrors)} | {Cell(row.ServerErrors)} | {Cell(row.ThirdPartyHosts)} |");
            }

            report.AppendLine();
        }

        var consoleErrors = rows.SelectMany(r => r.ConsoleErrors).ToList();
        var serverErrors = rows.SelectMany(r => r.ServerErrors).ToList();
        var hosts = rows.SelectMany(r => r.ThirdPartyHosts).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(h => h, StringComparer.Ordinal).ToList();

        report.AppendLine("## Totals");
        report.AppendLine();
        report.AppendLine(CultureInfo.InvariantCulture, $"- Console errors: {consoleErrors.Count}");
        report.AppendLine(CultureInfo.InvariantCulture, $"- Responses of 500 or worse: {serverErrors.Count}");
        report.AppendLine(CultureInfo.InvariantCulture, $"- Third-party hosts: {(hosts.Count == 0 ? "none" : string.Join(", ", hosts))}");
        report.AppendLine();
        report.AppendLine("Apple's map script is the only host outside ishaunted.com the canvas is allowed to reach,");
        report.AppendLine("and only once a map box has been opened.");

        Directory.CreateDirectory(Folder);
        var path = Path.Combine(Folder, "report.md");
        File.WriteAllText(path, report.ToString(), Encoding.UTF8);
        return path;
    }

    private static string Cell(IReadOnlyList<string> values) =>
        values.Count == 0 ? "none" : string.Join("<br>", values.Select(v => v.Replace("|", "\\|", StringComparison.Ordinal)));
}

/// <summary>Writes report.md once every capture fixture has finished.</summary>
[SetUpFixture]
public sealed class WalkReportAssembler
{
    [OneTimeTearDown]
    public void WriteTheReport()
    {
        var rows = WalkReport.ReadAll();
        if (rows.Count == 0) return;
        TestContext.Progress.WriteLine($"Walk report: {WalkReport.Write(rows)}");
    }
}
