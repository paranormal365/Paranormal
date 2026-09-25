using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Playwright;
using NUnit.Framework;

namespace Ben.Web.Playwright.Tests;

/// <summary>
/// Three more letters read from the outbox and acted on the way their readers act on them: an
/// invitation onto a case, a link confirming an added address, and a code proving who runs a place.
/// </summary>
/// <remarks>
/// <para><b>Why.</b> The 2026-09-23 audit of letters carrying something a person has to act on
/// found these three could not be followed by any browser test, for a reason that had nothing to
/// do with the tests: each was skipped when no mail server was configured, and the test hosts
/// never configure one. So the letter never existed, and the only coverage was the link or code
/// the SCREEN showed — which is what the sender believes it sent, not what arrived. They are
/// queued regardless now, so each test reads its letter from the outbox
/// (<see cref="BenTestBase.TryLetterFromTheOutboxAsync"/>) and uses exactly what it carries.</para>
///
/// <para><b>No shared seat's data is left changed.</b> The addressees are made for the test with
/// passwords generated for the run; the share on the client seat's case and the claimant group
/// are taken away in <see cref="PutThingsBack"/> however the test ended.</para>
/// </remarks>
[TestFixture]
[Category("Mail")]
public class AccountLettersAreFollowedTests : BenTestBase
{
    private static string Unique => Guid.NewGuid().ToString("N")[..8];

    // What each test made on somebody else's account, for the teardown.
    private string? _sharedCaseId;
    private string? _inviteeEmail;
    private string? _inviteeName;
    private string? _claimantOrgId;
    private string? _claimantOrgName;

    // ── an invitation onto a case ────────────────────────────────────────────

