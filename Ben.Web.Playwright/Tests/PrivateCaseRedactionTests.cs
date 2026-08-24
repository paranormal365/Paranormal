using Microsoft.Playwright;
using NUnit.Framework;

namespace Ben.Web.Playwright.Tests;

/// <summary>
/// Item 184 Phase E: the public rendering of private-lane work, walked as a visitor.
/// </summary>
/// <remarks>
/// <para><b>Read-only by design</b> (the item-182/183 lesson: e2e must never permanently mutate
/// the shared database — cases cannot even be deleted). These walks assert against the seeded
/// cases: the pseudonymed Springfield case and the landmark Bell Witch case.</para>
///
/// <para>The substitution MECHANICS (the ladder, HTML safety, verbatim-when-not-private, the
/// plan gates) are pinned by 20+ unit and controller tests in Ben.Web.Tests; what a walk adds is
/// the wiring proof — that the real site, through the real website host, serves the substituted
/// copy to somebody signed out. The seeded prose contains no real client names, so the name
/// assertions here are invariants (the seeded client's name never appears) rather than
/// demonstrations; a future seed with named prose strengthens them automatically.</para>
/// </remarks>
[TestFixture]
[Category("PrivateCaseRedaction")]
public class PrivateCaseRedactionTests : BenTestBase
{
    // Seeded by DevelopmentDataSeeder: tgh #2026-001 carries pseudonym "The Hargrove Family";
    // the seeded client across the suite is Daniel Park (daniel.park@benco.dev).
    private const string TghUrl = "tgh";

    [Test]
    [Description("A pseudonymed case page shows the pseudonym and never the seeded client's real name.")]
    public async Task Pseudonymed_case_page_never_shows_a_real_name()
    {
        await Page.GotoAsync($"{BaseUrl}/o/{TghUrl}/cases/2026-001");
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);

        await Expect(Page.GetByText("The Hargrove Family").First)
            .ToBeVisibleAsync(new() { Timeout = 15_000 });

        var body = await Page.Locator("body").InnerTextAsync();
        Assert.That(body, Does.Not.Contain("Daniel Park"),
            "the seeded client's real name reached a public case page");
    }

    [Test]
    [Description("A public-place case renders its title exactly as written — Ben's scope rule.")]
    public async Task Landmark_case_renders_verbatim()
    {
        await Page.GotoAsync($"{BaseUrl}/o/{TghUrl}/cases/2026-002");
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);

        await Expect(Page.GetByText("Bell Witch Cave — Annual Survey").First)
            .ToBeVisibleAsync(new() { Timeout = 15_000 });
    }

    [Test]
    [Description("The home page's cross-org discovery serves without a real name.")]
    public async Task Discovery_list_carries_no_real_name()
    {
        await Page.GotoAsync($"{BaseUrl}/");
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);

        await Expect(Page.GetByText("Abandoned Springfield Farmhouse").First)
            .ToBeVisibleAsync(new() { Timeout = 15_000 });

        var body = await Page.Locator("body").InnerTextAsync();
        Assert.That(body, Does.Not.Contain("Daniel Park"));
    }
}
