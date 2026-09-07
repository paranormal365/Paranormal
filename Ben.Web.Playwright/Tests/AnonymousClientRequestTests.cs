using Microsoft.Playwright;
using NUnit.Framework;

namespace Ben.Web.Playwright.Tests;

/// <summary>
/// Asking for an investigation without an account (site evaluation 2026-09-06, phase 1).
/// </summary>
/// <remarks>
/// <para>The wizard used to open with a sign-in wall, and the two tests that asserted the wall —
/// <c>Wizard_AnonymousRedirectsToLogin</c> and its twin — were retired with it. What replaced
/// them is here: the stranger gets all the way through, and the form never tells anybody which
/// email addresses have accounts.</para>
///
/// <para>Ordinary member seat only where a seat is needed at all. Most of this runs signed out,
/// which is the whole point.</para>
/// </remarks>
[TestFixture]
[Category("ClientRequests")]
public class AnonymousClientRequestTests : BenTestBase
{
    private static string Unique => Guid.NewGuid().ToString("N")[..8];

    /// <summary>
    /// Generated per run. The account this fixture creates is a throwaway that never confirms, so
    /// nothing outside the run needs the value — and a literal in a tracked file is a live
    /// credential in a public repository (<c>NoCredentialsInTheRepoTests</c>).
    /// </summary>
    private readonly string _password = NewTestPassword();

    /// <summary>
    /// A signed-out visitor reaches the form itself rather than a sign-in screen.
    /// </summary>
    [Test]
    public async Task A_stranger_reaches_the_form_without_signing_in()
    {
        await LogoutAsync();
        await Page.GotoAsync($"{BaseUrl}/my-requests/new");

        // The address fields, by label — the placeholders are worked examples, so matching on
        // "address" text alone finds the prose instead of the input.
        await Expect(Page.GetByLabel("Street Address", new() { Exact = false }).First)
            .ToBeVisibleAsync(new() { Timeout = 20_000 });

        Assert.That(Page.Url, Does.Not.Contain("/login"),
            "The wizard sent a signed-out visitor to sign in; it is meant to open to everyone.");

        var body = await Page.InnerTextAsync("body");
        Assert.That(body, Does.Not.Contain("You must be signed in"),
            "The old sign-in panel is still rendering.");
    }

    /// <summary>
    /// Step 1 will not advance until the address has been placed on the map.
    /// </summary>
    /// <remarks>
    /// W-R1: this used to be allowed, and the consequence appeared three screens later as
    /// "Address not verified" — after everything had been typed.
    /// </remarks>
    [Test]
    public async Task Step_one_will_not_advance_on_an_unverified_address()
    {
        await LogoutAsync();
        await Page.GotoAsync($"{BaseUrl}/my-requests/new");
        await Expect(Page.Locator("#req-street1")).ToBeVisibleAsync(new() { Timeout = 20_000 });

        await FillAndConfirmAsync("#req-street1", "2500 West End Ave");
        await FillAndConfirmAsync("#req-city", "Nashville");
        await FillAndConfirmAsync("#req-state", "TN");
        await FillAndConfirmAsync("#req-zip", "37203");

        var next = Page.GetByRole(AriaRole.Button, new() { Name = "Next" });
        await Expect(next).ToBeDisabledAsync(new() { Timeout = 10_000 });
        await Expect(Page.GetByText("Verify the address to continue")).ToBeVisibleAsync();
    }

