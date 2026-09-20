using System.Text.RegularExpressions;
using Microsoft.Playwright;
using NUnit.Framework;

namespace Ben.Web.Playwright.Tests;

/// <summary>
/// Writing a letter: the two dropdowns, the token they build, and the preview (item 246).
/// </summary>
/// <remarks>
/// <b>Reverted in teardown.</b> A published template replaces a real letter for the whole site, so
/// a fixture that left one behind would change what every later test — and every later person —
/// receives.
/// </remarks>
[TestFixture]
public class AdminEmailTemplateTests : BenTestBase
{
    private const string Kind = "reset-your-password";

    [SetUp]
    public async Task SignInAsync() => await LoginAsync(SuperAdminEmail, SuperAdminPassword);

    [TearDown]
    public async Task PutTheSitesLetterBackAsync()
    {
        if (await SuperAdminTokenAsync() is not { } token) return;
        var http = new HttpClient { BaseAddress = new Uri(ApiUrl) };
        http.DefaultRequestHeaders.Authorization = new("Bearer", token);
        await http.DeleteAsync($"/api/admin/email-templates/{Kind}");
    }

    private async Task OpenTheResetLetterAsync()
    {
        await Page.GotoAsync($"{BaseUrl}/admin/email-templates");
        await WaitForTheCircuitAsync();
        await Expect(Page.Locator("[data-testid=template-row]").First)
            .ToBeVisibleAsync(new() { Timeout = 30_000 });

        await ClickUntilAsync(
            Page.GetByText("Reset your password", new() { Exact = true }).First,
            Page.Locator("[data-testid=template-body]"));
    }

    [Test]
    [Description("Picking a table fills the column list, and Add token writes it into the body.")]
    public async Task The_two_dropdowns_build_a_token()
    {
        await OpenTheResetLetterAsync();

        await Page.SelectOptionAsync("#token-table", "AppUsers");
        await Expect(Page.Locator("#token-column option")).Not.ToHaveCountAsync(0);

        await Page.SelectOptionAsync("#token-column", "DisplayName");
        await Page.Locator("[data-testid=insert-token]").ClickAsync();

        await Expect(Page.Locator("[data-testid=last-token]"))
            .ToContainTextAsync("{AppUsers.DisplayName}");
        await Expect(Page.Locator("[data-testid=template-body]"))
            .ToHaveValueAsync(new Regex(@"\{AppUsers\.DisplayName\}"));
    }

    /// <summary>
    /// The gate that is not visible in the dropdown, asserted where somebody would look.
    /// </summary>
    /// <remarks>
    /// AppUser derives from ASP.NET Identity's IdentityUser, so PasswordHash is a real column on a
    /// table this letter legitimately carries. It must not be offerable.
    /// </remarks>
    [Test]
    [Description("A password hash is never one of the columns on offer.")]
    public async Task The_secret_columns_are_not_on_offer()
    {
        await OpenTheResetLetterAsync();
        await Page.SelectOptionAsync("#token-table", "AppUsers");

        // The second dropdown is filled by a Blazor re-render, so it is waited for rather than
        // read straight away — read immediately it holds only its placeholder, and the assertion
        // below would pass for the wrong reason.
        await Expect(Page.Locator("#token-column option")).Not.ToHaveCountAsync(1);

        var columns = await Page.Locator("#token-column option").AllInnerTextsAsync();
        var joined = string.Join(" ", columns);

        Assert.That(joined, Does.Contain("DisplayName"));
        Assert.That(joined, Does.Not.Contain("PasswordHash"));
        Assert.That(joined, Does.Not.Contain("SecurityStamp"));
    }

    [Test]
    [Description("A preview fills the tokens in with made-up details, in a sealed frame.")]
    public async Task A_preview_fills_the_tokens_in()
    {
        await OpenTheResetLetterAsync();

        await Page.Locator("[data-testid=template-subject]").FillAsync("Hello {AppUsers.DisplayName}");
        await Page.Locator("[data-testid=template-body]")
            .FillAsync("<p>It is {FullDate}. — {SiteName}</p>");

        await ClickUntilAsync(
            Page.Locator("[data-testid=template-preview]"),
            Page.Locator("[data-testid=preview-subject]"));

        await Expect(Page.Locator("[data-testid=preview-subject]"))
            .ToContainTextAsync("Marguerite Ashdown");

        var frame = Page.Locator("[data-testid=preview-body]");
        Assert.That(await frame.GetAttributeAsync("sandbox"), Is.EqualTo(string.Empty),
            "The preview must be sandboxed with nothing allowed.");

        var inside = Page.FrameLocator("[data-testid=preview-body]").Locator("body");
        await Expect(inside).ToContainTextAsync("September 20, 2026");
    }

    /// <summary>
    /// The refusal that makes rendering a missing token as nothing safe.
    /// </summary>
    [Test]
    [Description("A token this letter could never fill in is refused when saving.")]
    public async Task A_token_the_letter_cannot_fill_in_is_refused()
    {
        await OpenTheResetLetterAsync();

        await Page.Locator("[data-testid=template-subject]").FillAsync("Hello");
        await Page.Locator("[data-testid=template-body]").FillAsync("<p>{Cases.Title}</p>");

        await Page.Locator("[data-testid=template-save]").ClickAsync();

        await Expect(Page.Locator("#admin-email-templates"))
            .ToContainTextAsync("cannot fill in", new() { Timeout = 15_000 });
    }
}
