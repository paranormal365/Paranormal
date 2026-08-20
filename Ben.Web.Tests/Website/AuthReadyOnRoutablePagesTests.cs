using System.Text.RegularExpressions;
using Xunit;

namespace Ben.Web.Tests.Website;

/// <summary>
/// A routable page that calls the API on load must wait for auth first.
/// </summary>
/// <remarks>
/// <para><b>The failure is silent and looks like an empty account.</b> A page with its own
/// <c>@page</c> route can be reached by a hard navigation — a bookmark, a pasted link, a full
/// refresh — and Blazor Server renders it once during SSR, before the circuit exists and before
/// any bearer token does. Every API call in that render is unauthorised, and the client turns a
/// non-2xx into an empty list, so the page renders as though the person had nothing.</para>
///
/// <para><b>It hides inside components that are also embedded.</b> <c>OrganizationMembers</c> was
/// the case that prompted this: embedded in the org hub it was always fine, because
/// <c>OrganizationView</c> awaits <c>AuthReady</c> before rendering any tab — so the bug only
/// existed at its own address, which nothing exercised. It was eventually caught by a help
/// screenshot, which captured and published a grid reading "No records available" to a SuperAdmin
/// along with a raw GUID where the group's name should have been.</para>
///
/// <para><b>Source-scanned, and deliberately narrow.</b> The rule is only asserted for pages that
/// both declare a route and call <c>AdminClient</c>/<c>Client</c> in a lifecycle method — a page
/// that renders from its parameters needs no token and should not be forced to wait for one.
/// Pages that guard some other way (an <c>AuthorizeView</c>, an explicit <c>IsAuthenticated</c>
/// check after their own await) are allowed through by name below, with the reason.</para>
/// </remarks>
public sealed class AuthReadyOnRoutablePagesTests
{
    private static DirectoryInfo RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Ben.slnx")))
            dir = dir.Parent;
        return dir ?? throw new InvalidOperationException("repo root not found");
    }

    /// <summary>
    /// Pages that legitimately load without waiting, and why.
    /// </summary>
    /// <remarks>
    /// Anything anonymous belongs here: waiting for auth on a page written for people who have no
    /// account delays the one audience that can never satisfy the wait. Add to this list only with
    /// a reason — an entry here is a claim that the page works signed out.
    /// </remarks>
    private static readonly Dictionary<string, string> Exempt = new(StringComparer.Ordinal)
    {
        ["PublicationsDirectory.razor"] = "Anonymous by design — the directory is written for visitors with no account.",
        ["PublicationHome.razor"]       = "Anonymous; it waits for auth only to decide the Subscribe control.",
        ["PublicationPostReader.razor"] = "Anonymous throughout — a shared link is its main entry point.",
        ["OrgPublicHome.razor"]         = "Public group page.",
        ["OrgPublicPage.razor"]         = "Public CMS page.",
        ["OrgPublicCaseList.razor"]     = "Public case list.",
        ["OrgPublicCaseDetail.razor"]   = "Public case page.",
        ["PublicEventList.razor"]       = "Public events.",
        ["PublicEventDetail.razor"]     = "Public event page.",
        ["PublicInvestigationDetailPage.razor"] = "Public investigation page.",
        ["Login.razor"]                 = "The sign-in page itself.",
        ["SignUp.razor"]                = "Creating an account.",
        ["ConfirmEmail.razor"]          = "Reached from an email link, signed out.",
        ["InviteAccept.razor"]          = "Reached from an invitation link, possibly signed out.",
        ["Home.razor"]                  = "Renders for everyone; its signed-in extras do their own waiting.",

        // Both found by this test on its first run, and both checked rather than assumed:
        ["EquipmentModelPage.razor"]    = "The public equipment catalogue — /api/equipment-catalog/models answers "
                                        + "200 with no token, and the help states anyone may browse it signed out.",
        ["EventAttendanceConfirm.razor"] = "Reached from an emailed link at /attending/{Token}; the token is the "
                                        + "credential, and the recipient may well have no account.",
    };

    private static IEnumerable<string> RazorFiles()
    {
        var root = RepoRoot().FullName;
        foreach (var dir in new[] { "Ben.Web.Website", "Ben.Web.Website.Library" })
        {
            var path = Path.Combine(root, dir);
            if (!Directory.Exists(path)) continue;

            foreach (var file in Directory.EnumerateFiles(path, "*.razor", SearchOption.AllDirectories))
            {
                if (file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")) continue;
                if (file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}")) continue;
                yield return file;
            }
        }
    }

    /// <summary>The body of OnInitializedAsync / OnParametersSetAsync, where load-time calls live.</summary>
    private static string LifecycleBodies(string source)
    {
        var matches = Regex.Matches(
            source,
            @"(OnInitializedAsync|OnParametersSetAsync)\s*\(\s*\)(.*?)(?=\n    (?:private|protected|public|\}))",
            RegexOptions.Singleline);

        return string.Concat(matches.Select(m => m.Groups[2].Value));
    }

    [Fact]
    public void Every_routable_page_that_calls_the_api_on_load_waits_for_auth()
    {
        var offenders = new List<string>();

        foreach (var file in RazorFiles())
        {
            var name = Path.GetFileName(file);
            if (Exempt.ContainsKey(name)) continue;

            var source = File.ReadAllText(file);

            // Routable only: an embedded component is rendered by a parent that has already
            // decided when it is safe to load.
            if (!source.Contains("@page ", StringComparison.Ordinal)) continue;

            var lifecycle = LifecycleBodies(source);
            if (lifecycle.Length == 0) continue;

            var callsApi = Regex.IsMatch(lifecycle, @"\bawait\s+(AdminClient|Client|Api)\.");
            if (!callsApi) continue;

            if (!source.Contains("WaitUntilAuthReadyAsync", StringComparison.Ordinal))
                offenders.Add(name);
        }

        Assert.True(offenders.Count == 0,
            "These routable pages call the API in a lifecycle method without awaiting "
            + "WaitUntilAuthReadyAsync. On a hard navigation they render before the circuit is "
            + "live, every call comes back unauthorised, and the client turns that into empty "
            + "results — so the page reports an empty account rather than a problem:\n  "
            + string.Join("\n  ", offenders.Distinct().OrderBy(x => x, StringComparer.Ordinal))
            + "\n\nIf the page is meant to work signed out, add it to Exempt with the reason.");
    }

    [Fact]
    public void The_scan_actually_finds_pages()
    {
        // Without this, a change to the razor layout or the lifecycle regex would leave the test
        // above passing over an empty set — green, and checking nothing.
        var routable = RazorFiles().Count(f =>
            File.ReadAllText(f).Contains("@page ", StringComparison.Ordinal));

        Assert.True(routable > 30, $"Only {routable} routable pages found — has the layout changed?");
    }
}