    /// <summary>
    /// A case's client invites somebody with no account; the letter's link makes them one and
    /// puts them on the case.
    /// </summary>
    /// <remarks>
    /// The invitee has no account, so this is the door the invite page was built to be — the app's
    /// first self-service registration (item #4). Somebody who already has one is linked on the
    /// spot, gets no letter, and is covered by <see cref="CoClientAccessTests"/>.
    /// </remarks>
    [Test]
    [Description("A case invitation's link makes the newcomer an account and puts them on the case.")]
    public async Task A_case_invitation_brings_a_newcomer_onto_the_case()
    {
        var tag = Unique;
        var inviteeEmail = _inviteeEmail = $"invited{tag}@example.com";
        var inviteeName = _inviteeName = $"Invited Reader {tag}";
        var password = NewTestPassword();

        var caseId = await ACaseTheClientCanShareAsync();
        _sharedCaseId = caseId;

        // ── the client invites an address nobody has signed up with ──────────
        await LoginAsync(ClientEmail, ClientPassword);
        await Page.GotoAsync($"{BaseUrl}/my-cases/{caseId}");
        await WaitUntilLoadedAsync();

        var dialog = Page.Locator(".modal.show");
        await ClickUntilAsync(Main.GetByRole(AriaRole.Button, new() { Name = "Add Person" }).First, dialog);
        await FillAndConfirmAsync("#mycasedetail-email-address-of-person-baf6", inviteeEmail);

        // Enabled only once the circuit has seen a valid address, so a click after it cannot be dropped.
        var add = dialog.GetByRole(AriaRole.Button, new() { Name = "Add", Exact = true });
        await Expect(add).ToBeEnabledAsync(new() { Timeout = 8_000 });
        await ClickUntilAsync(add, dialog.GetByText("share this link", new() { Exact = false }));

        // With no mail server the reply says the letter did not leave, and the dialog falls back
        // to the link to copy. That fallback is still the promise to the inviter, so it is held
        // here — and it must be the same invitation the letter carries, not merely an invitation.
        var shown = await dialog.Locator("input.form-control").First.InputValueAsync();
        Assert.That(shown, Does.Match(@"/invite/[0-9A-F]{64}$"), "the dialog did not offer the invite link to copy");

        // ── the letter ───────────────────────────────────────────────────────
        var letter = await TryLetterFromTheOutboxAsync(inviteeEmail, html => html.Contains("/invite/"));
        Assert.That(letter, Is.Not.Null, $"No invitation to {inviteeEmail} reached the outbox.");
        var decoded = System.Net.WebUtility.HtmlDecode(letter!);
        var link = Regex.Match(decoded, @"/invite/[^""'<>\s]*").Value;

        Assert.That(shown, Does.EndWith(link),
            "the letter and the dialog carry different invitations — one of them is a dead link");

        // The letter names the case; the page it opens must be about the same one.
        var caseTitle = Regex.Match(decoded, @"the case ""<strong>(.+?)</strong>""").Groups[1].Value;

        // ── the invitee follows it, from a browser nobody is signed in to ────
        await LogoutAsync();
        await Page.GotoAsync($"{BaseUrl}{link}");
        await Expect(Page.GetByRole(AriaRole.Heading, new() { Name = "You're Invited" }))
            .ToBeVisibleAsync(new() { Timeout = 20_000 });
        if (caseTitle.Length > 0)
            await Expect(Page.Locator(".login-card")).ToContainTextAsync(caseTitle);

        await FillAndConfirmAsync("#inviteaccept-your-name-a392", inviteeName);
        await FillAndConfirmAsync("#inviteaccept-password-87da", password);
        await FillAndConfirmAsync("#inviteaccept-confirm-password-d259", password);

        // Accepting signs the newcomer in and sends them to the case — or, being brand new, the
        // welcome wizard catches them first and carries the case along.
        await ClickUntilUrlAsync(
            Page.GetByRole(AriaRole.Button, new() { Name = "Create Account & Accept" }),
            @"/(my-cases/[0-9a-fA-F\-]{36}|onboarding)");

        // ── they can now see the case ────────────────────────────────────────
        // The wizard is its own journey (OnboardingJourneyTests); what is under test is the case.
        await MarkOnboardedAsync(inviteeEmail, password);

        await using (var api = await Playwright.APIRequest.NewContextAsync(new() { BaseURL = ApiUrl }))
        {
            var auth = await BearerAsync(api, inviteeEmail, password);
            var theirs = await api.GetAsync($"/api/my-cases/{caseId}", new() { Headers = auth });
            Assert.That(theirs.Ok, Is.True,
                $"the invitation was accepted and the API still refuses the invitee the case ({theirs.Status})");
        }

        await Page.GotoAsync($"{BaseUrl}/my-cases/{caseId}");
        await WaitUntilLoadedAsync();
        await Expect(Main.GetByText("Log Occurrence", new() { Exact = false }).First)
            .ToBeVisibleAsync(new() { Timeout = 30_000 });
    }

    // ── confirming an address added to a profile ─────────────────────────────

