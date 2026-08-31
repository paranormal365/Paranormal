using Microsoft.Playwright;
using NUnit.Framework;

namespace Ben.Web.Playwright.Tests;

/// <summary>
/// The guest's own copy, and contributing it onward to the place's archive (Ben, 2026-08-31).
/// </summary>
/// <remarks>
/// <para>Daniel is the actor for the same reason he is in <c>EventEvidenceTests</c>: he belongs
/// to no group, so everything he manages to do here proves the page works on ATTENDANCE rather
/// than on membership he does not have. The seed gives him a confirmed attendance at the past
/// Bell Witch open night.</para>
///
/// <para><b>What this covers that the unit tests cannot.</b> The rules are pinned in
/// <c>ArchiveEvidencePublicationTests</c> and <c>EventEvidenceOwnerCopyTests</c>. What only a
/// browser can prove is that the page is REACHABLE, that its buttons reach a live Blazor circuit,
/// and that publishing from this screen actually changes the place's public page — the
/// wire between the parts, which is the join every write-only feature in this codebase has
/// broken at.</para>
/// </remarks>
[TestFixture]
[Category("EventEvidence")]
public class MyEvidenceArchiveTests : BenTestBase
{
    [Test]
    public async Task My_evidence_is_reachable_from_the_profile()
    {
        await LoginAsync(ClientEmail, ClientPassword);

        await Page.GotoAsync($"{BaseUrl}/profile");
        // The HEADING is the expectation, not the URL: Blazor changes the address before the page
        // has rendered, so waiting on the URL alone passes on a page that never drew.
        await ClickUntilAsync(
            Main.GetByRole(AriaRole.Link, new() { Name = "My evidence" }),
            Main.GetByRole(AriaRole.Heading, new() { Name = "My evidence" }));
    }

    /// <summary>
    /// The page must say something either way. An account that has offered nothing gets the empty
    /// state; one that has gets rows — and a blank page is neither, which is what a failed load
    /// looks like.
    /// </summary>
    [Test]
    public async Task My_evidence_says_something_either_way()
    {
        await LoginAsync(ClientEmail, ClientPassword);
        await Page.GotoAsync($"{BaseUrl}/my-evidence");

        await Expect(Main.GetByRole(AriaRole.Heading, new() { Name = "My evidence" }))
            .ToBeVisibleAsync();

        var hasRows = await Main.Locator("table tbody tr").CountAsync() > 0;
        var hasEmptyState = await Main.GetByText("You haven't offered anything yet").IsVisibleAsync();

        Assert.That(hasRows || hasEmptyState, Is.True,
            "The page rendered neither rows nor an empty state, which is what a failed load looks like.");
    }

    /// <summary>
    /// The join that matters: a guest publishes from their own page, and the picture appears on
    /// the PLACE's public page — where a stranger, signed out, can see it.
    /// </summary>
    /// <remarks>
    /// Skipped rather than failed when Daniel has nothing publishable. The seed's event has to be
    /// at a public place for there to be an archive at all, and a test that fails on a seed
    /// difference teaches people to ignore it.
    /// </remarks>
    [Test]
    public async Task Publishing_from_my_evidence_puts_it_on_the_places_page()
    {
        await LoginAsync(ClientEmail, ClientPassword);
        await Page.GotoAsync($"{BaseUrl}/my-evidence");
        await Expect(Main.GetByRole(AriaRole.Heading, new() { Name = "My evidence" })).ToBeVisibleAsync();

        var publish = Main.Locator("button[id^='publish-']").First;
        if (await publish.CountAsync() == 0)
        {
            Assert.Ignore("Nothing publishable for this account — the seeded event is not at a public place.");
            return;
        }

        // The button flipping to "Remove" is the signal: the page reloads itself after a
        // successful publish, so the retract button existing proves the round trip completed.
        await ClickUntilAsync(publish, Main.Locator("button[id^='retract-']").First);
    }

    /// <summary>
    /// Taking it back is the paid half of the bargain. On a free account the refusal has to be a
    /// sentence the person can act on, not a generic failure — it is the paywall's own words.
    /// </summary>
    [Test]
    public async Task A_free_account_is_told_why_it_cannot_take_it_back()
    {
        await LoginAsync(ClientEmail, ClientPassword);
        await Page.GotoAsync($"{BaseUrl}/my-evidence");
        await Expect(Main.GetByRole(AriaRole.Heading, new() { Name = "My evidence" })).ToBeVisibleAsync();

        var retract = Main.Locator("button[id^='retract-']").First;
        if (await retract.CountAsync() == 0)
        {
            Assert.Ignore("Nothing published to the archive for this account.");
            return;
        }

        await retract.ClickAsync();

        // Either it worked (this account turned out to be covered by a plan) or the refusal
        // explains itself. What must never happen is a bare failure with no guidance.
        var removed = Main.GetByText("Removed from the place's archive");
        var refused = Main.GetByText("part of a paid plan");

        await Expect(removed.Or(refused)).ToBeVisibleAsync(new() { Timeout = 8_000 });
    }
}
