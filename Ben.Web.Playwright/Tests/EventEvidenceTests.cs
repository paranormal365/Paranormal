using Microsoft.Playwright;
using NUnit.Framework;

namespace Ben.Web.Playwright.Tests;

/// <summary>
/// Item 111 end to end: a stranger who attended offers a file, a member accepts it, and the
/// event's public record grows — all through real screens.
/// </summary>
/// <remarks>
/// <para>Daniel is the submitter on purpose: he belongs to no group, so every step he completes
/// proves the door opens on <i>attendance</i> rather than on any membership he does not have.
/// The seed gives him a confirmed attendance at the past Bell Witch open night.</para>
///
/// <para>The upload uses Playwright's file chooser — the one browser capability the sandboxed
/// Browser pane cannot drive, and the reason this flow gets an e2e test rather than a manual
/// note.</para>
/// </remarks>
[TestFixture]
[Category("EventEvidence")]
public class EventEvidenceTests : BenTestBase
{
    private const string EventTitle = "Bell Witch Cave — Last Month's Open Night";

    /// <summary>
    /// Chooses the file and waits for the submit button to ENABLE — proof the change event
    /// reached a live circuit.
    /// </summary>
    /// <remarks>
    /// Blazor Server renders the page long before its circuit attaches, and a file chosen (or a
    /// click landed) in that window is silently dropped — nothing throws, the page just does not
    /// advance. The button enables only when the server-side component has the file, so "button
    /// enabled" is the one signal that the choice actually registered. Retried, per the same
    /// reasoning as ClickUntilAsync.
    /// </remarks>
    private async Task ChooseEvidenceFileAsync(string path)
    {
        for (var attempt = 0; attempt < 5; attempt++)
        {
            await Main.Locator("#evidence-file").SetInputFilesAsync(path);
            try
            {
                await Expect(Main.Locator("#evidence-submit")).ToBeEnabledAsync(new() { Timeout = 3_000 });
                return;
            }
            catch (Exception) { /* dropped on a not-yet-live circuit; choose again */ }
        }
        Assert.Fail("The file choice never registered — the circuit did not come up.");
    }

    private async Task<bool> OpenPastEventAsync()
    {
        // The slug embeds the SEED date, not "today minus thirty": recomputing it here drifted one day
        // per day after seeding. It is read from the group's calendar as its admin, which lists past
        // dates too. The public events list was asked before 10/27/2026, which only lists events still
        // to come and asked for the group's old address ("tgh"), so both tests skipped on every run.
        var orgId = await OrgIdBySlugAsync("paranormal365");
        await using var api = await ApiSignedInAsync(SuperAdminEmail, SuperAdminPassword);
        var calendar = await api.GetAsync($"/api/organizations/{orgId}/calendar");
        if (!calendar.Ok) return false;

        string? slug = null;
        foreach (var e in (await calendar.JsonAsync())!.Value.EnumerateArray())
        {
            if (e.GetProperty("title").GetString()?.Contains(EventTitle) == true
                && e.TryGetProperty("urlName", out var url))
            { slug = url.GetString(); break; }
        }
        if (slug is null) return false;

        await Page.GotoAsync($"{BaseUrl}/o/paranormal365/events/{slug}");
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);