    /// <summary>
    /// The letter goes to the ADDED address — the one being proved — and its link confirms it.
    /// </summary>
    /// <remarks>
    /// <see cref="ProfileEmailConfirmationTests"/> adds an address and reads the link the page
    /// prints for a machine with no mail; it never follows one. The link is followed here signed
    /// out, because the page is deliberately not auth-gated: the person holding the letter is often
    /// on another device, and the token is the whole credential.
    /// </remarks>
    [Test]
    [Description("The letter sent to an added address confirms it, and the profile then says so.")]
    public async Task An_added_address_is_confirmed_by_the_letter_sent_to_it()
    {
        var tag = Unique;
        var email = $"profile{tag}@example.com";
        var added = $"second{tag}@example.com";
        var password = NewTestPassword();

        await MakeAccountAsync(email, $"Two Addresses {tag}", password);
        await MarkOnboardedAsync(email, password);

        await LoginAsync(email, password);
        var card = await OpenTheEmailsCardAsync();

        await ClickUntilAsync(card.GetByRole(AriaRole.Button, new() { Name = "Add" }).First,
                              Page.Locator("#myemailscard-address-f9a1"));
        await FillAndConfirmAsync("#myemailscard-address-f9a1", added);
        await ClickUntilAsync(card.GetByRole(AriaRole.Button, new() { Name = "Save" }).First,
                              card.GetByText($"{added} added", new() { Exact = false }));

        // ── the letter, to the new address and not the sign-in one ───────────
        var link = await LinkFromTheOutboxAsync(added, "/validate-email/");

        // Without mail the card prints the link too. When it does, it must be the letter's.
        var printed = Page.Locator("#email-validation-link");
        if (await printed.CountAsync() > 0)
            Assert.That(await printed.InputValueAsync(), Does.EndWith(link),
                "the card printed a different link from the one the letter carries");

        // ── followed from a browser nobody is signed in to ───────────────────
        await LogoutAsync();
        await Page.GotoAsync($"{BaseUrl}{link}");
        await Expect(Page.GetByRole(AriaRole.Heading, new() { Name = "Confirm your email address" }))
            .ToBeVisibleAsync(new() { Timeout = 20_000 });
        await ClickUntilAsync(
            Page.GetByRole(AriaRole.Button, new() { Name = "Confirm", Exact = true }),
            Page.GetByRole(AriaRole.Heading, new() { Name = "Address confirmed" }).Or(Page.Locator(".alert-danger")));
        await Expect(Page.GetByRole(AriaRole.Heading, new() { Name = "Address confirmed" }))
            .ToBeVisibleAsync(new() { Timeout = 5_000 });

        // ── and the profile agrees ───────────────────────────────────────────
        await LoginAsync(email, password);
        card = await OpenTheEmailsCardAsync();

        var row = card.Locator(".border-top", new() { HasTextString = added }).First;
        await Expect(row).ToBeVisibleAsync(new() { Timeout = 20_000 });
        await Expect(row.Locator(".badge.bg-success-subtle", new() { HasTextString = "Confirmed" }))
            .ToBeVisibleAsync(new() { Timeout = 10_000 });
        await Expect(row).Not.ToContainTextAsync("Not confirmed");
        await Expect(row.GetByRole(AriaRole.Button, new() { Name = "Send confirmation link" })).ToHaveCountAsync(0);
    }

    // ── a code proving who runs a place ──────────────────────────────────────

    private const string ProvingPlaceName = "Playwright Proving House";
    private const string ProvingStreet = "1 Proving Letter Lane";
    private const string ProvingLabel = "Playwright proving address";
    private const string WitnessSlug = "pw-venue-witnesses";
    private const string WitnessName = "Playwright Venue Witnesses";

