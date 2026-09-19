using Microsoft.Playwright;
using NUnit.Framework;

namespace Ben.Web.Playwright.Tests;

/// <summary>
/// The group's finding, on the page a visitor reads (site evaluation 2026-09-06, W-P3).
/// </summary>
/// <remarks>
/// <para>A published report used to exist only on the client's own case page: the group wrote it,
/// the client read it, and nothing on the group's public site could carry any of it.</para>
///
/// <para><b>Signed out throughout.</b> The point is what a stranger sees, and a signed-in author
/// sees things a visitor does not — the lesson item 176 left behind. The seeded data carries
/// exactly one case with a finding switched on and several without, so both answers are checked
/// against real pages rather than one being assumed.</para>
/// </remarks>
[TestFixture]
[Category("PublicCaseReport")]
public class PublicCaseReportTests : BenTestBase
{
    /// <summary>The seeded case whose group switched its report on.</summary>
    private const string CaseWithAFinding = "/o/paranormal365/cases/2026-002";

    /// <summary>A seeded public case with no report at all — which is every other case.</summary>
    private const string CaseWithout = "/o/nps/cases/2026-001";

    [Test]
    public async Task A_visitor_reads_the_groups_finding_on_the_public_case_page()
    {
        await LogoutAsync();
        await Page.GotoAsync($"{BaseUrl}{CaseWithAFinding}");
        await WaitUntilLoadedAsync();

        // Waited for, not counted. Counting an h1 the instant the navigation returns answers
        // "not yet" and reads as "no such case" — a skip that hides whatever the page actually
        // did. If the case is genuinely missing this fails, which is the honest answer.
        await Expect(Page.Locator("h1")).ToBeVisibleAsync(new() { Timeout = 20_000 });

        var section = Page.Locator("#public-case-report");
        await Expect(section).ToBeVisibleAsync(new() { Timeout = 20_000 });

        // The heading a visitor reads, and both halves of what was released.
        await Expect(section).ToContainTextAsync("What we found");
        await Expect(section).ToContainTextAsync("Conclusion");

        // And nothing beyond the two fields: no section titles, no evidence, no field sessions.
        var body = await section.InnerTextAsync();
        Assert.That(body, Does.Not.Contain("Field Session"));
        Assert.That(body, Does.Not.Contain("Evidence"));
    }

    [Test]
    public async Task A_case_whose_group_switched_nothing_on_shows_no_finding()
    {
        // The state every case is in until somebody chooses, so this is the important half.
        await LogoutAsync();
        await Page.GotoAsync($"{BaseUrl}{CaseWithout}");
        await WaitUntilLoadedAsync();

        // The page first — an absent section must not be confused with an absent page, which is
        // exactly what an early count would have reported.
        await Expect(Page.Locator("h1")).ToBeVisibleAsync(new() { Timeout = 20_000 });

        // Server-decided: nothing is sent at all, so the section is absent rather than empty.
        await Expect(Page.Locator("#public-case-report")).ToHaveCountAsync(0, new() { Timeout = 10_000 });
    }

    [Test]
    public async Task The_finding_is_not_on_the_public_case_list_only_on_the_case()
    {
        // The list is a directory. Putting a conclusion on every card would make it a wall of
        // prose, and the summary belongs with the case it concludes.
        await LogoutAsync();
        await Page.GotoAsync($"{BaseUrl}/o/paranormal365/cases");
        await WaitUntilLoadedAsync();

        await Expect(Page.Locator("#public-case-report")).ToHaveCountAsync(0);
    }
}
