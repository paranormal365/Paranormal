using Xunit;

namespace Ben.Web.Tests.Services;

/// <summary>
/// Shared components that are only reachable through an opt-in parameter must have at least one
/// caller that actually opts in.
/// </summary>
/// <remarks>
/// <para>This codebase has now shipped the same shape four times: something is built, tested, and
/// merged, and nothing in the product ever switches it on. Platform messages were unreadable,
/// permission requests were unreviewable, <c>EquipmentItemFlags.CanRequestCheckout</c> was
/// permanently false, and <c>UserNameLink.ShowAvatar</c> was never set by any caller — so the whole
/// avatar-rendering path from Area 4 was dead in the UI while its own tests passed.</para>
///
/// <para>Unit tests cannot catch this: each piece works. What is missing is the wire between them,
/// and a source scan is the cheapest thing that notices. Deliberately narrow — a list of specific
/// switches known to be load-bearing, not a general rule about unused parameters, which would fire
/// on every genuinely-optional knob in the app.</para>
/// </remarks>
public sealed class ReachableComponentTests
{
    /// <summary>Walks up from the test binaries to the repository root.</summary>
    private static DirectoryInfo RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Ben.slnx")))
            dir = dir.Parent;

        Assert.NotNull(dir);
        return dir!;
    }

    private static IEnumerable<string> RazorSources()
        => new[] { "Ben.Web.Library", "Ben.Web.WebApp" }
            .Select(p => Path.Combine(RepoRoot().FullName, p))
            .Where(Directory.Exists)
            .SelectMany(d => Directory.EnumerateFiles(d, "*.razor", SearchOption.AllDirectories))
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")
                     && !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}"));

    /// <summary>
    /// Each entry is a switch whose whole point is to turn a feature on somewhere. If nothing sets
    /// it, the feature behind it does not exist for any user, however well it is tested.
    /// </summary>
    public static TheoryData<string, string, string> LoadBearingSwitches() => new()
    {
        // Area 4 (item #54): without this, UserAvatar is unreachable and nobody's profile photo
        // is ever rendered next to their name.
        { "ShowAvatar=\"true\"", "UserNameLink.ShowAvatar", "member rosters, attendee lists and comment threads" },
    };

    [Theory]
    [MemberData(nameof(LoadBearingSwitches))]
    public void A_load_bearing_switch_is_actually_switched_on_somewhere(
        string markup, string parameter, string expectedSurfaces)
    {
        var callers = RazorSources()
            .Where(f => File.ReadAllText(f).Contains(markup, StringComparison.Ordinal))
            .Select(Path.GetFileName)
            .ToList();

        Assert.True(
            callers.Count > 0,
            $"Nothing in the app sets {parameter}. It was added to be used on {expectedSurfaces}; "
            + "a parameter no caller sets makes the feature behind it unreachable, however green its "
            + "own tests are.");
    }
}