    /// <summary>
    /// A group claims a place, the code goes to the place's own published address, and the code
    /// read from that letter proves the claim.
    /// </summary>
    /// <remarks>
    /// <para><b>The address cannot be made fresh for the run</b>, and that is the rule, not the
    /// harness: a code may only go to a public address somebody outside the claiming group
    /// recorded at least a week earlier (<c>VenueClaims.ContactMinimumAge</c>), so a claimant
    /// cannot have a friend add their own inbox the afternoon they claim. Nothing a test can reach
    /// backdates one. So a fixed place carries one address, recorded by the site administrator for
    /// a standing witness group, and the first run that finds none records it and stands aside
    /// until it is a week old. After that every run uses it — each run's claimant group is new and
    /// is purged afterwards, which takes its claim with it and leaves the address free to prove
    /// the next one. The letter a run reads is told from earlier ones by that group's name.</para>
    ///
    /// <para><b>Ends at Proved, not at "confirmed as the venue".</b> A proved claim stands for a
    /// week while every group that knows the place may object; only then does a job confirm it,
    /// and the reviewer's screen deliberately offers no early confirm for one. What the code
    /// decides is Pending → Proved, so that is what is asserted — and that the place names no
    /// venue yet, which is the week doing its job.</para>
    /// </remarks>
    [Test]
    [Description("The code in the letter to a place's own address proves a group's claim to run it.")]
    public async Task A_venue_claim_is_proved_by_the_code_in_the_places_letter()
    {
        var orgName = _claimantOrgName = $"Playwright Code Claimants {Unique}";

        await using var api = await Playwright.APIRequest.NewContextAsync(new() { BaseURL = ApiUrl });
        var admin = await BearerAsync(api, SuperAdminEmail, SuperAdminPassword);
        var owner = await BearerAsync(api, UserEmail, UserPassword);

        // ── the claimant: a new group, answered for by the seeded owner seat ─
        var register = await api.PostAsync("/api/security/organizations/register", new()
        {
            Headers = owner,
            DataObject = new { name = orgName, urlName = $"pw-code-{Guid.NewGuid():N}"[..20], kind = 0 },
        });
        Assert.That(register.Ok, Is.True, "the claimant group was refused: " + await register.TextAsync());
        _claimantOrgId = (await register.JsonAsync())!.Value.GetProperty("organizationId").GetString();

        // ── the place, and its address old enough to prove anything ─────────
        var placeId = await TheProvingPlaceAsync(api, admin);

        var start = await StartAsync(api, owner, placeId);
        if (start.GetProperty("alreadyVenue").GetString() is { Length: > 0 } venue)
            Assert.Fail($"{venue} is confirmed as the venue at {ProvingPlaceName}, so nobody can claim it. An "
                      + "earlier run's group outlived its teardown and its week passed — undo it at /admin/venue-claims.");

        var proving = start.GetProperty("provingContacts").EnumerateArray()
            .FirstOrDefault(c => c.GetProperty("label").GetString() == ProvingLabel);
        if (proving.ValueKind == JsonValueKind.Undefined)
        {
            var planted = await RecordTheProvingAddressIfMissingAsync(api, admin, placeId);
            Assert.Ignore(
                $"{ProvingPlaceName}'s proving address ({planted}) has been on the record for less than a week, "
              + "and a code may only go to one that has been (VenueClaims.ContactMinimumAge). This test runs "
              + "once it is a week old, and every run after that.");
        }

        var contactId = proving.GetProperty("id").GetString()!;
        var address = await ContactValueAsync(api, admin, placeId, contactId);

        // ── the claim, on the claimant's screen ──────────────────────────────
        await LoginAsync(UserEmail, UserPassword);
        await Page.GotoAsync($"{BaseUrl}/places/{placeId}");
        await WaitUntilLoadedAsync();
        await ClickUntilUrlAsync(
            Page.Locator(".place-claim-link", new() { HasTextString = orgName }),
            @"/venue/claim\?place=");
        await Expect(Page.Locator("#claim-submit")).ToBeVisibleAsync(new() { Timeout = 30_000 });

        var choice = Page.Locator($"#claim-proof-{Guid.Parse(contactId):N}");
        await Page.Locator($"label[for=claim-proof-{Guid.Parse(contactId):N}]").ClickAsync();
        await Expect(choice).ToBeCheckedAsync();

        await ClickUntilAsync(Page.Locator("#claim-submit"), Page.Locator("#claim-code").Or(Page.Locator("#claim-error")));
        if (await Page.Locator("#claim-error").IsVisibleAsync())
            Assert.Fail("the claim was refused: " + await Page.Locator("#claim-error").InnerTextAsync());

        // The page names the inbox the code went to, masked the way the server masks it.
        var at = address.IndexOf('@');
        await Expect(Page.Locator("#claim-state")).ToContainTextAsync($"{address[0]}•••{address[at..]}");

        // ── the letter to the place ──────────────────────────────────────────
        // SendCodeAsync writes the code alone in bold; the place and group names are the only
        // other bold text and are never six digits.
        var codeShape = new Regex(@"<strong>(\d{6})</strong>");
        var letter = await TryLetterFromTheOutboxAsync(address,
            html => html.Contains(orgName) && codeShape.IsMatch(html));
        Assert.That(letter, Is.Not.Null,
            $"No letter to {address} naming {orgName} and carrying a six-digit code reached the outbox.");
        var code = codeShape.Match(letter!).Groups[1].Value;

        // ── the claimant enters it ───────────────────────────────────────────
        await FillAndConfirmAsync("#claim-code", code);
        await ClickUntilAsync(Page.Locator("#claim-code-submit"), Page.Locator("#claim-proved").Or(Page.Locator("#claim-error")));
        if (await Page.Locator("#claim-error").IsVisibleAsync())
            Assert.Fail($"the code from the letter ({code}) was refused: " + await Page.Locator("#claim-error").InnerTextAsync());
        await Expect(Page.Locator("#claim-proved")).ToBeVisibleAsync();

        // ── proved, and standing for its week ────────────────────────────────
        var after = await StartAsync(api, owner, placeId);
        var claim = after.GetProperty("openClaim");
        Assert.That(claim.ValueKind, Is.EqualTo(JsonValueKind.Object), "the proved claim is not the group's open claim");
        Assert.That(claim.GetProperty("state").GetInt32(), Is.EqualTo(1), "the claim is not Proved after the code");
        Assert.That(claim.GetProperty("objectionsCloseUtc").GetDateTime(),
            Is.GreaterThan(DateTime.UtcNow.AddDays(6)), "a proved claim should stand a week for objections");

        var contacts = await api.GetAsync($"/api/public/places/{placeId}/contacts");
        Assert.That(contacts.Ok, Is.True, await contacts.TextAsync());
        Assert.That((await contacts.JsonAsync())!.Value.GetProperty("venueOrganizationName").GetString(), Is.Null,
            "the place names a venue the moment the code was entered, before anybody could object");
    }

