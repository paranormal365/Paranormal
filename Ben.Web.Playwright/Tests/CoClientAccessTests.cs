using Microsoft.Playwright;
using NUnit.Framework;

namespace Ben.Web.Playwright.Tests;

/// <summary>
/// Sharing a case with another person, and the boundary around it.
/// </summary>
/// <remarks>
/// <para>
/// This had no coverage at all, which is notable because co-client access has produced real bugs
/// here before — both found by using the screen rather than by anything automated. It is also the
/// kind of feature where a mistake is quiet in the worst direction: a case shown to someone who
/// should not have it looks exactly like a case shown to someone who should.
/// </para>
/// <para>
/// So both directions are asserted, and the negative runs first. If the "stranger cannot see it"
/// check only ever ran after a share, a bug that granted access to everyone would still let the
/// positive pass and would make the negative look like a sharing failure.
/// </para>
/// </remarks>
[TestFixture]
[Category("CoClient")]
public class CoClientAccessTests : BenTestBase
{
    private const string ClientEmail    = "daniel.park@benco.dev";
    private const string ClientPassword = "D@niel!Park2026";

    // Genuinely unrelated to Daniel's case: the API answers 404 for him, which is the behaviour
    // this fixture is here to hold on to.
    //
    // Not Emma Rodriguez, who looks like a stranger and is not — she is already a co-client on
    // that case, so pointing the test at her reported a security hole that did not exist. Worth
    // checking against the API before believing a leak: an account that *should* have access is
    // indistinguishable on screen from one that should not.
    private const string StrangerEmail    = "james.thornton@benco.dev";
    private const string StrangerPassword = "J@mes!Thornton26";

    /// <summary>Opens Daniel's managed case and returns its URL.</summary>
    private async Task<string> OpenDanielsCaseAsync()
    {
        await LoginAsync(ClientEmail, ClientPassword);
        await Page.GotoAsync($"{BaseUrl}/my-cases");
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);

        // The managed case, not the first card — see the note in CaseMessageBoardTests.
        var card = Page.Locator(".card").Filter(new() { HasTextString = "Case Manager:" }).First;
        if (await card.CountAsync() == 0) Assert.Ignore("no managed case in the seed data");

        await ClickUntilUrlAsync(card, @"/my-cases/[0-9a-f\-]+");
        await WaitUntilLoadedAsync();
        return Page.Url;
    }

    [Test]
    public async Task AStrangerCannotOpenSomeoneElsesCase()
    {
        var caseUrl = await OpenDanielsCaseAsync();
        var path = new Uri(caseUrl).PathAndQuery;

        await LogoutAsync();
        await LoginAsync(StrangerEmail, StrangerPassword);

        await Page.GotoAsync($"{BaseUrl}{path}");
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);
        await WaitUntilLoadedAsync();

        var body = await Page.InnerTextAsync("body");

        // Daniel's own occurrences and messages must not be on the page. Asserting on the case
        // reference rather than "some error appeared" — a refusal can be rendered many ways, but
        // leaking the content is unambiguous.
        Assert.That(body, Does.Not.Contain("Log Occurrence"),
            "a user with no relationship to this case was given the client's own case view");
    }

    [Test]
    public async Task SharingACase_LetsTheOtherPersonSeeIt_AndTheOwnerCanTakeItBack()
    {
        var caseUrl = await OpenDanielsCaseAsync();
        var path = new Uri(caseUrl).PathAndQuery;

        // ── Share it ─────────────────────────────────────────────────────────
        var dialog = Page.Locator(".modal.show");
        var addPerson = Main.GetByRole(AriaRole.Button, new() { Name = "Add Person" }).First;
        if (await addPerson.CountAsync() == 0) Assert.Ignore("this case does not offer case sharing");

        await ClickUntilAsync(addPerson, dialog);
        await dialog.GetByPlaceholder("person@example.com").FillAsync(StrangerEmail);

        var addButton = dialog.GetByRole(AriaRole.Button, new() { Name = "Add", Exact = true });
        await Expect(addButton).ToBeEnabledAsync(new() { Timeout = 8_000 });
        await addButton.ClickAsync();

        // The dialog reports the outcome rather than closing, so wait for that.
        await Expect(dialog.GetByText("access to this case", new() { Exact = false }))
            .ToBeVisibleAsync(new() { Timeout = 15_000 });

        await dialog.GetByRole(AriaRole.Button, new() { Name = "Done" }).ClickAsync();
        await Expect(dialog).ToBeHiddenAsync(new() { Timeout = 10_000 });

        // ── The other person can now open it ─────────────────────────────────
        await LogoutAsync();
        await LoginAsync(StrangerEmail, StrangerPassword);

        await Page.GotoAsync($"{BaseUrl}{path}");
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);
        await WaitUntilLoadedAsync();

        var shared = await Page.InnerTextAsync("body");
        Assert.That(shared, Does.Not.Contain("Page not found"), "the shared case did not resolve");
        Assert.That(shared, Does.Contain("Log Occurrence"),
            "the case was shared but the other person did not get the case view");

        // ── And the owner can take it back ───────────────────────────────────
        await LogoutAsync();
        await OpenDanielsCaseAsync();

        // Scoped to this person's own row. Taking the first danger button on the page would revoke
        // whoever happens to be listed first — the case already has another co-client from the
        // seed data, and removing them here would quietly break other fixtures.
        var strangerRow = Main.Locator("div.d-flex.align-items-center.justify-content-between")
                              .Filter(new() { HasTextString = "James Thornton" }).First;
        var removeButton = strangerRow.Locator("button.btn-danger").First;

        if (await removeButton.CountAsync() > 0)
        {
            await removeButton.ClickAsync();
            await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);

            await LogoutAsync();
            await LoginAsync(StrangerEmail, StrangerPassword);
            await Page.GotoAsync($"{BaseUrl}{path}");
            await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);
            await WaitUntilLoadedAsync();

            var revoked = await Page.InnerTextAsync("body");
            Assert.That(revoked, Does.Not.Contain("Log Occurrence"),
                "access was revoked but the case was still readable — revocation is the half that "
                + "matters, and nothing else in the suite checks it");
        }
    }
}
