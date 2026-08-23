using Microsoft.Playwright;
using NUnit.Framework;

namespace Ben.Web.Playwright.Tests;

/// <summary>
/// Item 156 Phase F: the four-seat pass — owner, administrator, member, viewer — as a fixture
/// instead of a memory. Each seat signs in and the test asserts the group hub shows exactly
/// the surfaces that seat is owed: admin seats get the management tabs, a role-holding member
/// gets the tabs their role opens and none of the management ones, and a viewer gets the
/// member baseline and nothing else. One assertion per surface, from the seat that owns it —
/// the whole lesson of the test-as-an-ordinary-member rule is that a check made only from the
/// privileged chair proves nothing about any other chair.
/// </summary>
[TestFixture]
[NonParallelizable]
[Category("FourSeatSmoke")]
public class FourSeatSmokeTests : BenTestBase
{
    private const string TghId  = "881ea0f6-8c0d-475e-9065-c6ed15e3302f"; // Tennessee Ghost Hunters
    private const string McssId = "50000001-0000-0000-0000-000000000001"; // Music City Spirit Seekers

    /// <summary>
    /// Emma — owner of Music City Spirit Seekers and nobody's administrator app-wide: the only
    /// seeded person for whom the Owner membership tier is exercised separately from the
    /// SuperAdmin app role. Her password lives only in the gitignored dev configuration, so it
    /// arrives by environment variable and the owner test says loudly when it is missing.
    /// </summary>
    private static string OwnerEmail    => Environment.GetEnvironmentVariable("BEN_OWNER_EMAIL") ?? "emma.rodriguez@benco.dev";
    private static string? OwnerPassword => Environment.GetEnvironmentVariable("BEN_OWNER_PASSWORD");

    [Test]
    public async Task The_owner_seat_gets_the_management_surfaces()
    {
        if (string.IsNullOrEmpty(OwnerPassword))
            Assert.Ignore("Set BEN_OWNER_PASSWORD (Emma's dev password, from appsettings.Development.json) to run the owner seat.");

        await LoginAsync(OwnerEmail, OwnerPassword!);
        await GotoOrgAsync(McssId);

        foreach (var tab in new[] { "Details", "Members", "Roles", "Settings" })
            await Expect(Tab(tab)).ToBeVisibleAsync(new() { Timeout = 45_000 });
    }

    [Test]
    public async Task The_administrator_seat_gets_the_management_surfaces()
    {
        await LoginAsync(UserEmail, UserPassword);   // Sarah — Administrator of TGH
        await GotoOrgAsync(TghId);

        foreach (var tab in new[] { "Details", "Members", "Cases", "Investigations", "Roles", "Settings" })
            await Expect(Tab(tab)).ToBeVisibleAsync(new() { Timeout = 45_000 });
    }

    [Test]
    public async Task The_member_seat_gets_its_roles_doors_and_no_management()
    {
        await LoginAsync(MemberEmail, MemberPassword);   // James — Member with the Investigator Role
        await GotoOrgAsync(TghId);

        foreach (var tab in new[] { "Details", "Members", "Cases", "Investigations", "Calendar", "Messages", "Files" })
            await Expect(Tab(tab)).ToBeVisibleAsync(new() { Timeout = 45_000 });
        await Expect(Tab("Roles")).ToHaveCountAsync(0);
        await Expect(Tab("Settings")).ToHaveCountAsync(0);
    }

    [Test]
    public async Task The_viewer_seat_gets_the_baseline_and_nothing_else()
    {
        await LoginAsync(ViewerEmail, ViewerPassword);   // Victor — Viewer, no roles
        await GotoOrgAsync(TghId);

        foreach (var tab in new[] { "Details", "Members", "Calendar", "Messages", "Files" })
            await Expect(Tab(tab)).ToBeVisibleAsync(new() { Timeout = 45_000 });
        foreach (var tab in new[] { "Cases", "Investigations", "Roles", "Settings" })
            await Expect(Tab(tab)).ToHaveCountAsync(0);
    }

    private ILocator Tab(string name)
        => Main.GetByRole(AriaRole.Tab, new() { Name = name, Exact = true });

    private async Task GotoOrgAsync(string orgId)
    {
        await Page.GotoAsync($"{BaseUrl}/organizations/{orgId}?tab=details");
        await WaitUntilLoadedAsync();
        await Expect(Tab("Details")).ToBeVisibleAsync(new() { Timeout = 45_000 });
    }
}