    // ── putting things back ──────────────────────────────────────────────────

    [TearDown]
    public async Task PutThingsBack()
    {
        await using var api = await Playwright.APIRequest.NewContextAsync(new() { BaseURL = ApiUrl });

        if (_sharedCaseId is { } caseId)
        {
            _sharedCaseId = null;
            var auth = await TryBearerAsync(api, ClientEmail, ClientPassword);
            if (auth is not null)
            {
                // The invitee, whether they got on the case or the invitation is still waiting.
                var listed = await api.GetAsync($"/api/my-cases/{caseId}/co-clients", new() { Headers = auth });
                if (listed.Ok)
                    foreach (var cc in (await listed.JsonAsync())!.Value.EnumerateArray())
                        if (cc.GetProperty("displayName").GetString() == _inviteeName)
                            await api.DeleteAsync($"/api/my-cases/{caseId}/co-clients/{cc.GetProperty("accessId").GetString()}",
                                new() { Headers = auth });

                var pending = await api.GetAsync($"/api/my-cases/{caseId}/invites", new() { Headers = auth });
                if (pending.Ok)
                    foreach (var inv in (await pending.JsonAsync())!.Value.EnumerateArray())
                        if (string.Equals(inv.GetProperty("email").GetString(), _inviteeEmail, StringComparison.OrdinalIgnoreCase))
                            await api.DeleteAsync($"/api/my-cases/{caseId}/invites/{inv.GetProperty("id").GetString()}",
                                new() { Headers = auth });
            }
        }

        if (_claimantOrgId is { } orgId)
        {
            _claimantOrgId = null;
            // Takes the claim with it, which is what frees the proving address for the next run.
            var admin = await BearerAsync(api, SuperAdminEmail, SuperAdminPassword);
            var purged = await api.DeleteAsync($"/api/admin/organizations/{orgId}/purge",
                new() { Headers = admin, DataObject = new { confirmName = _claimantOrgName } });
            Assert.That(purged.Ok, Is.True,
                $"the claimant group was not purged, and its claim now blocks {ProvingPlaceName}: {await purged.TextAsync()}");
        }
    }

