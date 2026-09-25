using Microsoft.Playwright;
using NUnit.Framework;

namespace Ben.Web.Playwright.Tests;

/// <summary>
/// Former members are out of the Users list until somebody asks for them (Ben, 2026-09-22).
/// </summary>
/// <remarks>
/// <para><b>NOT YET RUN.</b> Written on 2026-09-22 while Playwright was on hold at Ben's request,
/// so it has never executed and has never been seen failing. Treat it as unverified until it has
/// done both — the rest of this suite earns its keep by being run against the un-fixed code first,
/// and this one has not had that.</para>
///
/// <para>What it is for: closing an account anonymises it and keeps the row, so every former
/// member stays in this table for ever as "A former member". The box is off by default, and the
/// count beside it exists so that a search finding nothing says why rather than reading as "that
/// person was never here".</para>
/// </remarks>
[TestFixture]
[Category("Admin")]
public class AdminUsersFormerMembersTests : BenTestBase
{
    private async Task OpenAsync()
    {
        await LoginAsync(SuperAdminEmail, SuperAdminPassword);
        await Page.GotoAsync($"{BaseUrl}/admin/users");
        await WaitForTheCircuitAsync();
        await WaitUntilLoadedAsync();
        await Expect(Main.Locator("#users-include-former")).ToBeVisibleAsync(new() { Timeout = 20_000 });
    }

    [Test]
    public async Task The_box_is_there_and_is_off_to_begin_with()
    {
        await OpenAsync();
        Assert.That(await Main.Locator("#users-include-former").IsCheckedAsync(), Is.False,
            "Former members were included before anybody asked for them.");
    }

    /// <summary>
    /// Ticking it can only ever add rows, and on this database it adds at least one.
    /// </summary>
    /// <remarks>
    /// Guarded by the hidden-count badge: if the seeded database holds no closed accounts there is
    /// nothing for this to prove, and the test says so rather than passing on an empty set.
    /// </remarks>
    [Test]
    public async Task Ticking_it_reveals_the_accounts_it_was_hiding()
    {
        await OpenAsync();

        var badge = Main.Locator("[data-testid='former-hidden-count']");
        if (await badge.CountAsync() == 0)
            Assert.Inconclusive("This database has no closed accounts, so there is nothing hidden "
                              + "to reveal. Seed one before trusting this test.");

        var before = await Main.Locator("tbody tr").CountAsync();

        await Main.Locator("#users-include-former").CheckAsync();
        await WaitUntilLoadedAsync();
        await Page.WaitForTimeoutAsync(500);

        Assert.That(await Main.Locator("tbody tr").CountAsync(), Is.GreaterThan(before),
            "Ticking the box did not add the rows the badge said were hidden.");

        // The badge is about what is hidden, so it goes when nothing is.
        Assert.That(await badge.CountAsync(), Is.Zero);
    }
}
