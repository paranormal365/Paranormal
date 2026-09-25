using System.Net.Http.Json;
using Microsoft.Playwright;
using NUnit.Framework;

namespace Ben.Web.Playwright.Tests;

/// <summary>
/// The links these letters carry are read from the letter and followed, the way their readers do.
/// </summary>
/// <remarks>
/// <para><b>Why.</b> An audit on 2026-09-23 found thirteen letters carrying something a person
/// has to act on, and two of them — the sign-up confirmation and the seat-pick link — followed
/// anywhere in the browser suite. These three already reach the outbox with no mail server, so
/// nothing stood between them and a test but writing it. Each reads its letter from the outbox
/// (<see cref="BenTestBase.TryLetterFromTheOutboxAsync"/>) — not a link built from an API reply —
/// because a link the letter never carried, or carried wrong, is precisely the failure.</para>
///
/// <para><b>Every account here is a throwaway</b>, made for the test with a password generated for
/// the run, so no shared seat has its password changed.</para>
/// </remarks>
[TestFixture]
[Category("Mail")]
public class EmailedLinksAreFollowedTests : BenTestBase
{
    private static string Unique => Guid.NewGuid().ToString("N")[..8];

    // ── a forgotten password ─────────────────────────────────────────────────

    [Test]
    [Description("The reset letter's link sets a new password, and the new one signs in.")]
    public async Task A_reset_letter_sets_a_new_password()
    {
        var email = $"forgot{Unique}@example.com";
        var oldPassword = NewTestPassword();
        await MakeAccountAsync(email, "Forgetful Reader", oldPassword, confirmed: true);

        await LogoutAsync();
        await Page.GotoAsync($"{BaseUrl}/forgot-password");
        await Expect(Page.Locator("#forgot-email")).ToBeVisibleAsync(new() { Timeout = 15_000 });
        await FillAndConfirmAsync("#forgot-email", email);
        await ClickUntilAsync(
            Page.GetByRole(AriaRole.Button, new() { Name = "Send reset link", Exact = false }),
            Page.GetByText("reset link is on its way", new() { Exact = false }));

        // The account was made by an administrator, so a handover letter to this address is in the
        // outbox too, with a link of the same shape. The reset letter is the one without the flag.
        var link = await LinkFromLetterAsync(email, "/reset-password?", html => !html.Contains("handover=1"));

        var newPassword = NewTestPassword();
        await SetPasswordFromLinkAsync(link, newPassword);

        Assert.That(await SignsInAsync(email, newPassword), Is.True, "The new password does not sign in.");
        Assert.That(await SignsInAsync(email, oldPassword), Is.False, "The old password still signs in.");
    }

    // ── an account somebody else made ────────────────────────────────────────

    /// <summary>
    /// The most sensitive letter the site sends: until its link is used, the only password on the
    /// account is the one the administrator typed.
    /// </summary>
    [Test]
    [Description("An account made by an administrator is handed over by the link in its letter.")]
    public async Task A_handover_letter_gives_the_account_to_its_owner()
    {
        var email = $"handed{Unique}@example.com";
        var typedByAdmin = NewTestPassword();

        // Made on the administrator's own page, unconfirmed — the case the letter exists for.
        await LoginAsync(SuperAdminEmail, SuperAdminPassword);
        await Page.GotoAsync($"{BaseUrl}/admin/users/create");
        await Expect(Page.Locator("#adminusercreate-email-0463")).ToBeVisibleAsync(new() { Timeout = 15_000 });
        await FillAndConfirmAsync("#adminusercreate-display-name-4286", $"Handed Over {Unique}");
        await FillAndConfirmAsync("#adminusercreate-email-0463", email);
        await FillAndConfirmAsync("#adminusercreate-password-2989", typedByAdmin);
        await FillAndConfirmAsync("#adminusercreate-confirm-password-f173", typedByAdmin);
        await ClickUntilUrlAsync(
            Page.GetByRole(AriaRole.Button, new() { Name = "Create User" }),
            @"/admin/users/[0-9a-f\-]{36}");

        var link = await LinkFromLetterAsync(email, "/reset-password?", html => html.Contains("handover=1"));

        await LogoutAsync();
        var chosen = NewTestPassword();
        await SetPasswordFromLinkAsync(link, chosen);

        Assert.That(await SignsInAsync(email, chosen), Is.True,
            "The password the owner chose does not sign in — the handover did not confirm the address, "
          + "or did not set the password.");
        Assert.That(await SignsInAsync(email, typedByAdmin), Is.False,
            "The administrator's password still works after the owner took the account over.");
    }

    // ── a request made under somebody's address ──────────────────────────────