    // ── plumbing ─────────────────────────────────────────────────────────────

    /// <summary>A case the client seat is the PRIMARY client on — the only kind it may share.</summary>
    private async Task<string> ACaseTheClientCanShareAsync()
    {
        await using var api = await Playwright.APIRequest.NewContextAsync(new() { BaseURL = ApiUrl });
        var auth = await BearerAsync(api, ClientEmail, ClientPassword);

        var cases = await api.GetAsync("/api/my-cases", new() { Headers = auth });
        Assert.That(cases.Ok, Is.True, "the client's own case list should load");

        foreach (var c in (await cases.JsonAsync())!.Value.EnumerateArray())
        {
            var id = c.GetProperty("caseId").GetString();
            if (id is null) continue;
            // 200 from co-clients means this seat is the primary client.
            if ((await api.GetAsync($"/api/my-cases/{id}/co-clients", new() { Headers = auth })).Ok) return id;
        }

        Assert.Ignore("the client seat is primary client on none of its cases — the seed cannot support this test");
        return "";
    }

    private async Task<ILocator> OpenTheEmailsCardAsync()
    {
        await Page.GotoAsync($"{BaseUrl}/profile");
        await Expect(Page.GetByRole(AriaRole.Tab, new() { Name = "Contact" })).ToBeVisibleAsync(new() { Timeout = 20_000 });
        var card = Page.Locator(".card", new() { HasText = "Email addresses" }).First;
        await ClickUntilAsync(Page.GetByRole(AriaRole.Tab, new() { Name = "Contact" }), card);
        return card;
    }

    /// <summary>The fixed place the proving address lives on — the same row every run (item 88's dedup).</summary>
    private static async Task<string> TheProvingPlaceAsync(IAPIRequestContext api, Dictionary<string, string> admin)
    {
        var made = await api.PostAsync("/api/places/public-location", new()
        {
            Headers = admin,
            DataObject = new
            {
                name = ProvingPlaceName, streetAddress1 = ProvingStreet, streetAddress2 = (string?)null,
                city = "Franklin", state = "TN", zipCode = "37064", country = "US",
                latitude = 35.9260m, longitude = -86.8700m,
            },
        });
        Assert.That(made.Ok, Is.True, "the proving place could not be made or found: " + await made.TextAsync());
        return (await made.JsonAsync())!.Value.GetProperty("id").GetString()!;
    }

    private async Task<JsonElement> StartAsync(IAPIRequestContext api, Dictionary<string, string> owner, string placeId)
    {
        var start = await api.GetAsync($"/api/organizations/{_claimantOrgId}/venue-claims/start?place={placeId}",
            new() { Headers = owner });
        Assert.That(start.Ok, Is.True, "the claim could not be started: " + await start.TextAsync());
        return (await start.JsonAsync())!.Value;
    }

    /// <summary>
    /// The proving address, recorded by the site administrator for the witness group when there is
    /// none yet. Answers with the address.
    /// </summary>
    /// <remarks>
    /// For a standing group of its own rather than a seeded one: every group a place's details
    /// name is told when somebody proves a claim there, and that should be nobody's inbox but the
    /// administrator's.
    /// </remarks>
    private async Task<string> RecordTheProvingAddressIfMissingAsync(
        IAPIRequestContext api, Dictionary<string, string> admin, string placeId)
    {
        var listed = await api.GetAsync($"/api/places/{placeId}/contacts", new() { Headers = admin });
        Assert.That(listed.Ok, Is.True, await listed.TextAsync());
        foreach (var c in (await listed.JsonAsync())!.Value.GetProperty("contacts").EnumerateArray())
            if (c.GetProperty("label").GetString() == ProvingLabel && c.GetProperty("isPublic").GetBoolean())
                return c.GetProperty("value").GetString()!;

        var address = $"venue-proof-{Unique}@example.com";
        var added = await api.PostAsync($"/api/places/{placeId}/contacts", new()
        {
            Headers = admin,
            DataObject = new
            {
                organizationId = await TheWitnessGroupAsync(api, admin),
                kind = 2,   // PlaceContactKind.Email
                value = address, label = ProvingLabel, isPublic = true,
            },
        });
        Assert.That(added.Ok, Is.True, "the proving address was refused: " + await added.TextAsync());
        return address;
    }

