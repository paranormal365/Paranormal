using System.Text.RegularExpressions;
using Ben.Web.Tests.Support;
using Xunit;

namespace Ben.Web.Tests.Controllers;

/// <summary>
/// Every controller's route is called by something that is not a test (crawl H3, 2026-09-21).
/// </summary>
/// <remarks>
/// <para><b>Why.</b> Every "write-only feature" found this year — eight of them — was an endpoint
/// with no caller: built, tested, and unreachable from any screen. The website side has
/// <c>ReachableComponentTests</c> and <c>OrphanedHandlerTests</c>; nothing asked the same of the
/// API's controllers.</para>
///
/// <para><b>What it checks.</b> Each controller's class-level <c>[Route]</c>, with its
/// <c>{parameters}</c> as wildcards, must appear in some non-test source: the website and its
/// library, the shared clients, the WASM hosts, the canvas and video editors, the iOS app, or
/// another file in the API (a letter that links to it). Tests and the Playwright suite do not
/// count — a route only a test calls is precisely the thing this is looking for. A route's own
/// controller does not count either.</para>
///
/// <para><b>A text search, so it can be fooled in one direction.</b> A client that builds a path
/// from pieces (<c>VenueProfilesUrl(orgId) + "/photos"</c>) is invisible to it; those are listed
/// with where the call really is. It cannot be fooled the other way: a route it finds IS written
/// down somewhere a user's request can come from.</para>
///
/// <para><b>A ratchet.</b> The list below is today's, each with its reason, and it may only get
/// shorter: a new controller nothing calls fails, and an entry that is now called fails too, so
/// the list never keeps a stale excuse.</para>
/// </remarks>
public sealed class EveryApiRouteHasACallerTests
{
    private const string ScaffoldCrud =
        "generic SuperAdmin CRUD from the original scaffold (AdminEntityControllerBase); no screen "
      + "uses it — the admin screens call the per-feature endpoints. A candidate for removal.";

    private const string ScaffoldRead =
        "generic read-only endpoint from the original scaffold (EntityReadControllerBase), gated to "
      + "SuperAdmin at the subclass; nothing reads it — the profile and contact screens use their "
      + "own endpoints. A candidate for removal.";

    private const string ScaffoldLookup =
        "generic read of a lookup list of type names (EntityReadControllerBase, any signed-in "
      + "user); nothing reads it. Harmless, and a candidate for removal.";

    /// <summary>Routes with no caller the search can see, and why each is acceptable today.</summary>
    private static readonly Dictionary<string, string> NoVisibleCaller = new(StringComparer.Ordinal)
    {
        // ── called, but through a path built from pieces ──────────────────────
        ["api/organizations/{orgId:guid}/venue-profiles/{profileId:guid}/photos"] =
            "called: BenAdminClientAdapter.Venue builds it as VenuePhotosUrl = VenueProfilesUrl(orgId) + \"/{profileId}/photos\".",
        ["api/public/cases/{caseId:guid}/media"] =
            "called: GetPublicCaseMediaUrl builds it as GetPublicCaseMediaBaseUrl() + \"{caseId}/media/{fileId}\".",

        // ── called from outside the product's own code ────────────────────────
        ["api/stripe/webhook"] =
            "called by Stripe, configured in the Stripe dashboard.",
        ["api/public/build"] =
            "called by the scripts that start and deploy the hosts (run-e2e.sh, deploy-ishaunted.ps1) "
          + "to prove the running build is this one.",

        // ── leftovers from the scaffold ───────────────────────────────────────
        ["api/admin/organization-addresses"] = ScaffoldCrud,
        ["api/admin/organization-emails"] = ScaffoldCrud,
        ["api/admin/organization-links"] = ScaffoldCrud,
        ["api/admin/organization-notes"] = ScaffoldCrud,
        ["api/admin/organization-pages"] = ScaffoldCrud,
        ["api/admin/organization-phones"] = ScaffoldCrud,
        ["api/admin/user-message-tos"] = ScaffoldCrud,
        ["api/admin/user-messages"] = ScaffoldCrud,
        ["api/organization-addresses"] = ScaffoldRead,
        ["api/organization-emails"] = ScaffoldRead,
        ["api/organization-links"] = ScaffoldRead,
        ["api/organization-notes"] = ScaffoldRead,
        ["api/organization-pages"] = ScaffoldRead,
        ["api/organization-phones"] = ScaffoldRead,
        ["api/user-addresses"] = ScaffoldRead,
        ["api/user-emails"] = ScaffoldRead,
        ["api/user-links"] = ScaffoldRead,
        ["api/user-message-tos"] = ScaffoldRead,
        ["api/user-messages"] = ScaffoldRead,
        ["api/user-notes"] = ScaffoldRead,
        ["api/user-phones"] = ScaffoldRead,
        ["api/organization-email-types"] = ScaffoldLookup,
        ["api/organization-link-types"] = ScaffoldLookup,
        ["api/organization-note-types"] = ScaffoldLookup,
        ["api/organization-phone-types"] = ScaffoldLookup,
        ["api/user-message-types"] = ScaffoldLookup,
    };

