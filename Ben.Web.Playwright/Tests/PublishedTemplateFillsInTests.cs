using System.Net.Http.Json;
using Microsoft.Playwright;
using NUnit.Framework;

namespace Ben.Web.Playwright.Tests;

/// <summary>
/// A published template's tokens are filled in on the letter that actually goes out (item 246).
/// </summary>
/// <remarks>
/// <para><b>Why a browser test.</b> The editor's own tests prove what the editor accepts and what
/// its preview shows — and the preview fills every token with made-up details, so it looked right
/// the whole time the real letters would have gone out blank. On 2026-09-23 seven templates were
/// published on production for letters whose senders handed a template no rows at all. This runs
/// the whole path: publish, somebody does the thing that sends the letter, read what was queued.</para>
///
/// <para><b>The letter.</b> "Somebody tried to use your address" — sent when a sign-up names an
/// address that already has an account. It is one of the seven, and the only one a stranger can
/// cause from a public page.</para>
///
/// <para><b>Reverted in teardown.</b> A published template replaces the letter for the whole site.</para>
/// </remarks>
[TestFixture]
public class PublishedTemplateFillsInTests : BenTestBase
{
    private const string Kind = "somebody-used-your-address";
    private const string Marker = "TEMPLATE-FILLED-IN";

    [TearDown]
    public async Task PutTheSitesLetterBackAsync()
    {
        if (await SuperAdminTokenAsync() is not { } token) return;
        using var http = new HttpClient { BaseAddress = new Uri(ApiUrl) };
        http.DefaultRequestHeaders.Authorization = new("Bearer", token);
        await http.DeleteAsync($"/api/admin/email-templates/{Kind}");
    }

    [Test]
    [Description("A published template names the account holder and carries the sign-in link.")]
    public async Task The_warning_letter_names_its_reader()
    {
        await PublishAsync(
            subject: "{AppUsers.DisplayName}, your address was used",
            body: $"<p>{Marker} Hello {{AppUsers.DisplayName}}, somebody signed up as {{AppUsers.Email}}.</p>{{SignInButton}}");

        var tag = Guid.NewGuid().ToString("N")[..8];
        var email = $"holder{tag}@example.com";
        var name = $"Holder {tag}";

        // The account the address belongs to, then a second sign-up with the same address.
        await SignUpAsync($"holder{tag}", name, email);
        await SignUpAsync($"prober{tag}", $"Prober {tag}", email);

        var letter = await TryLetterFromTheOutboxAsync(email, html => html.Contains(Marker));
        Assert.That(letter, Is.Not.Null,
            $"No letter to {email} was written from the published template. The second sign-up should "
          + "have warned the holder, in the template's words.");

        var text = System.Net.WebUtility.HtmlDecode(letter!);
        Assert.Multiple(() =>
        {
            Assert.That(text, Does.Contain($"Hello {name}, somebody signed up as {email}."),
                "The template's table tokens rendered blank: the letter handed it no AppUsers row.");
            Assert.That(text, Does.Contain("/login"),
                "The template asked for the sign-in button and the letter carried no link.");
        });
    }

    private async Task SignUpAsync(string handle, string displayName, string email)
    {
        await Page.GotoAsync($"{BaseUrl}/signup");
        await Expect(Page.Locator("#signup-handle")).ToBeVisibleAsync(new() { Timeout = 15_000 });
        await TypeHandleAsync(handle);
        await Expect(Page.GetByText("is free.")).ToBeVisibleAsync(new() { Timeout = 15_000 });
        await FillAndConfirmAsync("#signup-first", "Template");
        await FillAndConfirmAsync("#signup-last", "Reader");
        await FillAndConfirmAsync("#signup-name", displayName);
        await FillAndConfirmAsync("#signup-email", email);
        await FillAndConfirmAsync("#signup-password", NewTestPassword());
        await Page.GetByRole(AriaRole.Button, new() { Name = "Create account" }).ClickAsync();

        // The same answer either way, on purpose: an existing address is never revealed.
        await Expect(Page.GetByText("Check your email").First).ToBeVisibleAsync(new() { Timeout = 20_000 });
    }

    /// <summary>Saves the draft and publishes it, as the SuperAdmin, through the editor's own API.</summary>
    private async Task PublishAsync(string subject, string body)
    {
        var token = await SuperAdminTokenAsync();
        Assert.That(token, Is.Not.Null, "the SuperAdmin could not sign in to publish a template");

        using var http = new HttpClient { BaseAddress = new Uri(ApiUrl) };
        http.DefaultRequestHeaders.Authorization = new("Bearer", token);

        var saved = await http.PutAsJsonAsync($"/api/admin/email-templates/{Kind}/draft",
            new { subject, bodyHtml = body });
        Assert.That(saved.IsSuccessStatusCode, Is.True, "the draft was refused: " + await saved.Content.ReadAsStringAsync());

        var published = await http.PostAsync($"/api/admin/email-templates/{Kind}/publish", null);
        Assert.That(published.IsSuccessStatusCode, Is.True, "publishing was refused: " + await published.Content.ReadAsStringAsync());
    }
}
