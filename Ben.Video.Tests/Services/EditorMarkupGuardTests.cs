using System.Text.RegularExpressions;

namespace Ben.Video.Tests.Services;

/// <summary>
/// Every element the editor's own JavaScript clicks or reads actually exists in its markup.
/// </summary>
/// <remarks>
/// <para>"Replace Media…" clicked <c>#bv-relink-input</c> and that element was on no page in the
/// project. The menu item did nothing at all: the editor's one offer to repair a clip whose media
/// it had lost could not be started, and its handler had been written, maintained and audited for
/// a control that was never rendered (2026-09-05 audit, F14, found on screen).</para>
///
/// <para>Nothing could have caught it. The id is a string on both sides, so the build is happy,
/// and the failure is silence rather than an exception. This is the check that turns that silence
/// into a red test.</para>
/// </remarks>
public sealed class EditorMarkupGuardTests
{
    private static readonly string EditorRoot = FindEditorRoot();

    /// <summary>Every <c>bv-…</c> id the C# passes to a DOM helper, and where it does so.</summary>
    public static TheoryData<string, string> ReferencedElementIds()
    {
        var data = new TheoryData<string, string>();

        // Both forms the interop takes: a selector ("#bv-x") and a bare id ("bv-x").
        var pattern = new Regex(@"""#?(bv-[a-z0-9-]+)""", RegexOptions.IgnoreCase);

        foreach (var file in RazorFiles())
        {
            var text = File.ReadAllText(file);
            foreach (Match match in pattern.Matches(text))
            {
                var id = match.Groups[1].Value;

                // Only ids used as elements. A class name never reaches these helpers.
                if (!text.Contains($"\"#{id}\"") && !MentionsAsInteropTarget(text, id)) continue;

                data.Add(id, Path.GetFileName(file));
            }
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(ReferencedElementIds))]
    public void An_element_the_editor_reaches_for_is_on_the_page(string elementId, string usedIn)
    {
        var declared = RazorFiles().Any(f =>
            File.ReadAllText(f).Contains($"id=\"{elementId}\""));

        Assert.True(declared,
            $"{usedIn} reaches for #{elementId}, and no .razor in the editor declares it. "
            + "The call will silently do nothing.");
    }

    // ── Support ───────────────────────────────────────────────────────────────

    private static bool MentionsAsInteropTarget(string text, string id) =>
        text.Contains($"\"fileAt\", \"{id}\"")
        || text.Contains($"\"fileName\", \"{id}\"")
        || text.Contains($"\"fileCount\", \"{id}\"")
        || text.Contains($"\"clearFileInput\", \"{id}\"");

    private static IEnumerable<string> RazorFiles() =>
        Directory.EnumerateFiles(EditorRoot, "*.razor", SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")
                     && !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}"));

    private static string FindEditorRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);

        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, "Ben.Video.Editor", "Components");
            if (Directory.Exists(candidate)) return candidate;
            dir = dir.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate Ben.Video.Editor/Components.");
    }
}
