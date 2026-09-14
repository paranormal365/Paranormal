using System.Text.RegularExpressions;
using Ben.Web.Tests.Support;
using Xunit;

namespace Ben.Web.Tests.Website;

/// <summary>
/// A research page's map is never filled in from the case: the page asks the server for nothing about the case itself.
/// </summary>
/// <remarks>
/// Beta feedback, 2026-09-14: a map block holds places the investigator chose — the cemetery where the owners are buried —
/// and must not start from the client's address or the case place's coordinates. The block kit cannot reach a case at all
/// (BlockKitIsolationTests), and BlockMapHost has no way to hand a starting region to a map. The last way in is the page
/// that hosts it, so the page may call only the research endpoints, its own permissions and the geocoder (which answers
/// what the investigator typed or tapped). A new call — the case record, its place, its client request — fails here and
/// has to be argued for.
/// </remarks>
public sealed class CaseResearchPageSourceTests
{
    private static readonly HashSet<string> Allowed =
    [
        "GetMyOrgPermissionsAsync",
        "GetCaseResearchPageAsync",
        "SaveCaseResearchDraftAsync",
        "PublishCaseResearchPageAsync",
        "UploadCaseResearchAttachmentAsync",
        "AddCaseResearchLinkAsync",
        "DeleteCaseResearchAttachmentAsync",
        "SearchGeocodingAsync",
        "ReverseGeocodeAsync",
    ];

    private static readonly string[] Files =
    [
        "Ben.Web.Website.Library/Organization/Cases/CaseResearchPage.razor",
        "Ben.Web.Website.Library/Organization/Cases/CaseResearchRail.razor",
    ];

    private static string Read(string relative) => File.ReadAllText(Path.Combine(RepoFiles.Root().FullName, relative));

    [Fact]
    public void The_page_calls_only_research_permissions_and_the_geocoder()
    {
        var calls = Files
            .SelectMany(f => Regex.Matches(Read(f), @"\b(\w+)\s*\.\s*(\w+Async)\s*\(")
                .Where(m => m.Groups[1].Value is "AdminClient" or "CaseClient" or "PlatformClient")
                .Select(m => (File: Path.GetFileName(f), Method: m.Groups[2].Value)))
            .ToList();

        Assert.Contains(calls, c => c.Method == "GetCaseResearchPageAsync");   // the scan reads something
        var strays = calls.Where(c => !Allowed.Contains(c.Method)).Select(c => $"{c.File}: {c.Method}").Distinct().ToList();
        Assert.True(strays.Count == 0, "A research page may not ask for anything about the case:\n  " + string.Join("\n  ", strays));
    }

    [Fact]
    public void The_page_injects_no_other_client()
    {
        var injects = Files
            .SelectMany(f => Regex.Matches(Read(f), @"^@inject\s+(\S+)\s+(\S+)", RegexOptions.Multiline)
                .Select(m => $"{Path.GetFileName(f)}: {m.Groups[1].Value} {m.Groups[2].Value}"))
            .Where(i => !i.EndsWith(": IBenAdminClient AdminClient") && !i.EndsWith(": IBenUserState UserState") && !i.EndsWith(": IMediaUrlBuilder MediaUrls"))
            .ToList();
        Assert.True(injects.Count == 0, "Unexpected services on the research page (a second client is a second way to read the case):\n  " + string.Join("\n  ", injects));
    }

    [Fact]
    public void A_map_host_carries_no_starting_place()
    {
        // If BlockMapHost ever grows a region or a starting point, the page's map would have somewhere to be pre-filled from.
        var parameters = typeof(Ben.Web.Website.Library.Kit.Blocks.BlockMapHost).GetConstructors().Single().GetParameters()
            .Select(p => p.Name).ToList();
        Assert.Equal(["SearchAsync", "ReverseAsync", "PlaceUrl"], parameters);
    }
}