    private static readonly Regex ControllerRoute = new(
        @"\[Route\(""([^""]+)""\)\]\s*(?:\[[^\]]*\]\s*)*public\s+(?:sealed\s+|abstract\s+|partial\s+)*class\s+(\w+)");

    private static readonly string[] CallerExtensions = [".cs", ".razor", ".js", ".ts", ".swift", ".html", ".cshtml", ".json"];

    [Fact]
    public void Every_controller_route_is_called_by_something_that_is_not_a_test()
    {
        var root = RepoFiles.Root().FullName;
        var sep = Path.DirectorySeparatorChar;

        var sources = CallerExtensions
            .SelectMany(ext => RepoFiles.Paths("*" + ext))
            .Where(p => !IsTestCode(Path.GetRelativePath(root, p)))
            .Distinct()
            .Select(p => (Path: p, Text: File.ReadAllText(p)))
            .ToList();

        var controllers = RepoFiles.Paths("*.cs")
            .Where(p => Path.GetRelativePath(root, p).StartsWith($"Ben.Data.WebApi{sep}Controllers{sep}", StringComparison.Ordinal));

        var uncalled = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var file in controllers)
        {
            foreach (Match m in ControllerRoute.Matches(NoCredentialsInLogsTests_WithoutComments(File.ReadAllText(file))))
            {
                var route = m.Groups[1].Value.Replace("[controller]", Regex.Replace(m.Groups[2].Value, "Controller$", ""))
                                             .Trim('/');
                var pattern = Pattern(route);
                if (!sources.Any(s => s.Path != file && pattern.IsMatch(s.Text)))
                    uncalled.Add(route);
            }
        }

        var unexplained = uncalled.Where(r => !NoVisibleCaller.ContainsKey(r)).ToList();
        Assert.True(unexplained.Count == 0,
            "Nothing outside the tests calls these routes. Every write-only feature this year looked "
          + "like this: wire a screen to it, or remove it, or add it to NoVisibleCaller saying where "
          + "the call really comes from:\n  " + string.Join("\n  ", unexplained));

        var stale = NoVisibleCaller.Keys.Where(r => !uncalled.Contains(r)).ToList();
        Assert.True(stale.Count == 0,
            "These are called now, or gone. Remove them from NoVisibleCaller so the list only ever "
          + "gets shorter:\n  " + string.Join("\n  ", stale));
    }

    /// <summary>The proof the search is not blind: a route everybody knows is called is found.</summary>
    [Theory]
    [InlineData("api/admin/app-users", "/api/admin/app-users/{id}/roles")]
    [InlineData("api/organizations/{orgId:guid}/events/{eventId:guid}/bookings", "$\"/api/organizations/{orgId}/events/{eventId}/bookings\"")]
    [InlineData("api/public/hosted-events", "\"\\(base)/api/public/hosted-events/\\(id)\"")]
    public void A_route_is_found_however_its_parameters_are_written(string route, string callerText)
        => Assert.Matches(Pattern(route), callerText);

    /// <summary>The route with each {parameter} free to be written any way a caller writes it.</summary>
    private static Regex Pattern(string route)
    {
        var segments = route.Split('/').Select(s => s.StartsWith('{') ? @"[^/""\s]+" : Regex.Escape(s));
        return new Regex(@"(?<![\w-])" + string.Join("/", segments) + @"(?![\w-])", RegexOptions.IgnoreCase);
    }

    /// <summary>Test projects, the browser suite and test folders — none of them a user's request.</summary>
    private static bool IsTestCode(string relative)
    {
        var top = relative.Split(Path.DirectorySeparatorChar)[0];
        return top.EndsWith(".Tests", StringComparison.Ordinal)
            || top.EndsWith(".Playwright", StringComparison.Ordinal)
            || top is "docs" or "ProjectNotes" or "scripts" or "tools"
            || relative.Contains("UITests", StringComparison.Ordinal)
            || relative.Contains($"{Path.DirectorySeparatorChar}Tests{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
            || relative.EndsWith(".min.js", StringComparison.Ordinal);
    }

    private static string NoCredentialsInLogsTests_WithoutComments(string source)
        => Ben.Web.Tests.Services.NoCredentialsInLogsTests.WithoutComments(source);
}