        return (await Page.GetByText(EventTitle, new() { Exact = false }).CountAsync()) > 0;
    }

    [Test]
    public async Task An_attendee_can_offer_evidence_and_is_told_the_record_is_public()
    {
        await LoginAsync(ClientEmail, ClientPassword);
        if (!await OpenPastEventAsync()) Assert.Ignore("The seeded past event is not in this database.");

        // The bargain is stated BEFORE the button — the sentence itself is the assertion.
        await Expect(Main.GetByText("public", new() { Exact = false }).First)
            .ToBeVisibleAsync(new() { Timeout = 10_000 });
        await Expect(Main.Locator("#evidence-submit")).ToBeVisibleAsync(new() { Timeout = 10_000 });

        // Counted BEFORE, so the assertion is "one more than there was" — the seed and earlier
        // runs leave Daniel with pending submissions, and asserting mere visibility was satisfied
        // by an old badge while the new submit silently failed. A test that cannot fail is worse
        // than none.
        var before = await Main.GetByText("Waiting for review", new() { Exact = false }).CountAsync();

        // A real file through the real chooser — retried until the circuit provably has it.
        // The completion signal is the FILE NAME: "Your submissions" renders the name and the
        // status badge, not the note — an earlier version waited on the note text, which never
        // appears there, and burned its whole retry budget against a submit that had worked.
        var wav = Path.Combine(Path.GetTempPath(), $"whisper-{Guid.NewGuid():N}.wav");
        await File.WriteAllBytesAsync(wav, [82, 73, 70, 70, 4, 0, 0, 0]);
        await ChooseEvidenceFileAsync(wav);
        await Main.Locator("#evidence-note").FillAsync("Caught near the cave mouth, around 10pm.");

        await ClickUntilAsync(
            Main.Locator("#evidence-submit"),
            Main.GetByText(Path.GetFileName(wav), new() { Exact = false }));

        var after = await Main.GetByText("Waiting for review", new() { Exact = false }).CountAsync();
        Assert.That(after, Is.EqualTo(before + 1), "The submission did not appear as pending.");
    }

    [Test]
    public async Task A_member_reviews_it_and_acceptance_reaches_the_public_page()
    {
        // Arrange half: ensure a pending submission exists (the previous test usually has, but
        // each test must stand alone).
        await LoginAsync(ClientEmail, ClientPassword);
        if (!await OpenPastEventAsync()) Assert.Ignore("The seeded past event is not in this database.");

        var wav = Path.Combine(Path.GetTempPath(), $"knock-{Guid.NewGuid():N}.wav");
        await File.WriteAllBytesAsync(wav, [82, 73, 70, 70, 4, 0, 0, 0]);
        var marker = $"Three knocks {Guid.NewGuid():N}"[..24];

        // File-choice and click both retried until their effects are VISIBLE, and completion is
        // awaited BEFORE LoginAsync tears the circuit down: an upload on Blazor Server is not
        // done when the click returns, it is done when the page says so. The signal is the FILE
        // NAME in "Your submissions" — the note never renders there; the note (the marker) is
        // what the REVIEW QUEUE shows, which is where it is asserted after the login switch.
        await ChooseEvidenceFileAsync(wav);
        await Main.Locator("#evidence-note").FillAsync(marker);
        await ClickUntilAsync(
            Main.Locator("#evidence-submit"),
            Main.GetByText(Path.GetFileName(wav), new() { Exact = false }));

        // A member without the Calendar grant: the tab is theirs, the queue is not, and the page
        // says nothing rather than showing them a refusal. Until 10/27/2026 this seat did the
        // reviewing, met a 403 the page drew as an empty queue, and the test never got further.
        await LoginAsync(MemberEmail, MemberPassword);
        if (!await OpenOrganizationAsync("Paranormal365"))
            Assert.Ignore("No Paranormal365 in this database.");
        await OrgTab("Calendar").ClickAsync();
        await Expect(Main.Locator(".k-scheduler")).ToBeVisibleAsync(new() { Timeout = 20_000 });
        await Page.WaitForTimeoutAsync(1_500);   // the queue loads beside the calendar, not before it
        await Expect(Main.Locator("[data-testid=evidence-queue-failed]")).ToHaveCountAsync(0);

        // The reviewer's half: the group's owner, who holds the grant, on the Calendar tab.
        await LoginAsync(UserEmail, UserPassword);
        if (!await OpenOrganizationAsync("Paranormal365"))
            Assert.Ignore("No Paranormal365 in this database.");

        await OrgTab("Calendar").ClickAsync();

        try
        {
            await Expect(Main.GetByText(marker, new() { Exact = false }).First)
                .ToBeVisibleAsync(new() { Timeout = 15_000 });
        }
        catch (Exception)
        {
            // The submit button is on the event page, not this one; asking for it here timed out
            // and hid the real reason (site audit, 10/27/2026).
            var alerts = string.Join(" | ", await Main.Locator(".alert").AllInnerTextsAsync());
            var queue = await Main.GetByText("Evidence from your public events").CountAsync() > 0 ? "shown" : "not shown";
            Assert.Fail($"The member's queue never listed the submission. Queue card: {queue}. On-page alerts: [{alerts}]");
        }

        // This submission's own row: the seed leaves another pending one in the same queue, and
        // the first Accept on the card was sometimes that one (site audit, 10/27/2026).
        // Accepted, it leaves the queue, or stays with an "Accepted" badge while its clock runs.
        var mine = Main.Locator("div.border-bottom", new() { HasTextString = marker });
        var accept = mine.GetByRole(AriaRole.Button, new() { Name = "Accept", Exact = true });
        await accept.Last.ClickAsync();
        for (var waited = 0; await accept.CountAsync() > 0 && await Main.Locator(".alert-danger").CountAsync() == 0; waited += 250)
        {
            Assert.That(waited, Is.LessThan(15_000), "Accept was clicked and the submission is still waiting.");
            await Page.WaitForTimeoutAsync(250);
        }
        Assert.That(await Main.Locator(".alert-danger").CountAsync(), Is.Zero,
            "Accepting was refused: " + string.Join(" | ", await Main.Locator(".alert-danger").AllInnerTextsAsync()));

        // The public record, read the way the world reads it: signed out.
        await Page.Context.ClearCookiesAsync();
        await Page.GotoAsync($"{BaseUrl}/logout").ContinueWith(_ => Task.CompletedTask);
        if (!await OpenPastEventAsync()) Assert.Ignore("Event page unavailable anonymously.");

        await Expect(Main.Locator("[data-testid=event-evidence-list]"))
            .ToBeVisibleAsync(new() { Timeout = 10_000 });
    }
}
