using System.Text.RegularExpressions;
using Xunit;

namespace Ben.Web.Tests.Services;

/// <summary>
/// Guards against writing HTML into a case message body.
/// </summary>
/// <remarks>
/// <para>Three server-generated case messages were built with <c>&lt;strong&gt;</c> in them — a
/// published report, and an investigation cancelled from either side. Nothing renders that field
/// as HTML: the website's <c>MessageBody</c> defaults to plain text for case threads, and the
/// iPhone app draws a text bubble. So clients were shown the literal tags, on both front ends, for
/// as long as those messages have existed. It was found by reading a real conversation on a
/// phone, which is the only place anybody had looked at one recently.</para>
///
/// <para>The rule is the simple one: a case message body is plain text. If it ever needs
/// formatting, the renderers change first and this test changes with them.</para>
/// </remarks>
public sealed class CaseMessageBodiesArePlainTextTests
{
    /// <summary>A `Body = ...` assignment, up to the end of that line.</summary>
    private static readonly Regex BodyAssignment = new(
        @"Body\s*=\s*(?<value>.+)$",
        RegexOptions.Compiled | RegexOptions.Multiline);

    /// <summary>An opening tag — `&lt;` immediately followed by a letter or a slash.</summary>
    private static readonly Regex Markup = new(@"<[a-zA-Z/]", RegexOptions.Compiled);

    private static DirectoryInfo RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Ben.slnx")))
            dir = dir.Parent;
        Assert.NotNull(dir);
        return dir!;
    }

    /// <summary>
    /// Strips comments before scanning — a guard defeated by the prose describing it is the
    /// classic way these stop working.
    /// </summary>
    private static string WithoutComments(string source)
    {
        source = Regex.Replace(source, @"/\*.*?\*/", "", RegexOptions.Singleline);
        return Regex.Replace(source, @"^\s*//.*$", "", RegexOptions.Multiline);
    }

    [Fact]
    public void No_generated_case_message_body_contains_markup()
    {
        var root = RepoRoot();
        var offenders = new List<string>();
        var blocksScanned = 0;

        var files = Directory
            .EnumerateFiles(Path.Combine(root.FullName, "Ben.Data.WebApi"), "*.cs",
                            SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")
                     && !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}"));

        foreach (var file in files)
        {
            var text = WithoutComments(File.ReadAllText(file));

            // Every construction site of the entity, wherever it lives.
            // Fully qualified counts: the report notifier writes
            // `new Ben.Data.Source.Entities.CaseMessage`, and a bare-name scan sailed past the
            // exact bug this test was written for.
            foreach (Match construction in Regex.Matches(
                         text, @"new\s+(?:[A-Za-z_][A-Za-z0-9_]*\.)*CaseMessage\b"))
            {
                // The object initialiser that follows: far enough to cover it, near enough not to
                // wander into the next statement's own Body assignment.
                var start = construction.Index;
                var end = Math.Min(text.Length, start + 900);
                var block = text[start..end];

                var close = block.IndexOf("};", StringComparison.Ordinal);
                if (close > 0) block = block[..close];

                blocksScanned++;

                foreach (Match assignment in BodyAssignment.Matches(block))
                {
                    var value = assignment.Groups["value"].Value;
                    if (Markup.IsMatch(value))
                        offenders.Add($"{Path.GetFileName(file)}: {value.Trim()}");
                }
            }
        }

        // If this drops to zero the scan has stopped finding the entity and proves nothing.
        Assert.True(blocksScanned >= 3,
            $"expected to scan several `new CaseMessage` blocks, saw {blocksScanned}");

        Assert.True(offenders.Count == 0,
            "A case message body is plain text — every renderer escapes it, so tags are shown "
            + "to the client verbatim:\n  " + string.Join("\n  ", offenders));
    }
}
