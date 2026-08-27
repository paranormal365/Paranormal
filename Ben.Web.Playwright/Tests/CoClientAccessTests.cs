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

    // Genuinely unrelated to Daniel's case: the API answers 404 for him, which is the behaviour
    // this fixture is here to hold on to.
    //
    // Not Emma Rodriguez, who looks like a stranger and is not — she is already a co-client on
    // that case, so pointing the test at her reported a security hole that did not exist. Worth
    // checking against the API before believing a leak: an account that *should* have access is
    // indistinguishable on screen from one that should not.
    // The "stranger" here is the ordinary-member seat wearing a different hat: James has an
    // account and belongs to a group, and is simply nothing to do with this case. That is the
    // interesting negative — not somebody with no account at all.
    private static string StrangerEmail    => MemberEmail;
    private static string StrangerPassword => MemberPassword;

    /// <summary>
    /// Opens a case of Daniel's that the stranger genuinely cannot read, and returns its URL.
    /// </summary>
    /// <remarks>
    /// <para>The premise is CHECKED against the API rather than assumed. This fixture has now
    /// been broken twice from opposite directions — once by picking a person who turned out to be
    /// a co-client, and once by the seed drifting until Daniel's first listed case was one the
    /// stranger shares. Both times the result was a test reporting a security hole that did not
    /// exist, which is worse than no test: it sends somebody hunting through the access rules
    /// looking for a bug that was never there.</para>
    ///
    /// <para>So the case is chosen by asking: which of Daniel's cases does the API refuse this
    /// stranger? If none does, the seed cannot support the test and it says so plainly instead of
    /// failing.</para>
    /// </remarks>
    private async Task<string> OpenDanielsCaseAsync()
    {
        var api = await Playwright.APIRequest.NewContextAsync(new() { BaseURL = ApiUrl });

        var danielToken = await TokenAsync(api, ClientEmail, ClientPassword);
        var strangerToken = await TokenAsync(api, StrangerEmail, StrangerPassword);
        Assert.That(danielToken, Is.Not.Null, "the client seat should be able to sign in");
        Assert.That(strangerToken, Is.Not.Null, "the stranger seat should be able to sign in");

        var cases = await api.GetAsync("/api/my-cases", new()
        {
            Headers = new Dictionary<string, string> { ["Authorization"] = $"Bearer {danielToken}" },
        });
        Assert.That(cases.Ok, Is.True, "the client's own case list should load");

        string? chosen = null;
        foreach (var element in (await cases.JsonAsync())!.Value.EnumerateArray())
        {
            var id = element.GetProperty("caseId").GetString();
            if (id is null) continue;

            var probe = await api.GetAsync($"/api/my-cases/{id}", new()
            {
                Headers = new Dictionary<string, string>
                    { ["Authorization"] = $"Bearer {strangerToken}" },
            });
            // 404 is the API saying "not yours" — which is exactly the case this test needs.
            if (!probe.Ok) { chosen = id; break; }
        }

        if (chosen is null)
        {
            Assert.Ignore("every one of this client's cases is also readable by the stranger seat "
                        + "— the seed cannot support this test");
        }

        await LoginAsync(ClientEmail, ClientPassword);
        await Page.GotoAsync($"{BaseUrl}/my-cases/{chosen}");
        await WaitUntilLoadedAsync();
        return Page.Url;
    }

    private static async Task<string?> TokenAsync(IAPIRequestContext api, string email, string password)
    {
        var response = await api.PostAsync("/login", new()
        {
            DataObject = new { email, password },
        });
        return response.Ok ? (await response.JsonAsync())?.GetProperty("accessToken").GetString() : null;
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

    /// <summary>
    /// The case this test shared, so teardown can take the share back no matter how the run ended.
    /// </summary>
    /// <remarks>
    /// <b>This test used to poison its own next run.</b> It shares one of Daniel's cases with the
    /// stranger, while <see cref="OpenDanielsCaseAsync"/> picks the first case the stranger
    /// CANNOT read — so once a share was left behind, the next run skipped that case, landed on
    /// one with no sharing UI, and quietly ignored itself. Revocation was the cleanup, and it sat
    /// behind an <c>if</c> on finding a button, so any change to that row's markup turned the
    /// cleanup off without turning the test red. That is how one failure and one silent skip came
    /// out of the same suite on consecutive runs.
    /// </remarks>
    private string? _sharedCaseId;

    [TearDown]
    public async Task RevokeAnythingThisTestShared()
    {
        if (_sharedCaseId is null) return;
        var caseId = _sharedCaseId;
        _sharedCaseId = null;

        // Through the API, not the screen: teardown has to work even when the test failed halfway
        // through, and especially when it failed because the screen was wrong.
        var api = await Playwright.APIRequest.NewContextAsync(new() { BaseURL = ApiUrl });
        var token = await TokenAsync(api, ClientEmail, ClientPassword);
        if (token is null) return;
        var auth = new Dictionary<string, string> { ["Authorization"] = $"Bearer {token}" };

        var listed = await api.GetAsync($"/api/my-cases/{caseId}/co-clients", new() { Headers = auth });
        if (!listed.Ok) return;

        var strangerId = await UserIdAsync(api, StrangerEmail, StrangerPassword);
        foreach (var entry in (await listed.JsonAsync())!.Value.EnumerateArray())
        {
            if (entry.GetProperty("appUserId").GetString() != strangerId) continue;
            var accessId = entry.GetProperty("accessId").GetString();
            await api.DeleteAsync($"/api/my-cases/{caseId}/co-clients/{accessId}", new() { Headers = auth });
        }
    }

    private async Task<string?> UserIdAsync(IAPIRequestContext api, string email, string password)
    {
        var token = await TokenAsync(api, email, password);
        if (token is null) return null;
        var me = await api.GetAsync("/api/me", new()
        {
            Headers = new Dictionary<string, string> { ["Authorization"] = $"Bearer {token}" },
        });
        if (!me.Ok) return null;
        var body = (await me.JsonAsync())!.Value;
        return body.TryGetProperty("userId", out var id) ? id.GetString()
             : body.TryGetProperty("id", out var alt) ? alt.GetString() : null;
    }

    /// <summary>
    /// Opens a case Daniel can actually SHARE, and makes sure the stranger does not already have
    /// it.
    /// </summary>
    /// <remarks>
    /// Deliberately NOT <see cref="OpenDanielsCaseAsync"/>, which picks a case the stranger cannot
    /// read. That is the right premise for the negative test and precisely the wrong one here:
    /// this test's whole job is to grant that access, so a leftover share made its own case
    /// ineligible next time round, and the run silently landed on a case with no sharing at all.
    /// The two tests want different things from the seed, so they ask for different things.
    ///
    /// Any leftover share is cleared first, so a half-finished earlier run cannot decide what this
    /// one does.
    /// </remarks>
    private async Task<string> OpenACaseDanielCanShareAsync()
    {
        var api = await Playwright.APIRequest.NewContextAsync(new() { BaseURL = ApiUrl });
        var token = await TokenAsync(api, ClientEmail, ClientPassword);
        Assert.That(token, Is.Not.Null, "the client seat should be able to sign in");
        var auth = new Dictionary<string, string> { ["Authorization"] = $"Bearer {token}" };

        var cases = await api.GetAsync("/api/my-cases", new() { Headers = auth });
        Assert.That(cases.Ok, Is.True, "the client's own case list should load");

        var strangerId = await UserIdAsync(api, StrangerEmail, StrangerPassword);
        string? chosen = null;
        foreach (var element in (await cases.JsonAsync())!.Value.EnumerateArray())
        {
            var id = element.GetProperty("caseId").GetString();
            if (id is null) continue;

            // 200 from co-clients means Daniel is the PRIMARY client on this case — the only
            // person the API lets manage sharing, and so the only case this test can drive.
            var coClients = await api.GetAsync($"/api/my-cases/{id}/co-clients", new() { Headers = auth });
            if (!coClients.Ok) continue;

            foreach (var entry in (await coClients.JsonAsync())!.Value.EnumerateArray())
            {
                if (entry.GetProperty("appUserId").GetString() != strangerId) continue;
                var accessId = entry.GetProperty("accessId").GetString();
                await api.DeleteAsync($"/api/my-cases/{id}/co-clients/{accessId}", new() { Headers = auth });
            }

            chosen = id;
            break;
        }

        if (chosen is null)
        {
            Assert.Ignore("none of this client's cases are ones they can share — the seed cannot "
                        + "support this test");
        }

        await LoginAsync(ClientEmail, ClientPassword);
        await Page.GotoAsync($"{BaseUrl}/my-cases/{chosen}");
        await WaitUntilLoadedAsync();
        return Page.Url;
    }

    [Test]
    public async Task SharingACase_LetsTheOtherPersonSeeIt_AndTheOwnerCanTakeItBack()
    {
        var caseUrl = await OpenACaseDanielCanShareAsync();
        var path = new Uri(caseUrl).PathAndQuery;
        _sharedCaseId = path.Split('/').Last();

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
        await LoginAsync(ClientEmail, ClientPassword);
        await Page.GotoAsync($"{BaseUrl}{path}");
        await WaitUntilLoadedAsync();

        // Scoped to this person's own row. Taking the first danger button on the page would revoke
        // whoever happens to be listed first — the case already has another co-client from the
        // seed data, and removing them here would quietly break other fixtures.
        var strangerRow = Main.Locator("div.d-flex.align-items-center.justify-content-between")
                              .Filter(new() { HasTextString = "James Thornton" }).First;
        var removeButton = strangerRow.Locator("button.btn-danger").First;

        // NOT conditional any more. Revocation is, by this test's own reckoning, the half that
        // matters — and a silent `if` around it meant a markup change could switch the check off
        // while the test still reported green.
        //
        // Waited for rather than counted. CountAsync is an instant snapshot and WaitUntilLoadedAsync
        // only means the circuit is up, not that THIS row has rendered — so under a full-suite load
        // the count was taken before the list arrived and the test reported a missing button on a
        // page that was merely slow (once in 401 tests, 2026-08-27; passed alone). ToBeVisibleAsync
        // retries, so it still fails when the button is genuinely absent and no longer fails when
        // the page is just behind.
        await Expect(removeButton).ToBeVisibleAsync(new() { Timeout = 30_000 });
        Assert.That(await removeButton.CountAsync(), Is.GreaterThan(0),
            "the shared person's row should offer a way to take the access back");
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