    /// <summary>
    /// A signed-out request names an address that already has an account; the holder claims it
    /// from the letter.
    /// </summary>
    /// <remarks>
    /// Submitted at the API rather than through the wizard: the wizard needs a live geocoder and is
    /// covered by <see cref="AnonymousClientRequestTests"/>. What is under test starts at the letter.
    /// </remarks>
    [Test]
    [Description("A request made under your address is claimed by the link in the letter.")]
    public async Task A_request_under_your_address_is_claimed_from_its_letter()
    {
        var email = $"holder{Unique}@example.com";
        var password = NewTestPassword();
        await MakeAccountAsync(email, "Address Holder", password, confirmed: true);

        // Somebody who already has an account has long since been through the welcome wizard.
        // A brand-new one is sent there first — carrying the link along (OnboardingGate), which
        // is a different journey from the one under test.
        await MarkOnboardedAsync(email, password);

        var street = $"{Random.Shared.Next(100, 9999)} Belmont Blvd";
        var submitted = await Page.APIRequest.PostAsync($"{ApiUrl}/api/public/client-requests/submit", new()
        {
            DataObject = new
            {
                streetAddress1 = street,
                city = "Nashville", state = "TN", zipCode = "37212", country = "US",
                latitude = 36.1340m, longitude = -86.7960m,
                gender = 0,
                description = "<p>Footsteps on the stairs after midnight.</p>",
                organizationIds = new[] { Guid.Parse(await OrgIdBySlugAsync("benco")) },
                name = "Somebody Else",
                email,
                password = NewTestPassword(),
            },
        });
        Assert.That(submitted.Ok, Is.True, "the request was refused: " + await submitted.TextAsync());

        var link = await LinkFromLetterAsync(email, "/my-requests/adopt/", html => html.Contains(street));

        // Followed the way the reader does: signed out, sent to sign in, and brought back to the
        // same link with its key intact.
        await LogoutAsync();
        await Page.GotoAsync($"{BaseUrl}{link}");
        await Page.WaitForURLAsync(url => url.Contains("/login"), new() { Timeout = 20_000 });
        await FillCredentialsAsync(email, password);
        await ClickUntilUrlAsync(Page.Locator("form button[type='submit']"), @"/my-requests/adopt/");

        // The page names the address — how the holder tells their own request from somebody
        // using their email — before it asks.
        await Expect(Page.GetByText(street, new() { Exact = false }).First)
            .ToBeVisibleAsync(new() { Timeout = 20_000 });

        await ClickUntilUrlAsync(Page.Locator("#adopt-yes"), @"/my-requests/[0-9a-f\-]{36}$");
        await Expect(Page.GetByText(street, new() { Exact = false }).First)
            .ToBeVisibleAsync(new() { Timeout = 20_000 });
    }

    // ── plumbing ─────────────────────────────────────────────────────────────

    /// <summary>The link a letter to <paramref name="to"/> carried, from the letter <paramref name="which"/> picks.</summary>
    private async Task<string> LinkFromLetterAsync(string to, string linkPath, Func<string, bool> which)
    {
        var shape = new System.Text.RegularExpressions.Regex(
            System.Text.RegularExpressions.Regex.Escape(linkPath) + "[^\"'<>\\s]*");

        var html = await TryLetterFromTheOutboxAsync(to, body =>
            which(System.Net.WebUtility.HtmlDecode(body)) && shape.IsMatch(System.Net.WebUtility.HtmlDecode(body)));
        Assert.That(html, Is.Not.Null, $"No letter to {to} carrying a {linkPath} link reached the outbox.");

        return shape.Match(System.Net.WebUtility.HtmlDecode(html!)).Value;
    }

    /// <summary>Opens the letter's link and chooses a password on the page it lands on.</summary>
    private async Task SetPasswordFromLinkAsync(string link, string password)
    {
        await Page.GotoAsync($"{BaseUrl}{link}");
        await Expect(Page.Locator("#reset-new")).ToBeVisibleAsync(new() { Timeout = 15_000 });

        // A complete link keeps the code out of sight; a code box here means the link lost it.
        await Expect(Page.Locator("#reset-code")).ToHaveCountAsync(0);

        await FillAndConfirmAsync("#reset-new", password);
        await FillAndConfirmAsync("#reset-confirm", password);
        await ClickUntilAsync(
            Page.GetByRole(AriaRole.Button, new() { Name = "Set password" }),
            Page.GetByText("Your password is set").Or(Page.Locator(".alert-danger")));

        await Expect(Page.GetByText("Your password is set")).ToBeVisibleAsync(new() { Timeout = 5_000 });
    }

    /// <summary>Whether this pair signs in, asked of the API so no browser session changes.</summary>
    private async Task<bool> SignsInAsync(string email, string password)
    {
        await using var api = await Playwright.APIRequest.NewContextAsync(new() { BaseURL = ApiUrl });
        var login = await api.PostAsync("/login", new() { DataObject = new { email, password } });
        return login.Ok;
    }

    private async Task MarkOnboardedAsync(string email, string password)
    {
        await using var api = await Playwright.APIRequest.NewContextAsync(new() { BaseURL = ApiUrl });
        var login = await api.PostAsync("/login", new() { DataObject = new { email, password } });
        Assert.That(login.Ok, Is.True, "the test account could not sign in: " + await login.TextAsync());
        var token = (await login.JsonAsync())!.Value.GetProperty("accessToken").GetString();

        var done = await api.PostAsync("/api/me/onboarding/complete", new()
        {
            Headers = new Dictionary<string, string> { ["Authorization"] = $"Bearer {token}" },
        });
        Assert.That(done.Ok, Is.True, "could not mark the test account onboarded: " + await done.TextAsync());
    }

    private async Task MakeAccountAsync(string email, string displayName, string password, bool confirmed)
    {
        var token = await SuperAdminTokenAsync();
        Assert.That(token, Is.Not.Null, "the SuperAdmin could not sign in to make a test account");

        using var http = new HttpClient { BaseAddress = new Uri(ApiUrl) };
        http.DefaultRequestHeaders.Authorization = new("Bearer", token);

        var made = await http.PostAsJsonAsync("/api/admin/app-users", new
        {
            email, password, displayName, userName = (string?)null,
            isEmailConfirmed = confirmed, isSuperAdmin = false,
        });
        Assert.That(made.IsSuccessStatusCode, Is.True, "the test account was refused: " + await made.Content.ReadAsStringAsync());
    }
}
