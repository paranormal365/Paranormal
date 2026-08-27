using Microsoft.Playwright;
using NUnit.Framework;
using System.Text.Json;

namespace Ben.Web.Playwright.Tests;

/// <summary>
/// Authors a CMS page and then reads it back as a signed-out visitor.
/// </summary>
/// <remarks>
/// <para>
/// The two halves belong in one test on purpose. Public-facing features in this app have broken
/// on the anonymous path more than once while looking perfectly fine to the person who wrote
/// them — the author is signed in, so they see content a visitor never gets. Checking the
/// authoring side alone would reproduce exactly that blind spot.
/// </para>
/// <para>
/// The page is created by the test rather than seeded. That covers the authoring flow as well,
/// and it means the test does not depend on dev data that currently has no CMS pages at all.
/// </para>
/// </remarks>
[TestFixture]
[Category("Cms")]
public class CmsAuthoringTests : BenTestBase
{
    private string _orgId = "";
    private string _urlName = "";

    private async Task ResolveOrgAsync()
    {
        var login = await Page.APIRequest.PostAsync($"{ApiUrl}/login",
            new() { DataObject = new { email = SuperAdminEmail, password = SuperAdminPassword } });
        var token = (await login.JsonAsync())?.GetProperty("accessToken").GetString() ?? "";

        var response = await Page.APIRequest.GetAsync($"{ApiUrl}/api/organizations",
            new() { Headers = new Dictionary<string, string> { ["Authorization"] = $"Bearer {token}" } });
        var orgs = await response.JsonAsync();

        foreach (var org in orgs!.Value.EnumerateArray())
        {
            if (org.TryGetProperty("urlName", out var u) && u.GetString() is { Length: > 0 } urlName)
            {
                _orgId = org.GetProperty("id").GetString()!;
                _urlName = urlName;
                return;
            }
        }
    }

    [Test]
    public async Task AuthoredPage_IsVisibleToASignedOutVisitor()
    {
        await LoginAsync(SuperAdminEmail, SuperAdminPassword);
        await ResolveOrgAsync();
        if (_orgId.Length == 0) Assert.Ignore("no organisation with a url name in the seed data");

        // Unique per run: these tests write real rows, and a fixed slug would collide with itself.
        var stamp = Guid.NewGuid().ToString("N")[..8];
        var slug = $"playwright-{stamp}";
        var title = $"Playwright page {stamp}";
        var intro = $"Written by an automated test at {DateTime.UtcNow:O}.";

        await Page.GotoAsync($"{BaseUrl}/organizations/{_orgId}/cms");
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);
        await WaitUntilLoadedAsync();

        // ── Author it ────────────────────────────────────────────────────────
        var dialog = Page.Locator(".modal.show");

        // Wait for the button before trying to click it. WaitUntilLoadedAsync returns once the
        // circuit is up, which is not the same as this page having rendered its toolbar — under a
        // full-suite load the CMS list can still be arriving, and ClickUntilAsync then spends all
        // four of its attempts clicking nothing and fails pointing at the MODAL, which was never
        // the thing missing. Waiting on a signal the page itself produces is the rule this
        // codebase keeps relearning (flaked exactly once in 401 tests on 2026-08-27, and passed
        // alone in ten seconds).
        var newPage = Main.GetByRole(AriaRole.Button, new() { Name = "New Page" }).First;
        await Expect(newPage).ToBeVisibleAsync(new() { Timeout = 30_000 });

        await ClickUntilAsync(newPage, dialog);

        await dialog.GetByLabel("Title", new() { Exact = false }).First.FillAsync(title);
        await dialog.GetByLabel("URL Slug", new() { Exact = false }).First.FillAsync(slug);
        await dialog.GetByLabel("Summary", new() { Exact = false }).First.FillAsync(intro);

        // "Visible to public" is what decides whether a visitor may see it at all.
        var publicToggle = dialog.Locator("#pg-public");
        if (await publicToggle.CountAsync() > 0) await publicToggle.CheckAsync();

        await dialog.GetByRole(AriaRole.Button, new() { Name = "Save", Exact = false }).First.ClickAsync();
        await Expect(dialog).ToBeHiddenAsync(new() { Timeout = 10_000 });

        // The new page should now be listed.
        await Expect(Main.GetByText(title, new() { Exact = false }).First)
            .ToBeVisibleAsync(new() { Timeout = 10_000 });

        // ── Publish it ───────────────────────────────────────────────────────
        // "Published" only appears when editing, so this is a second pass over the same dialog.
        var row = Main.Locator("tr", new() { HasTextString = title }).First;
        // Edit lives behind the row's More-actions dropdown since the one-line Actions cell
        // (item 169) — the row's first button is Sections, which NAVIGATES.
        await ClickUntilAsync(row.GetByRole(AriaRole.Button, new() { Name = "More actions" }),
            row.GetByRole(AriaRole.Button, new() { Name = "Edit", Exact = true }));
        await ClickUntilAsync(row.GetByRole(AriaRole.Button, new() { Name = "Edit", Exact = true }), dialog);

        var publishedToggle = dialog.Locator("#pg-published");
        Assert.That(await publishedToggle.CountAsync(), Is.GreaterThan(0),
            "the edit dialog did not offer a Published control");
        await publishedToggle.CheckAsync();

        await dialog.GetByRole(AriaRole.Button, new() { Name = "Save", Exact = false }).First.ClickAsync();
        await Expect(dialog).ToBeHiddenAsync(new() { Timeout = 10_000 });

