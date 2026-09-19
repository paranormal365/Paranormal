using Xunit;

namespace Ben.Web.Tests.Services;

/// <summary>
/// A button whose only job is to go somewhere must be a link, not an <c>@onclick</c> handler.
/// </summary>
/// <remarks>
/// <para><b>Why.</b> An <c>@onclick</c> handler does nothing until the SignalR circuit is live.
/// Blazor prerenders the markup first, so between the button being painted and the circuit
/// connecting there is a window in which the control is on screen, looks pressable, and silently
/// swallows clicks. Measured on this machine: ~50ms on the signed-out request page and
/// <b>298ms</b> on <c>/admin/users</c> — and that is localhost, with a warm server and no network
/// in the way. Item 131, reported as "the sign up button… does nothing".</para>
///
/// <para><b>An anchor has no such window.</b> <c>&lt;a href="/somewhere"&gt;</c> is in the
/// server's HTML and works before any script runs, with scripting disabled, and on a middle-click
/// or right-click → open in new tab, which an <c>@onclick</c> button never supports.</para>
///
/// <para><b>The exceptions are real and were checked one at a time.</b> A handler inside a branch
/// that cannot render until after the circuit exists — an auth check, or a null-until-loaded
/// field — has no dead window, because the button does not exist during the prerender. Those are
/// listed in <see cref="RendersOnlyAfterTheCircuitExists"/> with the branch that protects them.
/// Verified rather than assumed: the seven that were converted all appear in the anonymous
/// prerendered HTML, fetched with curl and no JavaScript involved; these three do not.</para>
/// </remarks>
public sealed class NavigationIsAnAnchorTests
{
    /// <summary>
    /// Files where the handler sits in a branch that only renders once the circuit is up, with the
    /// guard that makes it safe. An entry here is a claim that was checked, not a way to opt out.
    /// </summary>
    private static readonly Dictionary<string, string> RendersOnlyAfterTheCircuitExists = new()
    {
        ["OrganizationList.razor"] =
            "Inside @if (UserState.IsSuperAdmin && !UserState.IsImpersonating). Auth resolves after "
          + "the circuit connects, so the button is absent from the prerender — confirmed: an "
          + "anonymous fetch of /organizations does not contain it.",

        ["AdminUserDetail.razor"] =
            "Inside the else of @if (_detail is null). _detail is null during prerender, so the "
          + "loading branch renders and this button does not exist yet.",

        // ClientRequestWizard.razor was here until the 2026-09-06 evaluation's phase 1. The
        // wizard now runs signed out and its two navigating buttons became anchors, so the
        // exception no longer describes anything — and this guard said so.
    };

    private static DirectoryInfo RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Ben.slnx")))
            dir = dir.Parent;

        Assert.NotNull(dir);
        return dir!;
    }

    /// <summary>
    /// Strips comments first — several guards here have fired on their own prose. The lookbehind
    /// on <c>/*</c> stops a file input's <c>accept="image/*"</c> swallowing the rest of the file,
    /// which cost a false accusation once already.
    /// </summary>
    private static string StripComments(string source)
    {
        var s = System.Text.RegularExpressions.Regex.Replace(
            source, @"@\*.*?\*@", string.Empty,
            System.Text.RegularExpressions.RegexOptions.Singleline);

        s = System.Text.RegularExpressions.Regex.Replace(
            s, @"(?<![\w""'])/\*.*?\*/", string.Empty,
            System.Text.RegularExpressions.RegexOptions.Singleline);

        return string.Join('\n', s.Split('\n').Select(line =>
        {
            var slashes = line.IndexOf("//", StringComparison.Ordinal);
            return slashes >= 0 ? line[..slashes] : line;
        }));
    }

    private static IEnumerable<string> RazorFiles() =>
        new[] { "Ben.Web.Website.Library", "Ben.Web.Website" }
            .Select(p => Path.Combine(RepoRoot().FullName, p))
            .Where(Directory.Exists)
            .SelectMany(d => Directory.EnumerateFiles(d, "*.razor", SearchOption.AllDirectories))
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}")
                     && !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}"));

    /// <summary>An @onclick whose entire body navigates to a string literal.</summary>
    private static readonly System.Text.RegularExpressions.Regex LiteralNavigation =
        new(@"@onclick=""@?\(\(\)\s*=>\s*\w*Nav\w*\.NavigateTo\(""[^""]+""\)\)""",
            System.Text.RegularExpressions.RegexOptions.Compiled);

    [Fact]
    public void A_button_that_only_navigates_is_written_as_a_link()
    {
        var offenders = new List<string>();

        foreach (var file in RazorFiles())
        {
            var name = Path.GetFileName(file);
            if (RendersOnlyAfterTheCircuitExists.ContainsKey(name)) continue;

            var lines = StripComments(File.ReadAllText(file)).Split('\n');
            for (var i = 0; i < lines.Length; i++)
                if (LiteralNavigation.IsMatch(lines[i]))
                    offenders.Add($"{name}:{i + 1}");
        }

        Assert.True(
            offenders.Count == 0,
            $"""
             These navigate with @onclick, which does nothing until the circuit connects:

               {string.Join("\n  ", offenders)}

             Use <a href="/target" class="btn …"> instead — it works in the prerender, with
             scripting off, and can be opened in a new tab. If the handler genuinely cannot render
             before the circuit exists, add the file to RendersOnlyAfterTheCircuitExists naming the
             branch that protects it, having checked that an anonymous fetch really does omit it.
             """);
    }

    /// <summary>An exception that has stopped being true is worse than no exception.</summary>
    [Fact]
    public void Every_stated_exception_still_has_a_handler_to_excuse()
    {
        var files = RazorFiles().ToList();

        foreach (var (name, reason) in RendersOnlyAfterTheCircuitExists)
        {
            var match = files.FirstOrDefault(f => Path.GetFileName(f) == name);
            Assert.True(match is not null, $"Exception '{name}' no longer exists — remove it.");
            Assert.False(string.IsNullOrWhiteSpace(reason), $"Exception '{name}' has no reason.");

            Assert.True(
                LiteralNavigation.IsMatch(StripComments(File.ReadAllText(match!))),
                $"'{name}' no longer navigates with @onclick — remove it from the list.");
        }
    }
}
