using Ben.Web.Tests.Support;
using System.Text.RegularExpressions;
using Xunit;

namespace Ben.Web.Tests.Website;

/// <summary>
/// An implicit Razor expression ends at the end of its line, so a method chain continued onto the next line is printed
/// on the page as text.
/// </summary>
/// <remarks>
/// <para><b>What went wrong.</b> Letting an investigation run longer than one night turned a date into a span and broke
/// the line to keep it readable:</para>
/// <code>
/// @inv.ScheduledDateTime.ToViewerLocalTime(UserState)
///   .ToDisplaySpan(inv.EndDateTime?.ToViewerLocalTime(UserState))
/// </code>
/// <para>Razor reads <c>@inv.ScheduledDateTime.ToViewerLocalTime(UserState)</c> as the whole expression and the second
/// line as markup. The client's own case page said "9/7/2026 9:58:30 AM .ToDisplaySpan(inv.EndDateTime?.ToViewerLocalTime(UserState))",
/// and the same shape sat on four more screens — the organizer's investigation panel, the scheduling proposals and the
/// client's investigations list. It compiles, renders and throws nothing; it was found by looking at a screenshot of the
/// product walk after hosted events merged (2026-09-13).</para>
///
/// <para><b>The rule.</b> A markup line may not begin with <c>.Method(</c> straight after a line that ends in an implicit
/// <c>@expression</c>. Written as <c>@( … )</c> it is one expression, on as many lines as it likes.</para>
/// </remarks>
public sealed class RazorExpressionLineBreakGuardTests
{
    private static readonly Regex EndsInImplicitExpression = new(@"@[A-Za-z_][\w.]*(\([^()]*\))*\s*$");
    private static readonly Regex StartsWithMemberCall = new(@"^\s*\.[A-Za-z_]\w*\(");

    [Fact]
    public void No_implicit_expression_is_continued_onto_the_next_line()
    {
        var files = RepoFiles.Paths("*.razor");
        Assert.True(files.Length > 100, $"only {files.Length} .razor files were found — a guard that reads nothing proves nothing");

        var broken = new List<string>();
        foreach (var path in files)
        {
            var lines = File.ReadAllLines(path);
            for (var i = 1; i < lines.Length; i++)
            {
                // Markup only: past @code the file is C#, where a chain across lines is ordinary.
                if (Regex.IsMatch(lines[i - 1], @"^\s*@(code|functions)\b")) break;

                if (StartsWithMemberCall.IsMatch(lines[i]) && EndsInImplicitExpression.IsMatch(lines[i - 1]))
                    broken.Add($"{Path.GetFileName(path)}:{i + 1}: {lines[i - 1].Trim()} {lines[i].Trim()}");
            }
        }

        Assert.True(broken.Count == 0,
            "these implicit expressions end at the line break, and the rest is printed on the page as text — wrap each in @( … ):\n  "
          + string.Join("\n  ", broken));
    }
}
