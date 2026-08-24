using Microsoft.Playwright;
using System.Net.Http.Json;
using NUnit.Framework;

namespace Ben.Web.Playwright.Tests;

/// <summary>
/// The public feed: posting, mentions, tags, following and reporting.
/// </summary>
/// <remarks>
/// <para><b>The feed is off by default</b>, so these turn it on and put it back. That is not
/// incidental tidying: leaving it on would make every other test in the suite run against a site
/// configured differently from the one a fresh install produces, and leaving it <i>off</i> after a
/// failure would make the next run of this fixture fail for the wrong reason.</para>
///
/// <para>Posting is done through the page rather than seeded through the API, because what is
/// being checked is that the composer reaches the server and that what comes back is linkified —
/// and a seeded post would prove neither.</para>
/// </remarks>
[TestFixture]
[Category("Feed")]
[NonParallelizable]
public class FeedTests : BenTestBase
{
    private const string FeedFlag = "features.public-feed";

    /// <summary>Was the feed already on before this fixture touched it?</summary>
    private static bool _wasAlreadyOn;

    /// <summary>Browser-side errors, kept so a failure can say what the page complained about.</summary>
    private readonly List<string> _consoleErrors = [];

    [SetUp]
    public void WatchTheConsole()
    {
        _consoleErrors.Clear();
        Page.Console += (_, msg) => { if (msg.Type == "error") _consoleErrors.Add(msg.Text); };
        Page.PageError += (_, err) => _consoleErrors.Add("[pageerror] " + err);
    }

    [OneTimeSetUp]
    public async Task TurnTheFeedOn()
    {
        var token = await AdminTokenAsync();
        if (token is null) return;

        using (var http = new HttpClient { BaseAddress = new Uri(ApiUrl) })
        {
            http.DefaultRequestHeaders.Authorization =
                new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);

            var settings = await http.GetFromJsonAsync<System.Text.Json.JsonElement>("/api/admin/site-settings");
            foreach (var setting in settings.EnumerateArray())
            {
                if (setting.GetProperty("key").GetString() != FeedFlag) continue;

                _wasAlreadyOn = setting.TryGetProperty("value", out var value)
                    && string.Equals(value.GetString(), "true", StringComparison.OrdinalIgnoreCase);
            }
        }

