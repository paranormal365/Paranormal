using System.Text.RegularExpressions;
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
        => new[] { "Ben.Web.Website.Library", "Ben.Web.Website" }
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

    /// <summary>
    /// Page-scoped templates can be both saved and applied from the UI.
    /// </summary>
    /// <remarks>
    /// <para>The sixth instance of the write-only shape, and the quietest: <c>CmsTemplateScope.Page</c>
    /// was defined, saved, listed, updated, deleted and sanitized by the API, and <b>no screen ever
    /// created a page from one</b>. Every layer worked; the feature did not exist.</para>
    ///
    /// <para>Both halves are checked because either alone is useless — somewhere to save layouts
    /// nobody can apply, or a picker that is always empty.</para>
    /// </remarks>
    [Fact]
    public void Page_layouts_can_be_both_saved_and_applied()
    {
        var sources = RazorSources()
            .Select(f => (Name: Path.GetFileName(f), Text: File.ReadAllText(f)))
            .ToList();

        var saves = sources
            .Where(f => f.Text.Contains("CmsTemplateScope.Page", StringComparison.Ordinal)
                     && f.Text.Contains("SaveCmsTemplateAsync", StringComparison.Ordinal))
            .Select(f => f.Name).ToList();

        var applies = sources
            .Where(f => f.Text.Contains("FromTemplateId", StringComparison.Ordinal))
            .Select(f => f.Name).ToList();

        Assert.True(saves.Count > 0,
            "No screen saves a page-scoped template, so the layout picker can only ever be empty.");

        Assert.True(applies.Count > 0,
            "No screen sends FromTemplateId, so a saved page layout can never be applied to a page. "
            + "That is how this feature spent its first life: fully built in the API and unreachable.");
    }

    /// <summary>
    /// A case-media section can be authored, and what it publishes can actually be fetched.
    /// </summary>
    /// <remarks>
    /// <para>Three halves rather than two, because this feature has an extra way to be silently
    /// dead. The section type could be pickable with no editor behind it; the editor could exist
    /// with nothing rendering the result; and — the one that nearly shipped — both could work while
    /// the images pointed at an endpoint that refuses anonymous callers, so every visitor saw broken
    /// frames and only the logged-in author saw a gallery.</para>
    ///
    /// <para>That last check is why the renderer is asserted to use the public media URL by name.
    /// Pointing it at <c>GetFileDownloadUrl</c> instead would compile, pass every resolution test,
    /// and look correct to whoever built it.</para>
    /// </remarks>
    [Fact]
    public void Case_media_can_be_authored_rendered_and_fetched()
    {
        var sources = RazorSources()
            .Select(f => (Name: Path.GetFileName(f), Text: File.ReadAllText(f)))
            .ToList();

        var authors = sources
            .Where(f => f.Text.Contains("GetPublishableCaseMediaAsync", StringComparison.Ordinal))
            .Select(f => f.Name).ToList();

        var renderers = sources
            .Where(f => f.Text.Contains("CmsSectionType.CaseMedia", StringComparison.Ordinal)
                     && f.Text.Contains("PublicMediaBaseUrl", StringComparison.Ordinal))
            .Select(f => f.Name).ToList();

        var wiring = sources
            .Where(f => f.Text.Contains("GetPublicCaseMediaBaseUrl", StringComparison.Ordinal))
            .Select(f => f.Name).ToList();

        Assert.True(authors.Count > 0,
            "No screen fetches a case's publishable media, so a case-media section can be added to a "
            + "page but never filled in.");

        Assert.True(renderers.Count > 0,
            "Nothing renders a CaseMedia section from the public media URL, so whatever an author "
            + "picks is stored and never drawn.");

        Assert.True(wiring.Count > 0,
            "No page passes PublicMediaBaseUrl to the section renderer, so every published case "
            + "photo resolves against an empty prefix and the gallery is a row of broken images — "
            + "which the author, being logged in, would never have seen.");
    }

    /// <summary>
    /// The nearby-search endpoint that item #88 extended is actually called by a screen.
    /// </summary>
    /// <remarks>
    /// <c>SearchController.Nearby</c> honoured every per-address privacy setting for a long time
    /// and had no caller at all — the entire reason item #88 was "server side built, UI remains"
    /// rather than "not started". Extending the server again without a screen calling it would
    /// have repeated exactly that mistake.
    /// </remarks>
    [Fact]
    public void Nearby_search_is_called_by_a_screen()
    {
        var callers = RazorSources()
            .Where(f => File.ReadAllText(f).Contains("GetNearbyAsync", StringComparison.Ordinal))
            .Select(Path.GetFileName)
            .ToList();

        Assert.True(callers.Count > 0,
            "Nothing calls IBenPlatformClient.GetNearbyAsync, so the nearby-search endpoint is "
            + "server-only again — the same shape item #88 found and fixed once.");
    }

    /// <summary>
    /// Every half of sharing a session by link reaches a screen (item 207).
    /// </summary>
    /// <remarks>
    /// <para>Three separate calls, because three separate omissions are each individually
    /// plausible and each individually ruins the feature. A make with no withdraw is a link that
    /// cannot be pulled back. A withdraw with no anonymous read is a page the recipient cannot
    /// open. This codebase has shipped a write-only feature seven times; a share link that can be
    /// created and never revoked would be the eighth, and worse than the others because the thing
    /// that cannot be undone is a stranger's access to somebody's recordings.</para>
    ///
    /// <para>Matched on the client method names rather than the URLs: a route can be rewritten,
    /// but a component that does not call the method is not calling the endpoint under any
    /// spelling of it.</para>
    /// </remarks>
    [Theory]
    [InlineData("CreateFieldSessionShareAsync", "nothing on any screen can make a share link")]
    [InlineData("RevokeFieldSessionShareAsync", "a link can be made and never withdrawn")]
    [InlineData("GetSharedFieldSessionAsync", "no page opens a share link, so every link 404s for its recipient")]
    public void Both_halves_of_sharing_a_session_reach_a_screen(string method, string consequence)
    {
        var callers = RazorSources()
            .Where(f => File.ReadAllText(f).Contains(method, StringComparison.Ordinal))
            .Select(Path.GetFileName)
            .ToList();

        Assert.True(callers.Count > 0,
            $"Nothing calls {method}, so {consequence} (item 207).");
    }

    /// <summary>
    /// Deleting a person reaches a screen, and so does the preview it is read against.
    /// </summary>
    /// <remarks>
    /// The preview is the whole safety of this feature: it is the only place a SuperAdmin learns
    /// what will be destroyed, what will merely be emptied, and whether the account row survives
    /// at all. A delete wired up without it would be a button that does something irreversible
    /// and says nothing first — which is the same shape as the seven write-only features this
    /// codebase has already shipped, with worse consequences.
    /// </remarks>
    [Theory]
    [InlineData("GetAppUserPurgePreviewAsync", "nothing shows a SuperAdmin what deleting a person would destroy")]
    [InlineData("PurgeAppUserAsync", "no screen can actually delete a person")]
    public void Deleting_a_person_reaches_a_screen(string token, string consequence)
    {
        var callers = RazorSources()
            .Where(f => File.ReadAllText(f).Contains(token, StringComparison.Ordinal))
            .Select(Path.GetFileName)
            .ToList();

        Assert.True(callers.Count > 0, $"Nothing references {token}, so {consequence}.");
    }

    /// <summary>
    /// The users list is one of the ways into the delete screen.
    /// </summary>
    /// <remarks>
    /// <para><b>Named files, not "something somewhere".</b> The first two versions of this guard
    /// both passed while the grid button was gone: the first matched the delete page's own
    /// <c>@page</c> directive, the second matched the SuperAdmin nav entry. Each was a true
    /// statement about the route and told nothing about the thing Ben actually asked for — a
    /// delete button on the users list. A source scan that can be satisfied by the file it is
    /// checking, or by an unrelated one, is not a guard.</para>
    ///
    /// <para>The nav entry is asserted separately for the same reason: the two are different ways
    /// in and losing either is a different regression.</para>
    /// </remarks>
    [Theory]
    [InlineData("AdminUsers.razor", "the SuperAdmin users list has no delete button")]
    [InlineData("BenNav.razor", "the SuperAdmin menu has no way to the delete screen")]
    public void The_delete_screen_is_linked_from(string fileName, string consequence)
    {
        var file = RazorSources()
            .FirstOrDefault(f => Path.GetFileName(f).Equals(fileName, StringComparison.Ordinal));

        Assert.True(file is not null, $"{fileName} has moved or been renamed; update this guard.");
        Assert.True(File.ReadAllText(file!).Contains("/admin/delete-user", StringComparison.Ordinal),
            $"{fileName} no longer links to /admin/delete-user, so {consequence}.");
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

    /// <summary>Razor and C# comments removed, so only what renders is examined.</summary>
    /// <remarks>
    /// <para>"Anything a person reads" means the rendered page, not the source. A comment
    /// explaining <i>why</i> a page once broke on the ishaunted.com deployment is documentation,
    /// and flagging it tells the author to make their comment vaguer — which is the opposite of
    /// what this test is for.</para>
    ///
    /// <para><b>Third time this pattern has bitten.</b> The stylesheet import guard flagged the
    /// comment describing the import it replaced; the forwarded-headers guard flagged the comment
    /// naming the middleware it must precede; this one flagged a comment naming the deployment.
    /// Any guard that scans source for a literal should strip comments first, as a rule.</para>
    /// </remarks>
    private static string StripComments(string source)
    {
        source = Regex.Replace(source, @"@\*.*?\*@", "", RegexOptions.Singleline);   // Razor
        source = Regex.Replace(source, @"/\*.*?\*/", "", RegexOptions.Singleline);   // C# block
        source = Regex.Replace(source, @"//[^\n]*", "", RegexOptions.Multiline);      // C# line
        return source;
    }

    [Fact]
    public void The_site_name_is_never_a_literal_in_anything_a_person_reads()
    {
        var offenders = RazorSources()
            .Where(f => !f.EndsWith("SocialCard.razor", StringComparison.Ordinal))
            .Select(f => (File: Path.GetFileName(f), Text: StripComments(File.ReadAllText(f))))
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
