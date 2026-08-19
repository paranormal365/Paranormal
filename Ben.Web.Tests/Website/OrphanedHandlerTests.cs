using System.Text.RegularExpressions;
using Xunit;

namespace Ben.Web.Tests.Website;

/// <summary>
/// Finds handlers that commit something but that nothing in the markup calls.
/// </summary>
/// <remarks>
/// <para>
/// This is the shape the template port kept failing in. A dialog converts, its fields bind, it
/// looks finished — and the button that submitted it did not survive, leaving a perfectly good
/// <c>SaveOccurrence</c> with no caller. Twelve dialogs shipped that way, plus an image editor
/// whose two save actions lived in a Telerik title bar, plus a role field that lost its
/// <c>@onblur</c>. Nothing failed, nothing warned: the feature was simply unreachable.
/// </para>
/// <para>
/// Compilers do not catch it because the method is still valid code, and a component test does not
/// catch it because you have to already suspect the control is missing to write the assertion.
/// Reading the sources does.
/// </para>
/// </remarks>
public class OrphanedHandlerTests
{
    /// <summary>Verbs that mean "this changes something", as opposed to opening or closing a dialog.</summary>
    private static readonly Regex CommitVerb = new(
        @"^(Save|Send|Create|Submit|Apply|Confirm|Upload|Propose|Respond|Accept|Reject|Deny|"
        + @"Delete|Remove|Attach|Publish|Add)\w*$",
        RegexOptions.Compiled);

    /// <summary>
    /// Handlers that are deliberately called from somewhere this check cannot see — a parent
    /// component through a parameter, or JavaScript through JSInvokable. Each needs a reason.
    /// </summary>
    private static readonly Dictionary<string, string> Allowed = new()
    {
        // (empty — every commit handler currently has a caller in its own markup)
    };

    private static DirectoryInfo RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Ben.slnx")))
            dir = dir.Parent;
        return dir ?? throw new InvalidOperationException("repo root not found");
    }

    [Fact]
    public void Every_commit_handler_is_reachable_from_the_markup_that_should_call_it()
    {
        var root = RepoRoot().FullName;
        var scanned = 0;
        var orphans = new List<string>();

        foreach (var dir in new[] { "Ben.Web.Website", "Ben.Web.Website.Library" })
        foreach (var file in Directory.EnumerateFiles(Path.Combine(root, dir), "*.razor", SearchOption.AllDirectories))
        {
            var text = File.ReadAllText(file);
            var split = text.IndexOf("@code", StringComparison.Ordinal);
            if (split < 0) continue;

            scanned++;
            var markup = text[..split];
            var code = text[split..];

            foreach (Match m in Regex.Matches(code, @"private\s+(?:async\s+)?[\w<>?\[\]\.]+\s+(\w+)\s*\("))
            {
                var name = m.Groups[1].Value;
                if (!CommitVerb.IsMatch(name)) continue;
                if (Allowed.ContainsKey(name)) continue;

                // Bound from the markup, or called by another method in the same component?
                // One occurrence in the code half is the declaration itself.
                var boundInMarkup = Regex.IsMatch(markup, @"\b" + Regex.Escape(name) + @"\b");
                var calledInCode = Regex.Matches(code, @"\b" + Regex.Escape(name) + @"\b").Count > 1;

                if (!boundInMarkup && !calledInCode)
                    orphans.Add($"{Path.GetRelativePath(root, file)} :: {name}()");
            }
        }

        Assert.True(scanned > 100, $"only {scanned} components were scanned — has the layout moved?");

        Assert.True(orphans.Count == 0,
            "These commit handlers exist but nothing calls them, which means the feature has no way "
            + "in. Either wire up the control that was meant to call it, or delete the handler:\n  "
            + string.Join("\n  ", orphans));
    }
}
