using Xunit;

namespace Ben.Web.Tests.Website;

/// <summary>
/// The storage figure the server can answer is actually shown to somebody.
/// </summary>
/// <remarks>
/// <para><b>What the 2026-09-17 audit found.</b> <c>GET api/field-sessions/my-storage</c> answers
/// how much of a personal account's 2 GB is used, and sends a <b>null cap</b> for anybody a
/// group's paid plan covers — because "a figure they are not measured against would be a lie
/// however carefully it were labelled". Nothing called it. So a solo investigator got no usage
/// figure and no warning; the first they knew of the cap was an upload being refused.</para>
///
/// <para><b>And the phone told them the wrong thing.</b>
/// <c>Ben.iOS/BenKit/Sources/BenKit/Field/SessionTrim.swift</c> carries the number hardcoded from
/// a comment — "A personal account holds 2 GB (AccountStorageGuard)" — so it is also wrong for a
/// covered member, for whom the real answer is uncapped. The app is in App Review, so that half is
/// deliberately left for the next build; this guard covers the website.</para>
///
/// <para><b>Why a source scan.</b> The endpoint worked when called. What was broken was that
/// nothing called it, which no controller test can fail for.</para>
/// </remarks>
public sealed class StorageFigureHasAReaderTests
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

    [Fact]
    public void The_route_has_a_client()
        => Assert.Contains(
            "/api/field-sessions/my-storage",
            Read("Ben.Web.Services", "WebApi", "BenAdminClientAdapter.Platform.cs"));

    [Fact]
    public void A_page_asks_for_it()
        => Assert.Contains(
            "GetMyStorageAsync",
            Read("Ben.Web.Website.Library", "Kit", "MyFieldSessions.razor"));

    /// <summary>
    /// And renders it. A page that fetched the figure and drew nothing would satisfy the
    /// assertion above — which is a fair description of how several of this audit's findings
    /// came about.
    /// </summary>
    [Fact]
    public void The_page_renders_the_figure()
    {
        var page = Read("Ben.Web.Website.Library", "Kit", "MyFieldSessions.razor");

        Assert.Contains("my-storage", page);
        Assert.Contains("UsedBytes", page);
    }

    /// <summary>
    /// A covered member is told they are uncapped rather than shown a cap that is not theirs.
    /// The null is the whole point of the endpoint's design, so a page that printed a number for
    /// it would reintroduce the lie the phone currently tells.
    /// </summary>
    [Fact]
    public void A_covered_member_is_told_there_is_no_personal_cap()
    {
        var page = Read("Ben.Web.Website.Library", "Kit", "MyFieldSessions.razor");

        // The null-cap branch exists...
        Assert.Contains("CapBytes is { } cap", page);
        // ...and says so in words rather than falling through to a number.
        Assert.Contains("no personal cap", page);
    }
}
