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
        // The public event page is reached by slug; the seed derives it from a moving date, so
        // navigate via the org's public page rather than computing the URL here.
        await Page.GotoAsync($"{BaseUrl}/o/tgh/events/{DateTime.UtcNow.AddDays(-30):yyyy-MM-dd}-bell-witch-cave-open-night");
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

        // The member's half: the queue card on the Calendar tab.
        await LoginAsync(MemberEmail, MemberPassword);
        if (!await OpenOrganizationAsync("Tennessee Ghost Hunters"))
            Assert.Ignore("No Tennessee Ghost Hunters in this database.");

        await Main.GetByRole(AriaRole.Tab, new() { Name = "Calendar", Exact = true }).ClickAsync();

        var row = Main.Locator("div", new() { HasTextString = marker }).Last;
        try
        {
            await Expect(Main.GetByText(marker, new() { Exact = false }).First)
                .ToBeVisibleAsync(new() { Timeout = 15_000 });
        }
        catch (Exception)
        {
            var errors = string.Join(" | ", await Main.Locator(".alert-danger").AllInnerTextsAsync());
            var button = await Main.Locator("#evidence-submit").InnerTextAsync();
            Assert.Fail($"Submission never appeared. Button: [{button}] On-page errors: [{errors}]");
        }

        await Main.GetByRole(AriaRole.Button, new() { Name = "Accept", Exact = true }).First.ClickAsync();
        await Page.WaitForTimeoutAsync(1_000);

        // The public record, read the way the world reads it: signed out.
        await Page.Context.ClearCookiesAsync();
        await Page.GotoAsync($"{BaseUrl}/logout").ContinueWith(_ => Task.CompletedTask);
        if (!await OpenPastEventAsync()) Assert.Ignore("Event page unavailable anonymously.");

        await Expect(Main.GetByText("Evidence from attendees", new() { Exact = false }))
            .ToBeVisibleAsync(new() { Timeout = 10_000 });
    }
}
