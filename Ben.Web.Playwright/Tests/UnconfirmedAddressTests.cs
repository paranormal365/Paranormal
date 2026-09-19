using System.Text.Json;
using Microsoft.Playwright;
using NUnit.Framework;

namespace Ben.Web.Playwright.Tests;

/// <summary>
/// The UI path for an account whose address is still only a claim.
/// </summary>
/// <remarks>
/// <para>Since Phase C of the external sign-in plan (2026-09-10), an account created from a
/// provider's unverified address — Microsoft's, in practice — starts unconfirmed and can still
/// use the site through that provider. Nothing else on the site would ever tell such a person why
/// a password-reset link never arrives, so two things do: a banner under the announcement, once
/// per sign-in, and a notice on the profile that can send the confirmation again.</para>
///
/// <para><b>How the state is reached here.</b> A real one needs a Microsoft round trip, which the
/// harness cannot make. So a seeded, confirmed person signs in, and then the SuperAdmin seat —
/// through the product's own admin endpoint, not the database — marks the address unconfirmed
/// underneath the live session. That is the same state a Microsoft-made account is in, and the
/// site is asked what it shows. The flag is put back in tear-down whatever happened.</para>
///
/// <para>The order matters: the flip must come <i>after</i> the password sign-in, because a
/// password sign-in against an unconfirmed account is refused. That refusal is the rule this UI
/// path exists to explain.</para>
/// </remarks>
[TestFixture]
[Category("UnconfirmedAddress")]
[NonParallelizable]
public sealed class UnconfirmedAddressTests : BenTestBase
{
    private IAPIRequestContext? _api;
    private string? _adminToken;
    private JsonElement _subject;
    private bool _flipped;

    [SetUp]
    public async Task SignInThenTakeTheConfirmationAway()
    {
        await LoginAsync(UserEmail, UserPassword);

        _api = await Playwright.APIRequest.NewContextAsync(new() { BaseURL = ApiUrl });
        var login = await _api.PostAsync("/login", new()
        {
            DataObject = new { email = SuperAdminEmail, password = SuperAdminPassword },
        });
        Assert.That(login.Ok, Is.True, "the admin seat should be able to sign in to flip the flag");
        _adminToken = (await login.JsonAsync())!.Value.GetProperty("accessToken").GetString();

        var users = await _api.GetAsync("/api/admin/app-users", Authed());
        Assert.That(users.Ok, Is.True, await users.TextAsync());
        var list = (await users.JsonAsync())!.Value;
        var rows = list.ValueKind == JsonValueKind.Array ? list : list.GetProperty("items");
        _subject = rows.EnumerateArray().FirstOrDefault(u =>
            string.Equals(u.GetProperty("email").GetString(), UserEmail, StringComparison.OrdinalIgnoreCase));
        if (_subject.ValueKind == JsonValueKind.Undefined)
            Assert.Ignore($"the seed has no account for {UserEmail}");

        await SetConfirmedAsync(false);
        _flipped = true;
    }

    [TearDown]
    public async Task GiveTheConfirmationBack()
    {
        if (_flipped) await SetConfirmedAsync(true);
        if (_api is not null) await _api.DisposeAsync();
    }

    /// <summary>
    /// Signed in, the site says the address is unconfirmed — on the home page, and on the
    /// profile, where the link can be sent again and the server's neutral sentence comes back.
    /// </summary>
    [Test]
    public async Task An_unconfirmed_address_is_named_and_the_link_can_be_sent_again()
    {
        // A fresh page load: the banner asks once per circuit, and the sign-in above happened in
        // a circuit that saw a confirmed address.
        await Page.GotoAsync($"{BaseUrl}/");
        var banner = Page.Locator("#confirm-address-banner");
        await Expect(banner).ToBeVisibleAsync(new() { Timeout = 15_000 });
        await Expect(banner).ToContainTextAsync(UserEmail);
        await Expect(banner).ToContainTextAsync("password reset");
        await ShotAsync("home-banner");

        await Page.GotoAsync($"{BaseUrl}/profile");
        var notice = Page.Locator("#profile-confirm-address");
        await Expect(notice).ToBeVisibleAsync(new() { Timeout = 15_000 });
        await Expect(notice).ToContainTextAsync(UserEmail);

        var resend = Page.Locator("#profile-resend-confirmation");
        await ClickUntilAsync(resend, notice.Locator("span.text-secondary"));
        await Expect(notice.Locator("span.text-secondary"))
            .ToContainTextAsync("If that address has an unconfirmed account", new() { Timeout = 15_000 });
        await ShotAsync("profile-resent");
    }

    /// <summary>Opt-in proof for a reviewer: <c>BEN_UNCONFIRMED_SHOT=/some/dir</c> writes PNGs there.</summary>
    private async Task ShotAsync(string name)
    {
        if (Environment.GetEnvironmentVariable("BEN_UNCONFIRMED_SHOT") is not { Length: > 0 } dir) return;
        Directory.CreateDirectory(dir);
        await Page.ScreenshotAsync(new() { Path = Path.Combine(dir, $"{name}.png"), FullPage = false });
    }

    /// <summary>The banner can be put away for the session, and stays away on the next page.</summary>
    [Test]
    public async Task The_banner_can_be_dismissed_for_the_session()
    {
        await Page.GotoAsync($"{BaseUrl}/");
        var banner = Page.Locator("#confirm-address-banner");
        await Expect(banner).ToBeVisibleAsync(new() { Timeout = 15_000 });

        await ClickUntilAsync(banner.Locator("button.btn-close"), Page.Locator("body:not(:has(#confirm-address-banner))"));
        await Expect(banner).ToHaveCountAsync(0);

        await Page.GotoAsync($"{BaseUrl}/profile");
        await Expect(Page.Locator("#profile-confirm-address")).ToBeVisibleAsync(new() { Timeout = 15_000 });
        await Expect(Page.Locator("#confirm-address-banner")).ToHaveCountAsync(0);
    }

    private APIRequestContextOptions Authed() =>
        new() { Headers = new Dictionary<string, string> { ["Authorization"] = $"Bearer {_adminToken}" } };

    /// <summary>
    /// Echoes the record back through the admin profile update with only the confirmation flag
    /// changed. UserName and Email are sent null, which that endpoint reads as "keep".
    /// </summary>
    private async Task SetConfirmedAsync(bool confirmed)
    {
        var id = _subject.GetProperty("id").GetString();
        var response = await _api!.PutAsync($"/api/admin/app-users/{id}/profile", new()
        {
            Headers = Authed().Headers,
            DataObject = new
            {
                displayName        = _subject.GetProperty("displayName").GetString(),
                userName           = (string?)null,
                email              = (string?)null,
                phoneNumber        = _subject.GetProperty("phoneNumber").GetString(),
                isEmailConfirmed   = confirmed,
                isTwoFactorEnabled = _subject.GetProperty("isTwoFactorEnabled").GetBoolean(),
                isLockoutEnabled   = _subject.GetProperty("isLockoutEnabled").GetBoolean(),
                lockoutEnd         = _subject.GetProperty("lockoutEnd").ValueKind == JsonValueKind.Null
                                       ? null : _subject.GetProperty("lockoutEnd").GetString(),
                dateCreated        = _subject.GetProperty("dateCreated").GetString(),
                dateUpdated        = _subject.GetProperty("dateUpdated").ValueKind == JsonValueKind.Null
                                       ? null : _subject.GetProperty("dateUpdated").GetString(),
            },
        });
        Assert.That(response.Ok, Is.True, $"could not set EmailConfirmed={confirmed}: {await response.TextAsync()}");
    }
}
