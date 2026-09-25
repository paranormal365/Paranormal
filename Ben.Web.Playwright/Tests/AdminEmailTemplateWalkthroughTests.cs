using System.Net.Http.Json;
using System.Text.RegularExpressions;
using Microsoft.Playwright;
using NUnit.Framework;

namespace Ben.Web.Playwright.Tests;

/// <summary>
/// Every path through the template editor, walked (item 246).
/// </summary>
/// <remarks>
/// <b>Everything is reverted in teardown.</b> Publishing replaces a real letter for the whole
/// site, so a fixture that left one behind would change what every later test — and every later
/// person — receives.
/// </remarks>
[TestFixture]
public class AdminEmailTemplateWalkthroughTests : BenTestBase
{
    private const string Reset = "reset-your-password";
    private const string Confirm = "confirm-your-address";
    private const string CaseMoved = "case-status-changed";

    [SetUp]
    public async Task SignInAsync() => await LoginAsync(SuperAdminEmail, SuperAdminPassword);

    [TearDown]
    public async Task PutEverythingBackAsync()
    {
        if (await SuperAdminTokenAsync() is not { } token) return;
        using var http = new HttpClient { BaseAddress = new Uri(ApiUrl) };
        http.DefaultRequestHeaders.Authorization = new("Bearer", token);
        foreach (var kind in new[] { Reset, Confirm, CaseMoved })
            await http.DeleteAsync($"/api/admin/email-templates/{kind}");
    }

    private async Task OpenAsync(string title)
    {
        await Page.GotoAsync($"{BaseUrl}/admin/email-templates");
        await WaitForTheCircuitAsync();
        await ClickUntilAsync(
            Page.GetByText(title, new() { Exact = true }).First,
            Page.Locator("[data-testid=template-body]"));
    }

    private ILocator Body => Page.Locator("[data-testid=template-body]");
    private ILocator Subject => Page.Locator("[data-testid=template-subject]");
    private ILocator Status => Page.Locator("#admin-email-templates");

    [Test]
    [Description("Every letter the site sends is listed.")]
    public async Task All_the_letters_are_listed()
    {
        await Page.GotoAsync($"{BaseUrl}/admin/email-templates");
        await WaitForTheCircuitAsync();

        await Expect(Page.Locator("[data-testid=template-row]").First).ToBeVisibleAsync();
        var rows = await Page.Locator("[data-testid=template-row]").CountAsync();

        Assert.That(rows, Is.GreaterThanOrEqualTo(30),
            "The list should carry every declared letter, not a handful.");
    }

    [Test]
    [Description("A starter fills both boxes, and the letter it makes can be saved.")]
    public async Task A_starter_produces_something_savable()
    {
        await OpenAsync("Reset your password");

        // The target has to be something that appears only AFTER the click. The supplied-token
        // row is always on screen, so using it meant ClickUntil returned without clicking and the
        // assertion read an untouched box.
        await ClickUntilAsync(
            Page.GetByRole(AriaRole.Button, new() { Name = "Reset a password" }),
            Page.GetByText("Nothing is saved yet", new() { Exact = false }));

        Assert.That(await Subject.InputValueAsync(), Is.Not.Empty);
        Assert.That(await Body.InputValueAsync(), Does.Contain("{ResetButton}"));

        await ClickUntilAsync(
            Page.Locator("[data-testid=template-save]"),
            Page.GetByText("Saved.", new() { Exact = false }));
    }

    [Test]
    [Description("Publishing marks the letter as ours, and reverting gives it back.")]
    public async Task Publish_then_revert_comes_full_circle()
    {
        await OpenAsync("Your case has moved on");

        await Subject.FillAsync("Your case {Cases.Title} has moved on");
        await Body.FillAsync("<p>Hello {AppUsers.DisplayName}, it is {FullDate}.</p>");

        await ClickUntilAsync(
            Page.Locator("[data-testid=template-publish]"),
            Page.GetByText("Published.", new() { Exact = false }));

        // The row says so without a reload.
        await Expect(Page.GetByText("In use since", new() { Exact = false }).First).ToBeVisibleAsync();

        await Page.Locator("[data-testid=template-revert]").ClickAsync();
        await ClickUntilAsync(
            Page.Locator("[data-testid=confirm-revert]"),
            Page.GetByText("back to the one the site writes", new() { Exact = false }));

        await Expect(Page.GetByText("The site's own letter", new() { Exact = false }).First)
            .ToBeVisibleAsync();
    }

    [Test]
    [Description("A letter that needs a link will not publish without one.")]
    public async Task A_confirmation_without_its_link_is_refused()
    {
        await OpenAsync("Confirm your address");

        await Subject.FillAsync("Welcome");
        await Body.FillAsync("<p>Hello {AppUsers.DisplayName}, welcome aboard.</p>");

        await Page.Locator("[data-testid=template-save]").ClickAsync();

        await Expect(Status).ToContainTextAsync("does not work without", new() { Timeout = 15_000 });
    }

