using System.Text.RegularExpressions;
using Xunit;

namespace Ben.Web.Tests.Website;

/// <summary>
/// No Telerik component is given a plain HTML attribute it does not declare.
/// </summary>
/// <remarks>
/// <para><b>Telerik components do not splat unmatched attributes — they throw.</b></para>
/// <code>
/// System.InvalidOperationException: Object of type
/// 'Telerik.Blazor.Components.TelerikMaskedTextBox' does not have a property
/// matching the name 'aria-label'.
/// </code>
///
/// <para>What makes this worth a guard rather than a code review note is <i>when</i> it throws:
/// during render. Under Blazor Server that kills the circuit, so the page freezes on the last
/// frame it drew successfully — no error on screen, no re-render, and cancellation tokens that
/// never fire because nothing is running any more. Backlog item #112 was exactly this, and every
/// symptom pointed away from the cause: the API had already answered in milliseconds, and a
/// twenty-second timeout around the call produced nothing at all. It was found by reading the
/// browser console, which is not where anyone looks first.</para>
///
/// <para>The attribute in that case had been added in good faith, as the fix for a real
/// accessibility finding, on the reasonable assumption that Telerik splats what it does not
/// recognise. The next person will assume the same thing. This test is how they find out in a
/// second rather than an afternoon.</para>
/// </remarks>
public class TelerikAttributeSplattingTests
{
    /// <summary>
    /// Attributes that are safe on a Telerik tag.
    /// </summary>
    /// <remarks>
    /// <para><c>@</c>-prefixed things are Blazor directives, not HTML attributes, and are handled
    /// by the compiler: <c>@bind-Value</c>, <c>@ref</c>, <c>@key</c>, <c>@onclick</c>.</para>
    ///
    /// <para><c>class</c> and <c>style</c> are the two plain attributes Telerik components do
    /// accept, because they declare them.</para>
    /// </remarks>
    private static readonly HashSet<string> Allowed = new(StringComparer.Ordinal)
    {
        "class", "style",
    };

    /// <summary>A lower-case attribute name on a Telerik tag — the shape that risks throwing.</summary>
    /// <remarks>
    /// Telerik's own parameters are PascalCase (<c>Value</c>, <c>Mask</c>, <c>AriaLabel</c>), so a
    /// name starting with a lower-case letter is either plain HTML or a directive. Matching on that
    /// keeps the check simple and free of a list of every parameter Telerik has ever shipped, which
    /// would be wrong within a version.
    /// </remarks>
    private static readonly Regex TelerikTag = new(
        @"<Telerik[A-Za-z]+\b(?<attrs>[^>]*?)/?>",
        RegexOptions.Compiled | RegexOptions.Singleline);

    /// <remarks>
    /// Two exclusions earn their keep, both found by running this against the real tree:
    /// <list type="bullet">
    ///   <item><description>A colon before the name means a directive suffix —
    ///     <c>@bind-Value:after</c> would otherwise report an attribute called "after".</description></item>
    ///   <item><description>Requiring a quote after the <c>=</c> skips lambda arrows: an
    ///     <c>OnClose="@(v =&gt; ...)"</c> would otherwise report an attribute called "v".</description></item>
    /// </list>
    /// </remarks>
    private static readonly Regex Attribute = new(
        @"(?<![\w@:-])(?<name>[a-z][a-z0-9-]*)\s*=\s*""",
        RegexOptions.Compiled);

    private static DirectoryInfo RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Ben.slnx")))
            dir = dir.Parent;
        return dir ?? throw new InvalidOperationException("repo root not found");
    }

    private static IEnumerable<string> Components()
    {
        var root = RepoRoot().FullName;
        foreach (var dir in new[] { "Ben.Web.Website", "Ben.Web.Website.Library", "Ben.Video.Editor", "Ben.Wasm.Video" })
        {
            var path = Path.Combine(root, dir);
            if (!Directory.Exists(path)) continue;

            foreach (var file in Directory.EnumerateFiles(path, "*.razor", SearchOption.AllDirectories))
            {
                // obj/ holds generated copies of the same components; scanning them double-reports.
                if (file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")) continue;
                yield return file;
            }
        }
    }

    [Fact]
    public void No_Telerik_component_is_given_an_attribute_it_cannot_accept()
    {
        var root = RepoRoot().FullName;
        var scanned = 0;
        var offenders = new List<string>();

        foreach (var file in Components())
        {
            var text = File.ReadAllText(file);
            scanned++;

            foreach (Match tag in TelerikTag.Matches(text))
            {
                foreach (Match attribute in Attribute.Matches(tag.Groups["attrs"].Value))
                {
                    var name = attribute.Groups["name"].Value;
                    if (Allowed.Contains(name)) continue;

                    offenders.Add($"{Path.GetRelativePath(root, file)} :: {tag.Value.Split('\n')[0].Trim()} — '{name}'");
                }
            }
        }

        Assert.True(offenders.Count == 0,
            "These Telerik components are given plain HTML attributes they do not declare. Telerik "
            + "does not splat unmatched attributes — it throws during render, which kills the Blazor "
            + "circuit and leaves the page frozen with no error anywhere on screen (backlog item "
            + "#112). Use the component's own parameter — AriaLabel, Title, Class — or a plain HTML "
            + "element where the component cannot do the job:\n  "
            + string.Join("\n  ", offenders));

        // A regex that quietly stops matching would leave this passing while checking nothing.
        Assert.True(scanned > 50, $"Only {scanned} components were scanned — has the layout moved?");
    }
}