    private static async Task<string> TheWitnessGroupAsync(IAPIRequestContext api, Dictionary<string, string> admin)
    {
        var orgs = await api.GetAsync("/api/organizations", new() { Headers = admin });
        Assert.That(orgs.Ok, Is.True, await orgs.TextAsync());
        foreach (var o in (await orgs.JsonAsync())!.Value.EnumerateArray())
            if (o.GetProperty("urlName").GetString() == WitnessSlug) return o.GetProperty("id").GetString()!;

        var made = await api.PostAsync("/api/security/organizations/register", new()
        {
            Headers = admin,
            DataObject = new { name = WitnessName, urlName = WitnessSlug, kind = 0 },
        });
        Assert.That(made.Ok, Is.True, "the witness group could not be made: " + await made.TextAsync());
        return (await made.JsonAsync())!.Value.GetProperty("organizationId").GetString()!;
    }

    /// <summary>The full address behind a masked proving contact — public, so anybody may read it.</summary>
    private static async Task<string> ContactValueAsync(
        IAPIRequestContext api, Dictionary<string, string> admin, string placeId, string contactId)
    {
        var listed = await api.GetAsync($"/api/places/{placeId}/contacts", new() { Headers = admin });
        Assert.That(listed.Ok, Is.True, await listed.TextAsync());
        foreach (var c in (await listed.JsonAsync())!.Value.GetProperty("contacts").EnumerateArray())
            if (string.Equals(c.GetProperty("id").GetString(), contactId, StringComparison.OrdinalIgnoreCase))
                return c.GetProperty("value").GetString()!;

        Assert.Fail($"the proving contact {contactId} is offered to the claimant and missing from the place's own list");
        return "";
    }

    private static async Task<Dictionary<string, string>?> TryBearerAsync(IAPIRequestContext api, string email, string password)
    {
        var login = await api.PostAsync("/login", new() { DataObject = new { email, password } });
        if (!login.Ok) return null;
        var token = (await login.JsonAsync())!.Value.GetProperty("accessToken").GetString();
        return new Dictionary<string, string> { ["Authorization"] = $"Bearer {token}" };
    }

    private static async Task<Dictionary<string, string>> BearerAsync(IAPIRequestContext api, string email, string password)
    {
        var auth = await TryBearerAsync(api, email, password);
        Assert.That(auth, Is.Not.Null, $"{email} could not sign in to the API");
        return auth!;
    }

    private async Task MarkOnboardedAsync(string email, string password)
    {
        await using var api = await Playwright.APIRequest.NewContextAsync(new() { BaseURL = ApiUrl });
        var auth = await BearerAsync(api, email, password);
        var done = await api.PostAsync("/api/me/onboarding/complete", new() { Headers = auth });
        Assert.That(done.Ok, Is.True, "could not mark the test account onboarded: " + await done.TextAsync());
    }

    private async Task MakeAccountAsync(string email, string displayName, string password)
    {
        await using var api = await Playwright.APIRequest.NewContextAsync(new() { BaseURL = ApiUrl });
        var admin = await BearerAsync(api, SuperAdminEmail, SuperAdminPassword);
        var made = await api.PostAsync("/api/admin/app-users", new()
        {
            Headers = admin,
            DataObject = new
            {
                email, password, displayName, userName = (string?)null,
                isEmailConfirmed = true, isSuperAdmin = false,
            },
        });
        Assert.That(made.Ok, Is.True, "the test account was refused: " + await made.TextAsync());
    }
}
