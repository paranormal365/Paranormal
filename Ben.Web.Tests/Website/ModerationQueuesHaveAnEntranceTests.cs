using Xunit;

namespace Ben.Web.Tests.Website;

/// <summary>
/// Every moderation queue endpoint is reachable from a page somebody can open.
/// </summary>
/// <remarks>
/// <para><b>The 2026-09-17 audit's worst functional finding.</b> <c>GET</c> and <c>POST
/// api/moderation/archive-media</c> were written, tested by nothing, and called from nowhere —
/// no adapter method, no page, no nav entry. Meanwhile any signed-in reader could flag a
/// published field session from the place page, which sets <c>MediaReviewState</c> to
/// <c>Held</c>, and <c>ArchiveMediaPublication</c> serves only <c>Approved</c>. The flag's own
/// design note says "the flag acts, then a person decides". No person could decide. One flag
/// permanently removed a contributor's night from the archive, invisibly.</para>
///
/// <para>The same was true of event evidence, which had no queue endpoint at all and an
/// <c>ArchiveReviewNote</c> column documented as being "for the moderator queue".</para>
///
/// <para><b>Why a source scan and not a behaviour test.</b> The endpoints always worked when
/// called. What was broken was that nothing called them, and no unit test of a controller can
/// fail for that reason — which is exactly why this shipped. This asserts the chain that makes
/// a server capability usable: route → adapter method → page → nav entry. It is the
/// "a server guard needs a UI path" house rule, written down for one family of endpoints.</para>
///
/// <para>Deliberately not a sweep over every route in the tree. Plenty of endpoints are
/// legitimately called only by the phone, an installer or another service, and a guard that has
/// to carry an allowlist of those is a guard nobody trusts. These four are the ones whose absence
/// destroyed user work.</para>
/// </remarks>
public sealed class ModerationQueuesHaveAnEntranceTests
{
    private static DirectoryInfo RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Ben.slnx")))
            dir = dir.Parent;
        return dir ?? throw new InvalidOperationException("repo root not found");
    }

    private static string Read(params string[] parts)
        => File.ReadAllText(Path.Combine(RepoRoot().FullName, Path.Combine(parts)));

    /// <summary>The four routes that hold a place archive's decisions.</summary>
    public static TheoryData<string> ArchiveRoutes() =>
    [
        "/api/moderation/archive-media",
        "/api/moderation/archive-evidence",
    ];

    [Theory]
    [MemberData(nameof(ArchiveRoutes))]
    public void An_archive_queue_route_has_a_client_that_calls_it(string route)
    {
        var adapter = Read("Ben.Web.Services", "WebApi", "BenAdminClientAdapter.Feed.cs");

        Assert.Contains(route, adapter);
    }

    /// <summary>
    /// And a page that calls the client. An adapter method with no caller is the same defect one
    /// layer up — which is how "Invite by email" came to exist twice with one entrance.
    /// </summary>
    [Theory]
    [InlineData("GetArchiveMediaReviewAsync")]
    [InlineData("ReviewArchiveMediaAsync")]
    [InlineData("GetArchiveEvidenceReviewAsync")]
    [InlineData("ReviewArchiveEvidenceAsync")]
    public void An_archive_queue_client_method_is_called_by_a_page(string method)
    {
        var page = Read("Ben.Web.Website.Library", "SuperAdmin", "ModerationArchiveQueue.razor");

        Assert.Contains(method, page);
    }

    /// <summary>
    /// The page is routable and in the navigation. A page nobody links to is the finding the same
    /// audit made about <c>/organization-security</c>.
    /// </summary>
    [Fact]
    public void The_archive_queue_page_is_routed_and_navigable()
    {
        var page = Read("Ben.Web.Website.Library", "SuperAdmin", "ModerationArchiveQueue.razor");
        var nav = Read("Ben.Web.Website", "Components", "Layout", "BenNav.razor");

        Assert.Contains("@page \"/moderation/archive\"", page);
        Assert.Contains("/moderation/archive", nav);
    }

    /// <summary>
    /// Approving has to be offered on a held row, because releasing is the whole reason the queue
    /// exists. A page that only ever offered Hold would satisfy every assertion above.
    /// </summary>
    [Fact]
    public void The_archive_queue_offers_approve_on_something_held()
    {
        var page = Read("Ben.Web.Website.Library", "SuperAdmin", "ModerationArchiveQueue.razor");

        Assert.Contains("approve: true", page);
        // Held rows are shown by default — the pile that was invisible is the pile somebody most
        // needs on arriving.
        Assert.Contains("_includeHeld = true", page);
    }
}