    /// <summary>
    /// The whole way through: address, story, group, and the account made at Submit.
    /// </summary>
    /// <remarks>
    /// <para>Geocoding is a real round trip to the map service, so this test skips rather than
    /// fails if the address cannot be verified — an offline machine is not a product defect. The
    /// account it creates is a throwaway one; it never confirms, so it can never sign in, which is
    /// exactly the state under test.</para>
    /// </remarks>
    [Test]
    public async Task A_stranger_can_ask_for_help_without_an_account()
    {
        await LogoutAsync();
        await Page.GotoAsync($"{BaseUrl}/my-requests/new");
        await Expect(Page.Locator("#req-street1")).ToBeVisibleAsync(new() { Timeout = 20_000 });

        await FillAndConfirmAsync("#req-street1", "2500 West End Ave");
        await FillAndConfirmAsync("#req-city", "Nashville");
        await FillAndConfirmAsync("#req-state", "TN");
        await FillAndConfirmAsync("#req-zip", "37203");

        await ClickUntilAsync(
            Page.GetByRole(AriaRole.Button, new() { Name = "Verify Address" }),
            Page.GetByText("Address verified").Or(Page.GetByText("couldn't")));

        if (!await Page.GetByText("Address verified").IsVisibleAsync())
            Assert.Ignore("The geocoding service did not answer; the funnel below needs a real lookup.");

        await ClickUntilAsync(
            Page.GetByRole(AriaRole.Button, new() { Name = "Next: About You" }),
            Page.GetByText("Step 2"));

        await ClickUntilAsync(
            Page.GetByRole(AriaRole.Button, new() { Name = "Next: Your Experiences" }),
            Page.GetByText("Step 3"));

        // The editor is an iframe; typing into its body is how a person writes here.
        var editorBody = Page.FrameLocator(".k-editor iframe").Locator("body");
        await editorBody.ClickAsync();
        await editorBody.PressSequentiallyAsync("Three knocks on the north wall, about 2am, twice last week.");

        await ClickUntilAsync(
            Page.GetByRole(AriaRole.Button, new() { Name = "Next: Find Organizations" }),
            Page.GetByText("Step 4").Or(Page.GetByText("No organizations")));

        if (await Page.GetByText("No organizations are currently accepting").IsVisibleAsync())
            Assert.Ignore("No seeded group covers Nashville on this stack; the account step needs one.");

        // The promise this form asks people to believe, in the words the plan fixed on.
        await Expect(Page.Locator("#req-privacy-note")).ToContainTextAsync("none of it is ever sold");

        var email = $"stranger{Unique}@example.com";
        await FillAndConfirmAsync("#req-name", "Casey Miller");
        await FillAndConfirmAsync("#req-email", email);
        await FillAndConfirmAsync("#req-password", _password);

        // Pick the first group offered.
        await Page.Locator(".border.rounded.p-3").First.ClickAsync();

        await ClickUntilAsync(
            Page.GetByRole(AriaRole.Button, new() { Name = "Submit Request" }),
            Page.GetByText("Request Submitted"));

        await Expect(Page.GetByText("Check your email")).ToBeVisibleAsync(new() { Timeout = 20_000 });

        // The account exists but cannot be used yet, and the sign-in page says which of those it
        // is rather than claiming the password is wrong.
        await Page.GotoAsync($"{BaseUrl}/login");
        await FillAndConfirmAsync("#login-email", email);
        await FillAndConfirmAsync("#login-password", _password);
        await ClickUntilAsync(
            Page.Locator("button[type='submit']"),
            Page.GetByText("Confirm your email address first"));
        await Expect(Page.GetByText("Confirm your email address first"))
            .ToBeVisibleAsync(new() { Timeout = 15_000 });
    }

    /// <summary>
    /// The form never says whether an address already has an account.
    /// </summary>
    /// <remarks>
    /// <para>Asserted at the API, not through the wizard: the property under test is the reply
    /// itself, byte for byte, and driving four screens twice to compare two sentences would test
    /// the screens rather than the rule. The endpoint is the thing an attacker would call anyway.
    /// </para>
    ///
    /// <para>Both calls are refused for the same reason — an ungeocoded address — so neither
    /// creates anything, and the comparison is still of the two paths' answers because the
    /// endpoint validates before it ever looks the address up. That ordering is the rule.</para>
    /// </remarks>
    [Test]
    public async Task An_existing_address_is_never_confirmed_to_a_stranger()
    {
        var known   = await SubmitAsync(ClientEmail);
        var unknown = await SubmitAsync($"nobody{Unique}@example.com");

        Assert.That(unknown.Status, Is.EqualTo(known.Status),
            "The status differed for a registered address — that is an oracle for who has an account.");
        Assert.That(unknown.Body, Is.EqualTo(known.Body),
            $"The reply differed for a registered address.\nKnown: {known.Body}\nUnknown: {unknown.Body}");
    }

    private async Task<(int Status, string Body)> SubmitAsync(string email)
    {
        var response = await Page.APIRequest.PostAsync($"{ApiUrl}/api/public/client-requests/submit", new()
        {
            DataObject = new
            {
                streetAddress1 = "2500 West End Ave",
                city           = "Nashville",
                state          = "TN",
                zipCode        = "37203",
                country        = "US",
                // Deliberately unverified, so nothing is created by either path.
                latitude       = (decimal?)null,
                longitude      = (decimal?)null,
                gender         = 0,
                description    = "<p>Three knocks.</p>",
                organizationIds = new[] { Guid.NewGuid() },
                name           = "Casey Miller",
                email,
                password       = _password,
            },
        });

        return (response.Status, await response.TextAsync());
    }
}
