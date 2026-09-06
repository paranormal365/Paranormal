using System.Text.RegularExpressions;
using Xunit;

namespace Ben.Web.Tests.Services;

/// <summary>
/// Every script the site serves must be able to parse.
/// </summary>
/// <remarks>
/// <para><c>spectrogram-draw-worker.js</c> shipped with a second, older copy of its whole
/// implementation appended after the first — including that copy's header comment, but without its
/// opening <c>/**</c>. The file therefore threw <c>SyntaxError: Unexpected token '*'</c> the moment
/// the browser loaded it, the draw worker never ran, and the audio editor's spectrogram drew
/// nothing at all from the day the file was added (2026-08-23) until it was found by looking at
/// the page (2026-09-06 audio walk, finding S).</para>
///
/// <para>Nothing caught it: a worker is loaded by URL at runtime, so no build step parses it, and
/// its only symptom was a blank canvas. This checks the shape that broke — an orphaned block
/// comment, and a file that defines the same worker entry point twice — without needing a
/// JavaScript engine in the test run.</para>
/// </remarks>
public sealed class ShippedScriptsParseTests
{
    private static IEnumerable<string> ShippedScripts()
    {
        string[] roots =
        [
            Path.Combine("Ben.Web.Website", "wwwroot", "js"),
            Path.Combine("Ben.Web.Website.Library", "Manage"),
            Path.Combine("Ben.Video.Editor", "wwwroot", "js"),
        ];

        foreach (var root in roots)
        {
            var full = Path.Combine(RepoRoot(), root);
            if (!Directory.Exists(full)) continue;

            foreach (var file in Directory.EnumerateFiles(full, "*.js", SearchOption.AllDirectories))
            {
                // Vendored libraries are somebody else's problem and are minified beyond checking.
                if (file.Contains($"{Path.DirectorySeparatorChar}lib{Path.DirectorySeparatorChar}")) continue;
                if (file.EndsWith(".min.js", StringComparison.OrdinalIgnoreCase)) continue;
                if (Path.GetFileName(file) == "wavesurfer.esm.js") continue;

                yield return file;
            }
        }
    }

    /// <summary>
    /// A block comment that starts in the middle of nowhere is the exact shape that broke the
    /// spectrogram: outside a comment and outside a string, a line beginning with <c>*</c> is a
    /// syntax error.
    /// </summary>
    [Fact]
    public void No_script_has_an_orphaned_block_comment()
    {
        List<string> offenders = [];

        foreach (var file in ShippedScripts())
        {
            var inComment = false;
            var line = 0;

            foreach (var raw in File.ReadLines(file))
            {
                line++;
                var text = raw.Trim();

                if (!inComment && text.StartsWith("/*"))
                {
                    // A one-line /* … */ opens and closes on the spot.
                    if (!text.Contains("*/", StringComparison.Ordinal)) inComment = true;
                    continue;
                }

                if (inComment)
                {
                    if (text.Contains("*/", StringComparison.Ordinal)) inComment = false;
                    continue;
                }

                // Outside a comment: a continuation line, or a stray close, is orphaned.
                if (text.StartsWith("* ") || text == "*/" || text.StartsWith("*/"))
                    offenders.Add($"{Path.GetFileName(file)}:{line}  {text[..Math.Min(60, text.Length)]}");
            }
        }

        Assert.True(offenders.Count == 0,
            "These lines sit outside any comment and start with '*', which is a syntax error — the "
            + "whole file fails to load and whatever it does silently stops happening:\n  "
            + string.Join("\n  ", offenders));
    }

    /// <summary>
    /// A worker that assigns <c>self.onmessage</c> twice has had an older copy of itself appended:
    /// the second assignment wins, so the newer implementation is dead code even when the file
    /// parses. That is how the colormap and mel-scale support would have been lost here.
    /// </summary>
    [Fact]
    public void No_worker_defines_its_entry_point_twice()
    {
        List<string> offenders = [];

        foreach (var file in ShippedScripts())
        {
            var source = File.ReadAllText(file);
            var count = Regex.Matches(source, @"^\s*self\.onmessage\s*=", RegexOptions.Multiline).Count;

            if (count > 1) offenders.Add($"{Path.GetFileName(file)}: assigns self.onmessage {count} times");
        }

        Assert.True(offenders.Count == 0,
            "A second assignment overwrites the first, so the implementation above it never "
            + "runs:\n  " + string.Join("\n  ", offenders));
    }

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);

        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, "Ben.Web.Website")))
            dir = dir.Parent;

        Assert.NotNull(dir);
        return dir!.FullName;
    }
}
