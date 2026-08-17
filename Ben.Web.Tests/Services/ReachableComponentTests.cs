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

    /// <summary>
    /// A refusal the server takes trouble to explain has to be shown to the person it is about.
    /// </summary>
    /// <remarks>
    /// <para>The fifth instance of the same shape, and the first that was <b>my own recent work</b>.
    /// The taxonomy endpoints answer a probable typo with a 409 listing the names it might have been
    /// — and both callers threw that away, showed "could not be added", and offered no way either to
    /// take the suggested name or to insist on the typed one. The check fired perfectly and made the
    /// feature strictly worse than not having it, because before it the word at least got created.
    /// </para>
    ///
    /// <para>The unit tests all passed, on both sides. They asserted the server returns a 409 with
    /// suggestions, which it did. Nothing asserted that a person ever sees them.</para>
    ///
    /// <para>Scoped to Razor, deliberately: the record legitimately appears in the API, the client
    /// interfaces and the adapters, and finding it there proves only that the pipe exists.</para>
    /// </remarks>
    [Fact]
    public void The_did_you_mean_suggestions_reach_a_screen()
    {
        var screens = RazorSources()
            .Where(f => File.ReadAllText(f).Contains("DidYouMean", StringComparison.Ordinal))
            .Select(Path.GetFileName)
            .ToList();

        Assert.True(
            screens.Count > 0,
            "No screen renders ProbableDuplicateResponse.DidYouMean. The taxonomy endpoints refuse a "
            + "probable typo and return the names it might have been; if no page shows them, the "
            + "person is simply blocked from adding a name that resembles an existing one, with no "
            + "way forward. Wire it to a 'did you mean' prompt with a way to insist.");
    }

    /// <summary>
    /// Wherever those suggestions are shown, there is also a way to reject them.
    /// </summary>
    /// <remarks>
    /// Showing the near-misses without <c>ConfirmDistinct</c> is still a dead end — a prettier one.
    /// "Ring" and "Ping" are two real companies, so a person must always be able to say the word
    /// they typed is genuinely their own.
    /// </remarks>
    [Fact]
    public void Every_screen_showing_suggestions_can_also_overrule_them()
    {
        var withoutAnOverride = RazorSources()
            .Select(f => (Name: Path.GetFileName(f), Text: File.ReadAllText(f)))
            .Where(f => f.Text.Contains("DidYouMean", StringComparison.Ordinal))
            .Where(f => !f.Text.Contains("onfirmDistinct", StringComparison.Ordinal))
            .Select(f => f.Name)
            .ToList();

        Assert.True(
            withoutAnOverride.Count == 0,
            "These screens show the 'did you mean' suggestions but never send ConfirmDistinct, so a "
            + "person whose name really is distinct cannot get past them: "
            + string.Join(", ", withoutAnOverride));
    }

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

    /// <summary>
    /// The site's name never appears as a literal in something a person reads.
    /// </summary>
    /// <remarks>
    /// Ben, 2026-08-17: <i>"Right now, it is IsHaunted.com. I assume it will be that when we are
    /// ready to buy the site, but it may not be available then."</i> It was hardcoded in the footer,
    /// the home page title, the invite page and three email bodies — and the ones that survive a
    /// rename are always the emails, because nobody rereads a template until a customer forwards it
    /// back. <c>SiteIdentity</c> is the one place now, and this stops it drifting out again.
    ///
    /// <para>Scoped to user-facing files. The name legitimately appears in connection strings, the
    /// architecture notes and a <c>Case.IsHaunted</c> property, none of which are a person reading
    /// a page.</para>
    /// </remarks>
    [Fact]
    public void The_site_name_is_never_a_literal_in_anything_a_person_reads()
    {
        var offenders = RazorSources()
            .Where(f => !f.EndsWith("SocialCard.razor", StringComparison.Ordinal))
            .Select(f => (File: Path.GetFileName(f), Text: File.ReadAllText(f)))
            // "IsHaunted.com" specifically — the bare word is a real property name on a case.
            .Where(x => x.Text.Contains("IsHaunted.com", StringComparison.OrdinalIgnoreCase))
            .Select(x => x.File)
            .ToList();

        Assert.True(
            offenders.Count == 0,
            "The site's name is hardcoded in: " + string.Join(", ", offenders)
            + ". Inject SiteIdentity and use its Name — the domain is not settled, and a literal is "
            + "a rename waiting to be missed.");
    }
}
