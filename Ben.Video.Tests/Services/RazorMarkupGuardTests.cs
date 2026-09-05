using System.Text.RegularExpressions;

namespace Ben.Video.Tests.Services;

/// <summary>
/// No Razor comment sits inside a component tag's attribute list.
/// </summary>
/// <remarks>
/// <para>A <c>@* … *@</c> between a component's attributes compiles without complaint and then
/// throws at render time: Blazor reads it as an attribute name and reports that the component "does
/// not have a property matching the name" followed by the whole comment. The editor showed the
/// unhandled-error bar and nothing else (found on screen during phase 5 of the 2026-09-05
/// audit).</para>
///
/// <para>The build cannot catch it and no unit test would have, so this scan is the only thing that
/// stands between a well-meant explanatory comment and a blank editor. Comments above the tag are
/// fine, which is where they belong anyway.</para>
/// </remarks>
public sealed class RazorMarkupGuardTests
{
    private static string EditorRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, "Ben.Video.Editor")))
            dir = dir.Parent;

        Assert.NotNull(dir);
        return Path.Combine(dir!.FullName, "Ben.Video.Editor");
    }

    [Fact]
    public void No_razor_comment_sits_between_a_components_attributes()
    {
        var offenders = new List<string>();

        foreach (var file in Directory.EnumerateFiles(EditorRoot(), "*.razor", SearchOption.AllDirectories))
        {
            var text = File.ReadAllText(file);

            // An opening tag for a component (capitalised name) that has not been closed by the
            // time a Razor comment starts.
            foreach (Match match in Regex.Matches(text, @"<[A-Z]\w*(?:\s[^<>]*?)?@\*", RegexOptions.Singleline))
            {
                // Only a real attribute-list position counts: no ">" between the tag and the
                // comment.
                if (match.Value.Contains('>')) continue;

                var line = text[..match.Index].Count(c => c == '\n') + 1;
                offenders.Add($"{Path.GetFileName(file)}:{line}");
            }
        }

        Assert.True(offenders.Count == 0,
            "A Razor comment inside a component tag's attribute list compiles and then throws at "
            + "render time, taking the whole component down. Move it above the tag: "
            + string.Join(", ", offenders));
    }
}
