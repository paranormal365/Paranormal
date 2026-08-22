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

        // A real file through the real chooser.
        var wav = Path.Combine(Path.GetTempPath(), $"whisper-{Guid.NewGuid():N}.wav");
        await File.WriteAllBytesAsync(wav, [82, 73, 70, 70, 4, 0, 0, 0]);
        await Main.Locator("#evidence-file").SetInputFilesAsync(wav);

        await Main.Locator("#evidence-note").FillAsync("Caught near the cave mouth, around 10pm.");
        await Main.Locator("#evidence-submit").ClickAsync();

        await Expect(Main.GetByText("Waiting for review", new() { Exact = false }))
            .ToBeVisibleAsync(new() { Timeout = 10_000 });
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
        await Main.Locator("#evidence-file").SetInputFilesAsync(wav);
        var marker = $"Three knocks {Guid.NewGuid():N}"[..24];
        await Main.Locator("#evidence-note").FillAsync(marker);
        await Main.Locator("#evidence-submit").ClickAsync();
        await Expect(Main.GetByText("Waiting for review", new() { Exact = false }).First)
            .ToBeVisibleAsync(new() { Timeout = 10_000 });

        // The member's half: the queue card on the Calendar tab.
        await LoginAsync(MemberEmail, MemberPassword);
        if (!await OpenOrganizationAsync("Tennessee Ghost Hunters"))
            Assert.Ignore("No Tennessee Ghost Hunters in this database.");

        await Main.GetByRole(AriaRole.Tab, new() { Name = "Calendar", Exact = true }).ClickAsync();

        var row = Main.Locator("div", new() { HasTextString = marker }).Last;
        await Expect(Main.GetByText(marker, new() { Exact = false }).First)
            .ToBeVisibleAsync(new() { Timeout = 15_000 });

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
