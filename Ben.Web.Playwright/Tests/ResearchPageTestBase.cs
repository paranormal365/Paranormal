using System.Text.RegularExpressions;
using Microsoft.Playwright;
using NUnit.Framework;

namespace Ben.Web.Playwright.Tests;

/// <summary>
/// What the research-page fixtures share: reaching a case's Research tab as Sarah, making a page with a name nobody else
/// uses, and taking it away again afterwards.
/// </summary>
/// <remarks>
/// Every test makes its own page on the seeded Belmont case, so no test depends on another's leftovers and none edits a
/// page another is reading. Pages are deleted through the API after each test (as Sarah, who made them) so a run leaves
/// the case as it found it even when the test failed half way.
/// </remarks>
public abstract class ResearchPageTestBase : BenTestBase
{
    private static readonly SemaphoreSlim TokenLock = new(1, 1);
    private static string? _sarahToken;
    private readonly List<string> _madePages = [];

    /// <summary>A research page's address: /organizations/{org}/cases/{case}/research/{entry}.</summary>
    protected static readonly Regex PageUrl = new(@"/organizations/(?<org>[0-9a-f\-]{36})/cases/(?<case>[0-9a-f\-]{36})/research/(?<entry>[0-9a-f\-]{36})");

    protected ILocator BlockPage => Page.Locator("[data-testid=block-page]");
    protected ILocator SaveStatus => Page.Locator("[data-testid=save-status]");

    /// <summary>Signs in as Sarah and opens the Belmont case's Research tab.</summary>
    protected async Task OpenResearchTabAsync()
    {
        await LoginAsync(UserEmail, UserPassword);   // Sarah — Paranormal365 administrator
        if (!await OpenOrgCaseAsync("Paranormal365", "Belmont"))
            Assert.Ignore("The seeded Belmont case is not in this database.");

        await Page.GotoAsync(Page.Url.Split('?')[0] + "?tab=research");
        await WaitForTheCircuitAsync();
        await Expect(Page.Locator("#research-new-page")).ToBeVisibleAsync(new() { Timeout = 20_000 });
    }

    /// <summary>New page from the Research tab; returns the page's address once it is open and editable.</summary>
    protected async Task<string> CreatePageAsync(string title)
    {
        var titleBox = Page.Locator("#research-new-page-title");
        await ClickUntilAsync(Page.Locator("#research-new-page"), titleBox);
        await titleBox.FillAsync(title);
        await Expect(Page.Locator("#research-new-page-create")).ToBeEnabledAsync();
        await Page.Locator("#research-new-page-create").ClickAsync();

        await Page.WaitForURLAsync(PageUrl, new() { Timeout = 20_000 });
        _madePages.Add(Page.Url);
        await WaitForTheCircuitAsync();
        await Expect(BlockPage).ToBeVisibleAsync(new() { Timeout = 20_000 });
        return Page.Url;
    }

    /// <summary>Adds a text block from the Add bar and types into its editor.</summary>
    protected async Task AddTextAsync(string words)
    {
        var editors = Page.Locator("[data-testid=text-block-editor] .k-editor [contenteditable='true']");
        await ClickUntilAsync(Page.Locator("[data-testid=block-add-text]"), editors);
        await editors.First.ClickAsync();
        await Page.Keyboard.TypeAsync(words);
    }

    /// <summary>Save now, and wait until the status says it is saved.</summary>
    protected Task SaveNowAsync() => SaveNowAsync(Page);

    /// <summary>
    /// Presses Save now and waits for that save to finish. Waiting for "Clean" alone is not enough: a page already saved
    /// once reads Clean before the new save starts, and a test that moved on then closed the tab mid-save (2026-09-14).
    /// </summary>
    protected async Task SaveNowAsync(IPage page)
    {
        var status = page.Locator("[data-testid=save-status]");
        var before = await status.GetAttributeAsync("data-flushes");
        await page.Locator("#research-page-save").ClickAsync();
        await Expect(status).Not.ToHaveAttributeAsync("data-flushes", before ?? "", new() { Timeout = 20_000 });
        await Expect(status).ToHaveAttributeAsync("data-state", "Clean");
        await Expect(status).ToContainTextAsync("Saved");
    }

    protected async Task PublishAsync()
    {
        await Expect(Page.Locator("#research-page-publish")).ToBeEnabledAsync(new() { Timeout = 10_000 });
        await Page.Locator("#research-page-publish").ClickAsync();
        await Expect(Page.Locator("[data-testid=research-page-notice]")).ToContainTextAsync("Published", new() { Timeout = 20_000 });
    }

    /// <summary>Loads the page again, as whoever is signed in, and waits for it to be live.</summary>
    protected async Task ReloadPageAsync(string url)
    {
        await Page.GotoAsync(url);
        await WaitForTheCircuitAsync();
        // Loaded, not merely drawn: the page's frame is there from the first render, its title (editor or reader) or its
        // refusal only once the page has been fetched.
        await Expect(Page.Locator("[data-testid=research-page] :is(#research-page-title, h1, .card.border-warning, [data-testid=research-page-missing])").First)
            .ToBeVisibleAsync(new() { Timeout = 20_000 });
    }

    /// <summary>A unique title, so a leftover from an earlier run can never satisfy an assertion.</summary>
    protected static string UniqueTitle(string what) => $"{what} {Guid.NewGuid().ToString("N")[..8]}";

    [TearDown]
    public async Task DeleteThePagesThisTestMadeAsync()
    {
        if (_madePages.Count == 0) return;
        var token = await SarahTokenAsync();
        var api = await Playwright.APIRequest.NewContextAsync(new() { BaseURL = ApiUrl });
        try
        {
            foreach (var url in _madePages)
            {
                var m = PageUrl.Match(url);
                if (!m.Success) continue;
                var path = $"/api/orgs/{m.Groups["org"].Value}/cases/{m.Groups["case"].Value}/research/{m.Groups["entry"].Value}";
                var response = await api.DeleteAsync(path, new() { Headers = new Dictionary<string, string> { ["Authorization"] = $"Bearer {token}" } });
                if (response.Status == 401)
                {
                    // The kept token outlived its lifetime over a long run: sign in again once.
                    token = await SarahTokenAsync(renew: true);
                    await api.DeleteAsync(path, new() { Headers = new Dictionary<string, string> { ["Authorization"] = $"Bearer {token}" } });
                }
            }
        }
        finally
        {
            _madePages.Clear();
            await api.DisposeAsync();
        }
    }

    private async Task<string> SarahTokenAsync(bool renew = false)
    {
        await TokenLock.WaitAsync();
        try
        {
            if (_sarahToken is not null && !renew) return _sarahToken;
            var api = await Playwright.APIRequest.NewContextAsync(new() { BaseURL = ApiUrl });
            var login = await api.PostAsync("/login", new() { DataObject = new { email = UserEmail, password = UserPassword } });
            Assert.That(login.Ok, Is.True, "Sarah's seat should be able to sign in to tidy up research pages");
            _sarahToken = (await login.JsonAsync())!.Value.GetProperty("accessToken").GetString();
            await api.DisposeAsync();
            return _sarahToken!;
        }
        finally { TokenLock.Release(); }
    }
}
