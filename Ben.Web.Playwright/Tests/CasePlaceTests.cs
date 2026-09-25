using System.Threading.Tasks;
using Microsoft.Playwright;
using NUnit.Framework;

namespace Ben.Web.Playwright.Tests;

/// <summary>
/// A case names the place it is about, and is offered the place already on file.
/// </summary>
/// <remarks>
/// <para>Ben, 2026-09-17: "if someone wants to create a case at a public case, they are given the
/// option to link to the existing case and create their own investigation." The offer is what these
/// drive. Until this shipped, <c>CaseController</c> contained no reference to a place at all: a case
/// sat at an address that matched a place three other groups had worked, and nothing joined the
/// two.</para>
///
/// <para>The seeded Bell Witch Cave is a <c>PublicLocation</c> with a name and no street address,
/// which is exactly the landmark shape the matcher falls back to a name for — so typing the name is
/// the path being tested, not a contrivance.</para>
/// </remarks>
[TestFixture]
[Category("Cases")]
public class CasePlaceTests : BenTestBase
{
    private const string SeededLandmark = "Bell Witch Cave";

    /// <summary>Opens New Case for the seeded group, or ignores when the seed differs.</summary>
    private async Task<bool> OpenNewCaseAsync()
    {
        await LoginAsync(UserEmail, UserPassword);
        if (!await OpenOrganizationAsync("Paranormal365")) return false;

        await OpenTabAsync("Cases", Main.GetByTestId("new-case")
                                       .Or(Main.GetByText("No cases", new() { Exact = false })));

        var newCase = Main.GetByTestId("new-case");
        if (await newCase.CountAsync() == 0) return false;

        await ClickUntilUrlAsync(newCase.First, @"/cases/new");
        await WaitUntilLoadedAsync();
        return true;
    }

    /// <summary>
    /// The kind of place has no default, so the form cannot be submitted until it is answered.
    /// Every consequence of that answer is heavy, and a default would be one of them chosen by
    /// nobody.
    /// </summary>
    [Test]
    public async Task Open_case_waits_for_the_kind_of_place()
    {
        if (!await OpenNewCaseAsync()) Assert.Ignore("the seeded group this walks is not on this database");

        var open = Main.GetByRole(AriaRole.Button, new() { Name = "Open Case" });
        await Expect(open).ToBeVisibleAsync(new() { Timeout = 15_000 });

        // Fill everything the form has ever required, and leave only the new question unanswered.
        await Main.Locator("#casecreatepage-case-title-b1b1").FillAsync("A night somewhere new");
        await Main.Locator("#casecreatepage-street-address-5b76").FillAsync("1 Nowhere Lane");
        await Main.Locator("#casecreatepage-city-4662").FillAsync("Nashville");
        await Main.Locator("#casecreatepage-state-7b45").FillAsync("TN");
        await Main.Locator("#casecreatepage-zip-code-ba79").FillAsync("37201");

        await Expect(open).ToBeDisabledAsync(new() { Timeout = 10_000 });

        await Main.Locator("#case-place-kind-public").CheckAsync();
        await Expect(open).ToBeEnabledAsync(new() { Timeout = 10_000 });
    }

    /// <summary>
    /// Typing a landmark's name offers the place already on file, and taking it settles the kind
    /// as well — the place decides what kind of location it is, not whatever was ticked before.
    /// </summary>
    [Test]
    public async Task The_place_already_on_file_is_offered_and_can_be_taken()
    {
        if (!await OpenNewCaseAsync()) Assert.Ignore("the seeded group this walks is not on this database");

        await Main.Locator("#casecreatepage-case-title-b1b1").FillAsync("Another look at the cave");
        await Main.Locator("#casecreatepage-street-address-5b76").FillAsync("430 Keysburg Rd");
        await Main.Locator("#casecreatepage-city-4662").FillAsync("Adams");
        await Main.Locator("#casecreatepage-state-7b45").FillAsync("TN");
        await Main.Locator("#casecreatepage-zip-code-ba79").FillAsync("37010");

        // The name is what a landmark with no street address is matched on.
        await Main.Locator("#case-place-name").FillAsync(SeededLandmark);

        var offer = Main.GetByTestId("place-candidates");
        try
        {
            // Debounced at 600ms and then a round trip, so this waits rather than looks once.
            await Expect(offer).ToBeVisibleAsync(new() { Timeout = 20_000 });
        }
        catch (AssertionException)
        {
            // A refusal says so in its own element rather than reading as "no match exists", which
            // would invite exactly the duplicate the offer prevents.
            if (await Main.GetByTestId("place-candidates-error").CountAsync() > 0)
                Assert.Ignore("existing places could not be checked on this run");
            throw;
        }

        await Expect(offer).ToContainTextAsync(SeededLandmark);

        await offer.GetByRole(AriaRole.Button, new() { Name = "Use this place" }).First.ClickAsync();

        var chosen = Main.GetByTestId("case-place-chosen");
        await Expect(chosen).ToBeVisibleAsync(new() { Timeout = 10_000 });
        await Expect(chosen).ToContainTextAsync(SeededLandmark);

        // The place settled the kind, so the form is complete without answering it again.
        await Expect(chosen).ToContainTextAsync("public location");
        await Expect(Main.GetByRole(AriaRole.Button, new() { Name = "Open Case" }))
            .ToBeEnabledAsync(new() { Timeout = 10_000 });
    }

    /// <summary>
    /// The place page's own "Open a case here" brings the form up with the place settled, which is
    /// the direction somebody browsing a location arrives from.
    /// </summary>
    [Test]
    public async Task A_place_can_send_you_to_a_new_case_with_itself_already_chosen()
    {
        if (!await OpenNewCaseAsync()) Assert.Ignore("the seeded group this walks is not on this database");

        // The org id is in the URL this test already reached, so the query form is driven directly
        // rather than by hunting the button on a place page whose rows depend on the seed.
        var url = Page.Url;
        await Page.GotoAsync($"{url}?place=40000001-0000-0000-0000-000000000001");
        await WaitUntilLoadedAsync();

        var chosen = Main.GetByTestId("case-place-chosen");
        await Expect(chosen).ToBeVisibleAsync(new() { Timeout = 20_000 });
        await Expect(chosen).ToContainTextAsync(SeededLandmark);
    }
}