        await SetFeedAsync(token, on: true);
    }

    [OneTimeTearDown]
    public async Task PutTheFeedBack()
    {
        var token = await AdminTokenAsync();
        if (token is not null) await SetFeedAsync(token, on: _wasAlreadyOn);
    }

    // ── Posting and linkifying ────────────────────────────────────────────────

    [Test]
    [Description("A post reaches the feed, and its @name and #tag come back as links.")]
    public async Task PostingLinkifiesMentionsAndTags()
    {
        var tag = $"t{Guid.NewGuid():N}"[..12];

        await LoginAsync(UserEmail, UserPassword);
        await GoToFeedAsync();

        // @jamesthornton is a real handle in the seed data; the tag is unique to this run so the
        // assertions below cannot be satisfied by somebody else's post.
        await ComposeAsync($"testing #{tag} with @jamesthornton");

        var post = Page.Locator(".bv-feed-post", new() { HasTextString = tag }).First;
        await Expect(post).ToBeVisibleAsync(new() { Timeout = 20_000 });

        // The mention is a link because the server resolved it to an account.
        await Expect(post.Locator(".bv-feed-mention")).ToHaveTextAsync("@jamesthornton");
        await Expect(post.Locator(".bv-feed-tag")).ToHaveTextAsync($"#{tag}");
    }

    [Test]
    [Description("An @name nobody holds stays as plain text.")]
    public async Task AnUnresolvedMentionIsNotALink()
    {
        // The author should be able to see their typo reached nobody, rather than it looking like
        // a working link to a person who does not exist.
        var marker = $"m{Guid.NewGuid():N}"[..12];

        await LoginAsync(UserEmail, UserPassword);
        await GoToFeedAsync();
        await ComposeAsync($"{marker} hello @nobodyholdsthishandle");

        var post = Page.Locator(".bv-feed-post", new() { HasTextString = marker }).First;
        await Expect(post).ToBeVisibleAsync(new() { Timeout = 20_000 });
        await Expect(post.Locator(".bv-feed-mention")).ToHaveCountAsync(0);
    }

    [Test]
    [Description("A tag link opens a page of everything carrying that tag.")]
    public async Task ATagLinkOpensItsOwnPage()
    {
        var tag = $"t{Guid.NewGuid():N}"[..12];

        await LoginAsync(UserEmail, UserPassword);
        await GoToFeedAsync();
        await ComposeAsync($"tagging #{tag}");

        var post = Page.Locator(".bv-feed-post", new() { HasTextString = tag }).First;
        await Expect(post).ToBeVisibleAsync(new() { Timeout = 20_000 });
        await post.Locator(".bv-feed-tag").First.ClickAsync();

        await Expect(Page).ToHaveURLAsync(new System.Text.RegularExpressions.Regex($"/feed/tags/{tag}$"),
            new() { Timeout = 20_000 });
        await Expect(Page.Locator(".bv-feed-post", new() { HasTextString = tag }).First)
            .ToBeVisibleAsync(new() { Timeout = 20_000 });

        // A tag page has no composer: a post written there would not carry the tag unless the
        // author typed it, and a composer that does not do what its context implies is worse than
        // no composer.
        await Expect(Page.Locator("#feed-composer")).ToHaveCountAsync(0);
    }

    // ── Reporting ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Reporting says so afterwards, and does not hide the post.
    /// </summary>
    /// <remarks>
    /// The property the whole moderation design rests on. It is also why the control becomes a
    /// label rather than disappearing: a control that vanishes leaves somebody wondering whether
    /// their report registered.
    /// </remarks>
    [Test]
    [Description("Reporting marks the post reported and leaves it visible.")]
    public async Task ReportingDoesNotHideThePost()
    {
        var marker = $"r{Guid.NewGuid():N}"[..12];

        // Posted by James so that Sarah — the reader below — is not its author; you cannot report
        // your own post.
        await LoginAsync(MemberEmail, MemberPassword);
        await GoToFeedAsync();
        await ComposeAsync($"{marker} a post to report");

        await LogoutAsync();
        await LoginAsync(UserEmail, UserPassword);
        await GoToFeedAsync();

        var post = Page.Locator(".bv-feed-post", new() { HasTextString = marker }).First;
        await Expect(post).ToBeVisibleAsync(new() { Timeout = 20_000 });

        await post.GetByRole(AriaRole.Button, new() { Name = "Report" }).ClickAsync();

        await Expect(post.GetByText("Reported")).ToBeVisibleAsync(new() { Timeout = 20_000 });
        await Expect(post).ToBeVisibleAsync();   // still there — a report hides nothing
    }

    [Test]
    [Description("You cannot report your own post.")]
    public async Task YourOwnPostOffersNoReportControl()
    {
        var marker = $"o{Guid.NewGuid():N}"[..12];

        await LoginAsync(UserEmail, UserPassword);
        await GoToFeedAsync();
        await ComposeAsync($"{marker} my own post");

        var post = Page.Locator(".bv-feed-post", new() { HasTextString = marker }).First;
        await Expect(post).ToBeVisibleAsync(new() { Timeout = 20_000 });
        await Expect(post.GetByRole(AriaRole.Button, new() { Name = "Report" })).ToHaveCountAsync(0);
    }

    // ── The switch ────────────────────────────────────────────────────────────

    /// <summary>
    /// With the feed off, its URL stops working — it does not merely lose its menu entry.
    /// </summary>
    /// <remarks>
    /// The failure this codebase keeps re-learning is a hidden link whose address still answers.
    /// The gate renders during the server render, so the page genuinely is not there.
    /// </remarks>
    [Test]
    [Description("Switched off, /feed is not found rather than merely unlinked.")]
    public async Task SwitchedOffTheFeedUrlStopsWorking()
    {
        var token = await AdminTokenAsync();
        Assert.That(token, Is.Not.Null, "Could not sign in as SuperAdmin to switch the feed.");

        try
        {
            await SetFeedAsync(token!, on: false);

            await LoginAsync(UserEmail, UserPassword);

            var gone = await FeedPageIsAsync(present: false);

            Assert.That(gone, Is.True,
                "The feed page still rendered after the flag was switched off. Note this is checked "
                + "by navigating repeatedly, not by waiting on one page: the website holds a cached "
                + "snapshot of the flags (SiteFeaturesProvider.RefreshInterval, 30s), and an "
                + "already-rendered page will never change on its own.");
        }
        finally
        {
            await SetFeedAsync(token!, on: true);
        }
    }

    // ── Helpers ──────────────────────────────────────────────────────────────


    private async Task GoToFeedAsync()
    {
        var there = await FeedPageIsAsync(present: true);

        if (!there)
        {
            // What the page actually said, rather than a guess about why. The old message blamed
            // the feature snapshot for every failure, which sent an entire debugging session down
            // the wrong path when the real cause was on the page in plain text.
            var text = await Page.Locator("body").InnerTextAsync();
            var console = string.Join(" | ", _consoleErrors.TakeLast(5));
            Assert.Fail($"The feed page never showed its composer.\n"
                        + $"  url: {Page.Url}\n"
                        + $"  console errors: {(console.Length == 0 ? "(none)" : console)}\n"
                        + $"  page text: {text.Replace("\n", " / ")}");
        }
    }

    /// <summary>
    /// Navigates until the feed page is present — or absent — or a deadline passes.
    /// </summary>
    /// <remarks>
    /// <para>Navigating rather than waiting is the whole point. The website reads its feature flags
    /// from a cached snapshot refreshed on a timer, so a page that has already rendered will
    /// <b>never</b> change its mind: polling the DOM of one page waits for something that cannot
    /// happen. Only a fresh request asks the question again.</para>
    ///
    /// <para>The snapshot also refreshes <i>behind</i> the reader that notices it is stale, so the
    /// first request after a change still gets the old answer and the next one gets the new — which
    /// is why this loops rather than reloading once.</para>
    /// </remarks>
    private async Task<bool> FeedPageIsAsync(bool present)
    {
        var deadline = DateTime.UtcNow.AddSeconds(50);

        while (DateTime.UtcNow < deadline)
        {
            await Page.GotoAsync($"{BaseUrl}/feed");

            // The composer is behind the circuit now (item 186: nothing is claimed about the
            // reader until auth resolves), so counting straight after load asks before the page
            // can possibly have answered. Give the circuit a moment to render its verdict —
            // either the composer or the join card is that verdict.
            try
            {
                await Page.Locator("#feed-composer, #feed-join-prompt").First
                    .WaitForAsync(new() { State = WaitForSelectorState.Attached, Timeout = 8_000 });
            }
            catch (TimeoutException)
            {
                // Neither appeared: the page is off, or still catching up. Loop.
            }

            var count = await Page.Locator("#feed-composer").CountAsync();
            if (present == count > 0) return true;

            await Task.Delay(3000);
        }

        return false;
    }

    /// <summary>
    /// Types a post and sends it, retrying until the text actually reaches the server.
    /// </summary>
    /// <remarks>
    /// A Blazor Server page renders its inputs before the circuit connects, and this box binds its
    /// value — so a character typed too early is not merely ignored, it is erased by the first
    /// interactive render. The Post button is disabled until the body reaches the server, which
    /// makes it the signal that the typing took.
    /// </remarks>
    private async Task ComposeAsync(string body)
    {
        var box = Page.Locator("#feed-composer");
        var post = Page.GetByRole(AriaRole.Button, new() { Name = "Post", Exact = true });

        for (var attempt = 0; attempt < 10; attempt++)
        {
            await box.FillAsync(body);

            try
            {
                await Expect(post).ToBeEnabledAsync(new() { Timeout = 1_500 });
                await post.ClickAsync();
                return;
            }
            catch (Exception)
            {
                // Circuit not live yet; the value was discarded. Try again.
            }
        }

        Assert.Fail("The composer never accepted the post — the page is not becoming interactive.");
    }

    /// <summary>
    /// A SuperAdmin bearer token, or null when sign-in failed.
    /// </summary>
    /// <remarks>
    /// A plain <c>HttpClient</c> rather than Playwright's request context: the Playwright instance
    /// is created per test and does not exist during <c>[OneTimeSetUp]</c>, where this is first
    /// needed. Using one client everywhere keeps the setup and the tests on the same path.
    /// </remarks>
    private static async Task<string?> AdminTokenAsync()
    {
        using var http = new HttpClient { BaseAddress = new Uri(ApiUrl), Timeout = TimeSpan.FromSeconds(30) };

        try
        {
            using var response = await http.PostAsJsonAsync("/login",
                new { email = SuperAdminEmail, password = SuperAdminPassword });

            if (!response.IsSuccessStatusCode) return null;

            var json = await response.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>();
            return json.GetProperty("accessToken").GetString();
        }
        catch (HttpRequestException)
        {
            return null;
        }
    }

    private static async Task SetFeedAsync(string token, bool on)
    {
        using var http = new HttpClient { BaseAddress = new Uri(ApiUrl), Timeout = TimeSpan.FromSeconds(30) };
        http.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);

        using var _ = await http.PutAsJsonAsync(
            $"/api/admin/site-settings/{FeedFlag}", new { value = on ? "true" : "false" });
    }

    // ── item 186 F1: the open door ────────────────────────────────────────────

    /// <summary>
    /// A visitor with no account reads the feed, and is invited rather than blocked.
    /// </summary>
    /// <remarks>
    /// The whole point of the arc: this page used to redirect a signed-out visitor to /login,
    /// which gave them nothing to sign up FOR. Note the signal is the join card, not the
    /// composer — a visitor has no composer, which is the other half of what this asserts.
    /// </remarks>
    [Test]
    [Description("Signed out: posts render, the join card stands where the composer would, no composer.")]
    public async Task AVisitorReadsTheFeedAndIsInvitedToJoin()
    {
        // Seed something to read as a member first, then leave.
        await LoginAsync(MemberEmail, MemberPassword);
        await GoToFeedAsync();
        await ComposeAsync($"Reading is open to everyone now #t{Guid.NewGuid():N}"[..48]);
        await LogoutAsync();

        await GoToFeedAsVisitorAsync();

        await Expect(Page.Locator(".bv-feed-post").First).ToBeVisibleAsync(new() { Timeout = 15_000 });
        await Expect(Page.Locator("#feed-join-prompt")).ToBeVisibleAsync(new() { Timeout = 10_000 });
        Assert.That(await Page.Locator("#feed-composer").CountAsync(), Is.Zero,
            "a visitor was offered a composer they cannot post with");

        // Controls that would only 401 are not offered at all.
        Assert.That(await Page.GetByRole(AriaRole.Button, new() { Name = "Report" }).CountAsync(), Is.Zero,
            "a visitor was offered Report, which the API refuses without an account");
        Assert.That(await Page.GetByRole(AriaRole.Button, new() { Name = "Follow" }).CountAsync(), Is.Zero,
            "a visitor was offered Follow, which the API refuses without an account");

        // Following is a signed-in idea; the tab has nothing to say to a visitor.
        Assert.That(await Page.GetByRole(AriaRole.Button, new() { Name = "Following" }).CountAsync(), Is.Zero);
    }

    /// <summary>A visitor may open a thread from a shared link, and read a profile.</summary>
    [Test]
    [Description("Signed out: a thread and a profile both open, with no reply composer.")]
    public async Task AVisitorOpensAThreadAndAProfile()
    {
        await LoginAsync(MemberEmail, MemberPassword);
        await GoToFeedAsync();
        await ComposeAsync($"A thread anyone can open #t{Guid.NewGuid():N}"[..40]);

        // The anchor, not the text inside it: GetByText resolves to the text node's element,
        // which here is the <a> only by luck of markup — asking for the link by its href shape
        // is what stays true when the card's footer changes.
        var threadHref = await Page.Locator(".bv-feed-post").First
            .Locator("a[href^='/feed/']").First.GetAttributeAsync("href");
        Assert.That(threadHref, Is.Not.Null, "no thread link on the post just written");

        var authorHref = await Page.Locator(".bv-feed-post").First
            .Locator("a[href^='/feed/people/']").First.GetAttributeAsync("href");

        await LogoutAsync();

        await Page.GotoAsync($"{BaseUrl}{threadHref}");
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);
        await Expect(Page.Locator(".bv-feed-post").First).ToBeVisibleAsync(new() { Timeout = 15_000 });
        Assert.That(await Page.Locator("#feed-composer").CountAsync(), Is.Zero,
            "a visitor was offered a reply box");

        await Page.GotoAsync($"{BaseUrl}{authorHref}");
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);
        await Expect(Page.Locator(".bv-feed-post").First).ToBeVisibleAsync(new() { Timeout = 15_000 });
    }

    /// <summary>
    /// Navigates to the feed as a visitor, allowing for the website's cached feature snapshot.
    /// </summary>
    /// <remarks>
    /// The signed-in helper polls for the composer, which a visitor never gets. The join card is
    /// the visitor's equivalent signal that the page rendered rather than 404ing.
    /// </remarks>
    private async Task GoToFeedAsVisitorAsync()
    {
        var deadline = DateTime.UtcNow.AddSeconds(50);

        while (DateTime.UtcNow < deadline)
        {
            await Page.GotoAsync($"{BaseUrl}/feed");

            // Behind the circuit, exactly like the composer: the page claims nothing about the
            // reader until auth resolves, so counting straight after load asks too early.
            try
            {
                await Page.Locator("#feed-join-prompt")
                    .WaitForAsync(new() { State = WaitForSelectorState.Attached, Timeout = 8_000 });
                return;
            }
            catch (TimeoutException)
            {
                await Task.Delay(2000);
            }
        }

        var text = await Page.Locator("body").InnerTextAsync();
        Assert.Fail("The feed page never showed its join card to a signed-out visitor.\n"
                    + $"  url: {Page.Url}\n  page text: {text.Replace("\n", " / ")}");
    }

    // ── item 186 F3 ───────────────────────────────────────────────────────────

    /// <summary>A like sticks, and survives a reload rather than living in the page's head.</summary>
    [Test]
    [Description("Liking a post persists across a reload, and unliking takes it back.")]
    public async Task ALikePersistsAndCanBeTakenBack()
    {
        await LoginAsync(UserEmail, UserPassword);
        await GoToFeedAsync();
        await ComposeAsync($"Something worth liking #t{Guid.NewGuid():N}"[..44]);

        var card = Page.Locator(".bv-feed-post").First;
        var like = card.Locator(".bv-feed-like");
        await Expect(like).ToBeVisibleAsync(new() { Timeout = 10_000 });

        await like.ClickAsync();
        await Expect(card.Locator(".bv-feed-like[aria-pressed='true']"))
            .ToBeVisibleAsync(new() { Timeout = 10_000 });

        // The reload is the point: an optimistic count that never reached the server would
        // look identical until the page came back.
        await GoToFeedAsync();
        var reloaded = Page.Locator(".bv-feed-post").First;
        await Expect(reloaded.Locator(".bv-feed-like[aria-pressed='true']"))
            .ToBeVisibleAsync(new() { Timeout = 10_000 });

        await reloaded.Locator(".bv-feed-like").ClickAsync();
        await Expect(reloaded.Locator(".bv-feed-like[aria-pressed='false']"))
            .ToBeVisibleAsync(new() { Timeout = 10_000 });
    }

    /// <summary>The three tabs are there, and For You is where the reader lands.</summary>
    [Test]
    [Description("For You is the default tab; Latest and Following are both offered.")]
    public async Task ForYouIsTheDefaultTab()
    {
        await LoginAsync(UserEmail, UserPassword);
        await GoToFeedAsync();

        await Expect(Page.GetByRole(AriaRole.Button, new() { Name = "For You" }))
            .ToHaveClassAsync(new System.Text.RegularExpressions.Regex("active"));
        await Expect(Page.GetByRole(AriaRole.Button, new() { Name = "Latest" })).ToBeVisibleAsync();
        await Expect(Page.GetByRole(AriaRole.Button, new() { Name = "Following" })).ToBeVisibleAsync();

        // Switching tabs actually re-reads rather than just restyling the tab.
        await Page.GetByRole(AriaRole.Button, new() { Name = "Latest" }).ClickAsync();
        await Expect(Page.Locator(".bv-feed-post").First).ToBeVisibleAsync(new() { Timeout = 15_000 });
    }

    /// <summary>A visitor sees the count but is offered a way in rather than a dead control.</summary>
    [Test]
    [Description("Signed out: no like button, and the feed still reads.")]
    public async Task AVisitorGetsNoLikeButton()
    {
        await LoginAsync(MemberEmail, MemberPassword);
        await GoToFeedAsync();
        await ComposeAsync($"Visitors cannot like this #t{Guid.NewGuid():N}"[..46]);
        await LogoutAsync();

        await GoToFeedAsVisitorAsync();
        await Expect(Page.Locator(".bv-feed-post").First).ToBeVisibleAsync(new() { Timeout = 15_000 });

        Assert.That(await Page.Locator("button.bv-feed-like").CountAsync(), Is.Zero,
            "a visitor was offered a like button the API would refuse");
    }
}