    [Test]
    [Description("Publishing an empty letter is refused rather than sending nothing.")]
    public async Task Publishing_nothing_is_refused()
    {
        await OpenAsync("Your case has moved on");

        await Subject.FillAsync("");
        await Body.FillAsync("");

        await Page.Locator("[data-testid=template-publish]").ClickAsync();

        // Either refusal is honest; what must NOT happen is a published empty letter.
        await Expect(Status).ToContainTextAsync("nothing", new() { Timeout = 15_000 });
    }

    [Test]
    [Description("Only tokens this letter can fill in are offered, and only its own tables.")]
    public async Task The_offer_matches_the_letter()
    {
        await OpenAsync("Reset your password");

        var tables = await Page.Locator("#token-table option").AllInnerTextsAsync();
        Assert.That(tables.Where(t => t.Trim().Length > 0 && !t.Contains("Pick")).ToList(),
            Is.EquivalentTo(new[] { "AppUsers" }),
            "A reset letter carries the person and nothing else.");

        await OpenAsync("Your case has moved on");
        tables = await Page.Locator("#token-table option").AllInnerTextsAsync();
        Assert.That(string.Join(",", tables), Does.Contain("Cases"));
    }

    [Test]
    [Description("Moving to another letter shows that letter, not the last one's words.")]
    public async Task Switching_letters_shows_the_new_one()
    {
        await OpenAsync("Reset your password");
        await Body.FillAsync("<p>SOMETHING ONLY HERE</p>");

        // Unsaved work is now guarded, so switching asks first.
        await ClickUntilAsync(
            Page.GetByText("Your case has moved on", new() { Exact = true }).First,
            Page.Locator("[data-testid=leave-anyway]"));

        await ClickUntilAsync(
            Page.Locator("[data-testid=leave-anyway]"),
            Page.GetByText("A visit is booked", new() { Exact = true }).First);

        await Expect(Body).Not.ToHaveValueAsync(new Regex("SOMETHING ONLY HERE"));
    }

    [Test]
    [Description("Each kind of insert puts something in the body.")]
    public async Task Every_insert_writes_into_the_body()
    {
        await OpenAsync("Reset your password");
        await Body.FillAsync("");

        // A piece.
        await Page.Locator("[data-testid=insert-block]").First.ClickAsync();
        await Expect(Body).ToHaveValueAsync(new Regex("<"), new() { Timeout = 10_000 });

        var afterBlock = (await Body.InputValueAsync()).Length;

        // A ready-made token.
        await Page.GetByRole(AriaRole.Button, new() { Name = "{FullDate}" }).ClickAsync();
        await Expect(Body).ToHaveValueAsync(new Regex(@"\{FullDate\}"), new() { Timeout = 10_000 });

        // One the mailer supplies. Waited for, not read straight away: the body is written by a
        // Blazor re-render, and InputValueAsync does not wait for one.
        await Page.Locator("[data-testid=insert-supplied]").First.ClickAsync();
        await Expect(Body).ToHaveValueAsync(new Regex(@"\{Reset"), new() { Timeout = 10_000 });

        var final = await Body.InputValueAsync();
        Assert.That(final.Length, Is.GreaterThan(afterBlock));
        Assert.That(final, Does.Contain("{Reset"));
    }

    /// <summary>
    /// The list and the editor share a screen, so a wrong click is one pixel from your work.
    /// </summary>
    [Test]
    [Description("Unsaved work is not thrown away by clicking another letter.")]
    public async Task Leaving_with_unsaved_changes_asks_first()
    {
        await OpenAsync("Reset your password");
        await Body.FillAsync("<p>WORK IN PROGRESS</p>");

        await ClickUntilAsync(
            Page.GetByText("Your case has moved on", new() { Exact = true }).First,
            Page.Locator("[data-testid=leave-anyway]"));

        // Staying keeps what was written.
        await Page.GetByRole(AriaRole.Button, new() { Name = "Stay here" }).ClickAsync();
        await Expect(Body).ToHaveValueAsync(new Regex("WORK IN PROGRESS"));
    }

    [Test]
    [Description("Saving makes the boxes the new baseline, so switching no longer asks.")]
    public async Task After_saving_moving_on_does_not_ask()
    {
        await OpenAsync("Your case has moved on");
        await Subject.FillAsync("A subject");
        await Body.FillAsync("<p>Hello {AppUsers.DisplayName}.</p>");

        await ClickUntilAsync(
            Page.Locator("[data-testid=template-save]"),
            Page.GetByText("Saved.", new() { Exact = false }));

        await ClickUntilAsync(
            Page.GetByText("A visit is booked", new() { Exact = true }).First,
            Page.GetByText("Tells a client when somebody is coming", new() { Exact = false }).First);

        await Expect(Page.Locator("[data-testid=leave-anyway]")).ToHaveCountAsync(0);
    }