        // ── Read it back with no session at all ──────────────────────────────
        // A fresh context rather than a sign-out: it carries no cookie and no circuit, which is
        // what a stranger following a link actually has.
        var visitor = await Page.Context.Browser!.NewContextAsync();
        try
        {
            var visitorPage = await visitor.NewPageAsync();
            await visitorPage.GotoAsync($"{BaseUrl}/o/{_urlName}/{slug}");
            await visitorPage.WaitForLoadStateAsync(LoadState.NetworkIdle);

            var body = await visitorPage.InnerTextAsync("body");

            Assert.Multiple(() =>
            {
                Assert.That(body, Does.Not.Contain("Page not found"),
                    $"/o/{_urlName}/{slug} did not resolve for a signed-out visitor");
                Assert.That(body, Does.Not.Contain("An unhandled error has occurred"));
                Assert.That(body, Does.Contain(title),
                    "the published page's title was not shown to a signed-out visitor");
            });
        }
        finally
        {
            await visitor.CloseAsync();
            await DeletePageAsync(title);
        }
    }

    [Test]
    public async Task UnpublishedPage_IsNotVisibleToASignedOutVisitor()
    {
        await LoginAsync(SuperAdminEmail, SuperAdminPassword);
        await ResolveOrgAsync();
        if (_orgId.Length == 0) Assert.Ignore("no organisation with a url name in the seed data");

        var stamp = Guid.NewGuid().ToString("N")[..8];
        var slug = $"playwright-draft-{stamp}";
        var title = $"Playwright draft {stamp}";

        await Page.GotoAsync($"{BaseUrl}/organizations/{_orgId}/cms");
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);
        await WaitUntilLoadedAsync();

        var dialog = Page.Locator(".modal.show");

        // Wait for the button before trying to click it. WaitUntilLoadedAsync returns once the
        // circuit is up, which is not the same as this page having rendered its toolbar — under a
        // full-suite load the CMS list can still be arriving, and ClickUntilAsync then spends all
        // four of its attempts clicking nothing and fails pointing at the MODAL, which was never
        // the thing missing. Waiting on a signal the page itself produces is the rule this
        // codebase keeps relearning (flaked exactly once in 401 tests on 2026-08-27, and passed
        // alone in ten seconds).
        var newPage = Main.GetByRole(AriaRole.Button, new() { Name = "New Page" }).First;
        await Expect(newPage).ToBeVisibleAsync(new() { Timeout = 30_000 });

        await ClickUntilAsync(newPage, dialog);
        await dialog.GetByLabel("Title", new() { Exact = false }).First.FillAsync(title);
        await dialog.GetByLabel("URL Slug", new() { Exact = false }).First.FillAsync(slug);
        await dialog.GetByRole(AriaRole.Button, new() { Name = "Save", Exact = false }).First.ClickAsync();
        await Expect(dialog).ToBeHiddenAsync(new() { Timeout = 10_000 });

        // Prove the draft exists before asserting a visitor cannot see it — otherwise this passes
        // just as happily when the page was never created, which is the classic vacuous negative.
        await Expect(Main.GetByText(title, new() { Exact = false }).First)
            .ToBeVisibleAsync(new() { Timeout = 10_000 });

        // Never published, so a visitor must not get it. This is the half that matters: the
        // author can see their own draft, and that is exactly why it needs checking from outside.
        var visitor = await Page.Context.Browser!.NewContextAsync();
        try
        {
            var visitorPage = await visitor.NewPageAsync();
            await visitorPage.GotoAsync($"{BaseUrl}/o/{_urlName}/{slug}");
            await visitorPage.WaitForLoadStateAsync(LoadState.NetworkIdle);

            var body = await visitorPage.InnerTextAsync("body");
            Assert.That(body, Does.Not.Contain(title),
                "an unpublished page was shown to a signed-out visitor");
        }
        finally
        {
            await visitor.CloseAsync();
            await DeletePageAsync(title);
        }
    }

    /// <summary>
    /// Removes a page this fixture created.
    /// </summary>
    /// <remarks>
    /// These run against shared dev data and each run writes a real page. Without this the
    /// organisation's page list fills with "Playwright page …" entries, and the data the rest of
    /// the suite reads stops resembling anything a person would have.
    /// </remarks>
    private async Task DeletePageAsync(string title)
    {
        try
        {
            await DeletePageCoreAsync(title);
        }
        catch (Exception ex)
        {
            // Best effort. Tidying up is not what these tests are about, and a cleanup that throws
            // would either redden a passing test or bury the real failure underneath it.
            TestContext.Out.WriteLine($"could not remove the test page \"{title}\": {ex.Message.Split('\n')[0]}");
        }
    }

    private async Task DeletePageCoreAsync(string title)
    {
        await Page.GotoAsync($"{BaseUrl}/organizations/{_orgId}/cms");
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);
        await WaitUntilLoadedAsync();

        var row = Main.Locator("tr", new() { HasTextString = title }).First;
        if (await row.CountAsync() == 0) return;

        var confirm = Page.Locator(".modal.show");
        // Delete sits in the row's More-actions dropdown since item 169.
        await ClickUntilAsync(row.GetByRole(AriaRole.Button, new() { Name = "More actions" }),
            row.GetByRole(AriaRole.Button, new() { Name = "Delete", Exact = true }));
        var deleteButton = row.GetByRole(AriaRole.Button, new() { Name = "Delete", Exact = true });
        if (await deleteButton.CountAsync() == 0) return;

        await ClickUntilAsync(deleteButton, confirm);
        await confirm.GetByRole(AriaRole.Button, new() { Name = "Delete", Exact = true }).First.ClickAsync();

        // Waiting on the row, not on the text: the page reports what it deleted, so the title is
        // still on screen afterwards and asserting its absence would fail on a successful delete.
        await Expect(Main.Locator("tr", new() { HasTextString = title }))
            .ToHaveCountAsync(0, new() { Timeout = 15_000 });
    }
}
