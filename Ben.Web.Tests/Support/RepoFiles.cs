using System.Collections.Immutable;

namespace Ben.Web.Tests.Support;

/// <summary>
/// The one way a source-scanning guard finds the files it is meant to read.
/// </summary>
/// <remarks>
/// <para><b>Why this exists.</b> Forty-odd guards in this suite prove things about the source by
/// reading it — that an endpoint is called by a screen, that a permission has a label, that a help
/// link exists. Each one walked up from the test assembly to <c>Ben.slnx</c> and enumerated
/// everything below it, and that is wrong in two opposite ways at once, both of them silent.</para>
///
/// <para><b>The false pass.</b> This repository keeps git worktrees under
/// <c>.claude/worktrees/</c> — other branches, checked out as full copies. A guard scanning from
/// the root reads those too, so a guard can be satisfied by a file that exists only on somebody
/// else's branch. The feature it is protecting can be entirely absent from the branch under test
/// and the guard still goes green.</para>
///
/// <para><b>The false failure.</b> One guard tried to avoid that by excluding any path containing
/// <c>/worktrees/</c>. That works from the main checkout and inverts completely when the suite is
/// run FROM a worktree, which is how this repository is usually worked: the walk-up finds the
/// worktree's own <c>Ben.slnx</c>, every file beneath it contains <c>/worktrees/</c>, and the
/// filter throws away the entire tree. The guard then reports the feature inert because it could
/// not see one single file. That cost an hour on 2026-09-12 and produced a confident, wrong claim
/// that the failure was pre-existing on master.</para>
///
/// <para><b>The rule, therefore:</b> resolve the root, then exclude only worktrees <i>nested
/// inside that root</i> — by prefix, never by substring. Whichever tree is being tested is read in
/// full, and any other tree parked inside it is invisible.</para>
/// </remarks>
public static class RepoFiles
{
    /// <summary>The root of the tree the tests were built from.</summary>
    /// <remarks>
    /// Found by walking up to <c>Ben.slnx</c>. In a worktree that is the worktree's own root, which
    /// is exactly right: a guard should judge the branch it was built from and nothing else.
    /// </remarks>
    public static DirectoryInfo Root()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Ben.slnx")))
            dir = dir.Parent;

        if (dir is null)
            throw new InvalidOperationException(
                "Could not find Ben.slnx above " + AppContext.BaseDirectory +
                " — a source-scanning guard cannot run, and must not pass by default.");

        return dir;
    }

    /// <summary>
    /// Every file of one kind that belongs to the tree under test: build output excluded, and any
    /// worktree parked inside this root excluded.
    /// </summary>
    /// <param name="pattern">A search pattern, e.g. <c>*.razor</c> or <c>*.cs</c>.</param>
    public static ImmutableArray<string> Paths(string pattern)
    {
        var root = Root();

        // Prefix, not substring. This is the whole point of the class.
        var nested = Path.Combine(root.FullName, ".claude", Path.DirectorySeparatorChar switch
        {
            _ => "worktrees",
        }) + Path.DirectorySeparatorChar;

        return Directory
            .EnumerateFiles(root.FullName, pattern, SearchOption.AllDirectories)
            .Where(f => !IsBuildOutput(f))
            .Where(f => !f.StartsWith(nested, StringComparison.OrdinalIgnoreCase))
            .OrderBy(f => f, StringComparer.Ordinal)
            .ToImmutableArray();
    }

    /// <summary>The contents of every file of one kind belonging to the tree under test.</summary>
    public static ImmutableArray<string> Contents(string pattern)
        => Paths(pattern).Select(File.ReadAllText).ToImmutableArray();

    private static bool IsBuildOutput(string path)
    {
        var obj = Path.DirectorySeparatorChar + "obj" + Path.DirectorySeparatorChar;
        var bin = Path.DirectorySeparatorChar + "bin" + Path.DirectorySeparatorChar;
        return path.Contains(obj, StringComparison.OrdinalIgnoreCase)
            || path.Contains(bin, StringComparison.OrdinalIgnoreCase);
    }
}