    /// <summary>
    /// The whole point of the feature, proved end to end.
    /// </summary>
    /// <remarks>
    /// Everything else here tests the editor. This publishes a template, makes the site actually
    /// send that kind of letter, and reads back what the recipient would have got. Without it the
    /// feature could be perfectly usable and change nothing anybody receives — which is exactly
    /// the failure this codebase keeps finding under the name "write-only".
    /// </remarks>
    [Test]
    [Description("A published template is what the next letter of that kind actually says.")]
    public async Task A_published_template_reaches_the_letter_that_is_sent()
    {
        const string marker = "WRITTEN-BY-A-TEMPLATE";

        await OpenAsync("Reset your password");
        await Subject.FillAsync($"{marker} for {{AppUsers.DisplayName}}");
        await Body.FillAsync($"<p>{marker}. Use this: {{ResetButton}}</p>");

        await ClickUntilAsync(
            Page.Locator("[data-testid=template-publish]"),
            Page.GetByText("Published.", new() { Exact = false }));

        // Make the site send one, through the door a person would use.
        await LogoutAsync();
        await Page.GotoAsync($"{BaseUrl}/forgot-password");
        await Expect(Page.Locator("#forgot-email")).ToBeVisibleAsync(new() { Timeout = 15_000 });
        await FillAndConfirmAsync("#forgot-email", UserEmail);
        await ClickUntilAsync(
            Page.GetByRole(AriaRole.Button, new() { Name = "Send reset link", Exact = false }),
            Page.GetByText("reset link is on its way", new() { Exact = false }));

        // And read what was queued.
        var token = await SuperAdminTokenAsync();
        Assert.That(token, Is.Not.Null, "Could not sign in to read the outbox.");

        using var http = new HttpClient { BaseAddress = new Uri(ApiUrl) };
        http.DefaultRequestHeaders.Authorization = new("Bearer", token);

        var rows = await http.GetFromJsonAsync<List<OutboxRow>>(
            "/api/admin/mail/outbox?take=25");

        Assert.That(rows, Is.Not.Null);

        var mine = rows!.FirstOrDefault(r => r.Subject.Contains(marker));
        Assert.That(mine, Is.Not.Null,
            "The letter that went did not use the published template. Subjects seen: "
          + string.Join(" | ", rows!.Take(5).Select(r => r.Subject)));

        Assert.That(mine!.Kind, Is.EqualTo("reset-your-password"),
            "The letter should be filed under the kind it declared, not a guess from its subject.");

        var body = await http.GetFromJsonAsync<OutboxBody>(
            $"/api/admin/mail/outbox/{mine.Id}/body");

        Assert.That(body!.Html, Does.Contain(marker));

        // The supplied token became a real button, not the literal text.
        Assert.That(body.Html, Does.Not.Contain("{ResetButton}"));
        Assert.That(body.Html, Does.Contain("<a href="));
    }

    private sealed record OutboxRow(Guid Id, string To, string Subject, string Kind);
    private sealed record OutboxBody(Guid Id, string? Html);

    /// <summary>The editor is usable on the widths the rest of the site is tested at.</summary>
    [TestCase(375, 812)]
    [TestCase(768, 1024)]
    [TestCase(1280, 800)]
    [Description("The editor fits its width, with nothing running off the side.")]
    public async Task It_fits(int width, int height)
    {
        await Page.SetViewportSizeAsync(width, height);
        await OpenAsync("Reset your password");

        // Nothing pushes the page sideways.
        var overflows = await Page.EvaluateAsync<bool>(
            "() => document.documentElement.scrollWidth > document.documentElement.clientWidth + 1");
        Assert.That(overflows, Is.False, $"The page scrolls sideways at {width}px.");

        // The controls that matter are reachable, not merely present.
        foreach (var testid in new[] { "template-subject", "template-body", "template-save",
                                       "template-preview", "template-publish" })
            await Expect(Page.Locator($"[data-testid={testid}]")).ToBeVisibleAsync();

        // Both dropdowns are still usable rather than collapsed to nothing.
        foreach (var id in new[] { "#token-table", "#token-column" })
        {
            var box = await Page.Locator(id).BoundingBoxAsync();
            Assert.That(box, Is.Not.Null);
            Assert.That(box!.Width, Is.GreaterThan(80), $"{id} is unusably narrow at {width}px.");
        }
    }
}
