using Ben.Web.Tests.Support;
using Xunit;

namespace Ben.Web.Tests.SupportTests;

/// <summary>
/// The file-finder every source-scanning guard depends on.
/// </summary>
/// <remarks>
/// <para>Forty-odd guards prove things by reading the source, so every one of them is only as
/// trustworthy as this. Both of its failure modes are silent, which is why they are pinned here
/// rather than left to be noticed:</para>
///
/// <para><b>Reading too much</b> makes a guard pass on a file that exists only on another branch —
/// this repository keeps other branches as worktrees inside itself. <b>Reading too little</b>
/// makes a guard fail (or, worse, vacuously pass) because it saw nothing at all, which is what
/// happened on 2026-09-12 when a substring exclusion inverted inside a worktree.</para>
/// </remarks>
public sealed class RepoFilesTests
{
    [Fact]
    public void The_root_is_the_tree_these_tests_were_built_from()
    {
        var root = RepoFiles.Root();

        Assert.True(File.Exists(Path.Combine(root.FullName, "Ben.slnx")),
            "the root must be a real checkout, whether that is the main one or a worktree");
    }

    [Fact]
    public void It_finds_the_source_of_whichever_tree_it_is_run_from()
    {
        // The failure this catches: an exclusion that empties the list, leaving every guard to
        // report "nothing calls this" about a codebase it never opened.
        var razor = RepoFiles.Paths("*.razor");
        var code = RepoFiles.Paths("*.cs");

        Assert.True(razor.Length > 100, $"only {razor.Length} .razor files found — the scan is blind");
        Assert.True(code.Length > 500, $"only {code.Length} .cs files found — the scan is blind");
    }

    [Fact]
    public void It_never_reads_a_worktree_parked_inside_the_root()
    {
        // The other failure: a guard satisfied by another branch's copy of a file. Only meaningful
        // when worktrees exist, so it asserts nothing when they do not rather than pretending.
        var root = RepoFiles.Root();
        var nested = Path.Combine(root.FullName, ".claude", "worktrees");
        if (!Directory.Exists(nested)) return;

        var strays = RepoFiles.Paths("*.cs")
            .Where(f => f.StartsWith(nested + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            .Take(3)
            .ToList();

        Assert.True(strays.Count == 0,
            "these belong to another branch and must not satisfy a guard about this one:\n  "
            + string.Join("\n  ", strays));
    }

    [Fact]
    public void Build_output_is_not_source()
    {
        // Generated and compiled files would let a guard pass on something nobody wrote, and the
        // obj tree contains copies of razor markup.
        Assert.DoesNotContain(RepoFiles.Paths("*.cs"),
            f => f.Contains(Path.DirectorySeparatorChar + "obj" + Path.DirectorySeparatorChar)
              || f.Contains(Path.DirectorySeparatorChar + "bin" + Path.DirectorySeparatorChar));
    }

    [Fact]
    public void Contents_and_paths_describe_the_same_set()
    {
        var pattern = "*.slnx";
        Assert.Equal(RepoFiles.Paths(pattern).Length, RepoFiles.Contents(pattern).Length);
    }
}
