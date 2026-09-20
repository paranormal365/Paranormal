using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Playwright;
using NUnit.Framework;

namespace Ben.Web.Playwright.Capture;

/// <summary>
/// The whole product, start to end, as each kind of person who uses it — after hosted events merged, to see that nothing
/// else moved.
/// </summary>
/// <remarks>
/// <para><b>Asked for by Ben on 2026-09-13:</b> <i>"step through it from start to end for all functionality outside and
/// after merging events."</i> The browser suite proves what its tests name; this walks what a person actually does, in
/// the order they would do it, on the testing copy's realistic data, and photographs every screen so the pictures can
/// be looked at rather than inferred.</para>
///
/// <para><b>It does not stop at the first problem.</b> Each step is recorded — the address it ended on, what the page
/// said, every server error the browser saw, every script error, the error banner — and the walk moves on. One report
/// per person is written beside the pictures, and the test fails at the end naming every step that had a problem.</para>
///
/// <para><b>Opt-in:</b> <c>BEN_PRODUCT_WALK=1</c>, pictures under <c>BEN_WALK_OUT</c>. It is meant for the local hosts on
/// <c>IsHauntedDb_player</c>. It reads, and it performs the small ordinary actions a person would — a search, a vote
/// cast and taken back, a tab opened — never a destructive one.</para>
///
/// <para><b>Each step waits for what it is of</b>: the page's loading placeholders to clear and, when the step names one,
/// the element the step is about. Never for a length of time.</para>
/// </remarks>
[TestFixture]
[Category("Capture")]
[NonParallelizable]
public sealed class ProductWalk : BenTestBase
{
    private string _persona = "";
    private int _step;
    private readonly List<string> _signals = [];
    private readonly StringBuilder _report = new();
    private readonly List<string> _problems = [];
    private readonly List<string> _notes = [];

    private static string OutRoot => Environment.GetEnvironmentVariable("BEN_WALK_OUT") ?? "product-walk";

    public override BrowserNewContextOptions ContextOptions() => new()
    {
        ViewportSize = new ViewportSize { Width = 1440, Height = 900 },
        ColorScheme = ColorScheme.Dark,
    };

    [SetUp]
    public void RequireOptIn()
    {
        if (Environment.GetEnvironmentVariable("BEN_PRODUCT_WALK") != "1")
            Assert.Ignore("Set BEN_PRODUCT_WALK=1 (and point the hosts at IsHauntedDb_player) to walk the product.");

        // NUnit keeps one instance of a fixture for all its tests, so each person starts from nothing.
        _persona = "";
        _step = 0;
        _signals.Clear();
        _report.Clear();
        _problems.Clear();

        // What the page itself reports while a step runs. Blazor Server makes its API calls from the server, so a refused
        // or failed call shows in the page, not here — the page text is checked for that separately.
        Page.Console += (_, m) => { if (m.Type == "error") _signals.Add($"console: {m.Text}"); };
        Page.PageError += (_, e) => _signals.Add($"script error: {e}");
        Page.Response += (_, r) =>
        {
            if (r.Status >= 500 && r.Url.StartsWith(BaseUrl, StringComparison.OrdinalIgnoreCase))
                _signals.Add($"HTTP {r.Status} {r.Url}");
        };
    }

    [TearDown]
    public void WriteTheReport()
    {
        if (_persona.Length == 0) return;
        var dir = Path.Combine(OutRoot, _persona);
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "report.md"),
            $"# {_persona}\n\n| # | Step | Ended on | Result |\n|---|---|---|---|\n{_report}");
    }

    /// <summary>One thing a person does, photographed and checked, never allowed to end the walk.</summary>
    private async Task StepAsync(string name, Func<Task> act, ILocator? subject = null, string? expect = null)
    {
        _step++;
        _signals.Clear();
        _notes.Clear();
        var problems = new List<string>();

        try
        {
            await act();
            await WaitForTheCircuitAsync();
            await WaitUntilLoadedAsync(20_000);
            await Expect(Spinners).ToHaveCountAsync(0, new() { Timeout = 30_000 });
            if (subject is not null)
                await Expect(subject.First).ToBeVisibleAsync(new() { Timeout = 20_000 });
        }
        catch (Exception ex)
        {
            problems.Add("did not complete: " + ex.Message.Split('\n')[0]);
        }

        var file = $"{_step:00}-{Regex.Replace(name.ToLowerInvariant(), "[^a-z0-9]+", "-").Trim('-')}.png";
        var dir = Path.Combine(OutRoot, _persona);
        Directory.CreateDirectory(dir);

        // Transitions finished first: a tab's highlight fades over 150 ms, and a picture taken inside that fade shows the
        // previous tab chosen over the new tab's content.
        //
        // And taken again when a spinner arrived while it was being taken. Some parts of a page only begin once sign-in
        // has resolved, so their loader can appear after the page looked settled; the picture is of the page once that
        // loader has gone, and a step whose page never settles says so.
        var settled = false;
        for (var attempt = 0; attempt < 3 && !settled; attempt++)
        {
            if (await Spinners.CountAsync() > 0)
            {
                try { await Expect(Spinners).ToHaveCountAsync(0, new() { Timeout = 30_000 }); }
                catch (PlaywrightException) { break; }
            }
            // Every picture in view finished, or known broken: a picture that never decodes is a finding, and one still
            // arriving would be photographed as a grey box. Only those in view — a lazy image below the fold never loads.
            foreach (var broken in await BrokenImagesInViewAsync())
                if (!problems.Contains($"broken image: {broken}")) problems.Add($"broken image: {broken}");
            try { await Page.ScreenshotAsync(new() { Path = Path.Combine(dir, file), Animations = ScreenshotAnimations.Disabled }); }
            catch (Exception ex) { problems.Add("no picture: " + ex.Message.Split('\n')[0]); settled = true; break; }
            settled = await Spinners.CountAsync() == 0;
        }
        if (!settled) problems.Add("still loading when photographed");

        // The page as photographed, not as it was a moment before.
        string text;
        try { text = await Main.InnerTextAsync(new() { Timeout = 10_000 }); }
        catch (Exception) { text = ""; }

        // Looked for OUTSIDE the page's own content, and through the banner's own element — because
        // a page is allowed to TALK about an error without being one. /changes carries a line about
        // the day that phrase stopped appearing, and reading `main` counted the changelog's own
        // words as proof the changelog was broken. RouteCrawlTests was corrected for exactly this
        // on 2026-09-19; this copy of the same check was not, and repeated the mistake (2026-09-20).
        //
        // Outside is also where a real one lives: #blazor-error-ui is rendered beyond the layout's
        // content region.
        var banner = await Page.EvaluateAsync<bool>(@"() => {
            const main  = document.querySelector('.app-content, main, .content-wrapper');
            const body  = document.body.innerText || '';
            const inner = main ? (main.innerText || '') : '';
            const chrome = inner ? body.split(inner).join(' ') : body;
            const err = document.querySelector('#blazor-error-ui');
            const shown = !!err && getComputedStyle(err).display !== 'none';
            return shown || /An unhandled error has occurred/i.test(chrome);
        }");

        if (banner) problems.Add("unhandled error banner");
        if (await Page.Locator("#blazor-error-ui").IsVisibleAsync()) problems.Add("Blazor error bar showing");
        if (text.Contains("Page not found") || text.Contains("Sorry, there's nothing at this address")) problems.Add("not found");
        if (text.Trim().Length < 40) problems.Add($"rendered only {text.Trim().Length} characters");
        if (expect is not null && !text.Contains(expect, StringComparison.OrdinalIgnoreCase)) problems.Add($"never said \"{expect}\"");
        problems.AddRange(_signals.Distinct());

        var path = new Uri(Page.Url).PathAndQuery;
        var result = problems.Count == 0 ? "ok" : string.Join("; ", problems).Replace("|", "/");
        if (_notes.Count > 0) result += " (" + string.Join("; ", _notes) + ")";
        _report.Append($"| {_step} | {name} | `{path}` | {result} |\n");
        foreach (var p in problems) _problems.Add($"[{_persona} {_step:00} {name}] {p}");
    }

    private Task GoAsync(string path) => Page.GotoAsync($"{BaseUrl}{path}");

    /// <summary>
    /// Waits for every image in view to decode and returns the addresses (without their query strings, which carry media
    /// tickets) of the ones that could not be.
    /// </summary>
    private async Task<string[]> BrokenImagesInViewAsync() => await Page.EvaluateAsync<string[]>(@"async () => {
        const inView = [...document.images].filter(img => {
            const r = img.getBoundingClientRect();
            return r.width > 0 && r.height > 0 && r.bottom > 0 && r.top < innerHeight && r.right > 0 && r.left < innerWidth;
        });
        const results = await Promise.all(inView.map(img => img.decode().then(() => null, () => img.currentSrc || img.src)));
        return results.filter(src => src).map(src => src.split('?')[0]);
    }");


    /// <summary>
    /// Follows the first link on the page whose address starts with <paramref name="prefix"/>, or notes that the page
    /// offered none — on the testing copy some groups simply have no tours or website pages, and that is not a fault.
    /// </summary>
    private async Task FollowIfAnyAsync(string prefix)
    {
        await WaitUntilLoadedAsync(20_000);
        if (await Main.Locator($"a[href^='{prefix}']").CountAsync() == 0)
        {
            _notes.Add($"nothing to open under {prefix}");
            return;
        }
        await FollowAsync(prefix);
    }

    /// <summary>Opens the first card's Open button on a group's case list.</summary>
    private async Task OpenFirstCaseAsync(string org)
    {
        var open = Main.Locator(".card").GetByRole(AriaRole.Button, new() { Name = "Open" })
            .Or(Main.Locator(".card").GetByRole(AriaRole.Link, new() { Name = "Open" })).First;
        await Expect(open).ToBeVisibleAsync(new() { Timeout = 20_000 });
        await ClickUntilUrlAsync(open, $@"/organizations/{org}/cases/[0-9a-f\-]{{36}}");
    }

    /// <summary>Opens the case whose card names <paramref name="text"/>, from the group's case list.</summary>
    private async Task OpenCaseNamedAsync(string org, string text)
    {
        await GoAsync($"/organizations/{org}/cases");
        var card = Main.Locator(".card").Filter(new() { HasTextString = text }).First;
        var open = card.GetByRole(AriaRole.Button, new() { Name = "Open" }).Or(card.GetByRole(AriaRole.Link, new() { Name = "Open" })).First;
        await Expect(open).ToBeVisibleAsync(new() { Timeout = 20_000 });
        await ClickUntilUrlAsync(open, $@"/organizations/{org}/cases/[0-9a-f\-]{{36}}");
    }

    /// <summary>Follows the first link on the page whose address starts with <paramref name="prefix"/>.</summary>
    private async Task FollowAsync(string prefix)
    {
        // Not the "New …" link that sits first on a list page: the step is after a thing that exists, and
        // "one user" used to photograph the New User form (2026-09-16).
        var link = Main.Locator($"a[href^='{prefix}']:not([href$='/new'])").First;
        await Expect(link).ToBeVisibleAsync(new() { Timeout = 20_000 });
        var href = (await link.GetAttributeAsync("href"))!;
        await ClickUntilUrlAsync(link, Regex.Escape(href.Split('?')[0]));
    }

    private async Task OrgTabAsync(string tab)
    {
        var button = Main.GetByRole(AriaRole.Tab, new() { Name = tab, Exact = true }).First;
        await Expect(button).ToBeVisibleAsync(new() { Timeout = 20_000 });
        await ClickUntilAsync(button, Main.Locator("[role=tab][aria-selected=true]", new() { HasTextString = tab }));
    }

    /// <summary>For a seat that may not be offered a tab at all: a missing tab is noted, not failed.</summary>
    private async Task OrgTabIfOfferedAsync(string tab)
    {
        await Expect(Main.GetByRole(AriaRole.Tab).First).ToBeVisibleAsync(new() { Timeout = 20_000 });
        if (await Main.GetByRole(AriaRole.Tab, new() { Name = tab, Exact = true }).CountAsync() == 0)
        {
            _notes.Add($"no {tab} tab for this seat");
            return;
        }
        await OrgTabAsync(tab);
    }

    private void Finish()
        => Assert.That(_problems, Is.Empty, $"{_persona} walk:\n  " + string.Join("\n  ", _problems));

    // ── 1. Somebody who has never been here ───────────────────────────────────

    [Test]
    public async Task A_visitor()
    {
        _persona = "1-visitor";

        await StepAsync("home", () => GoAsync("/"), expect: "Haunted");
        await StepAsync("a case from the home page", () => FollowAsync("/o/"));
        await StepAsync("find a group", () => GoAsync("/find"), Main.GetByPlaceholder("Enter city, address, or zip code"));
        await StepAsync("search Nashville", async () =>
        {
            await Main.GetByPlaceholder("Enter city, address, or zip code").FillAsync("Nashville, TN");
            // The answer, not a card: every group is already listed as cards before anybody searches.
            await ClickUntilAsync(Main.GetByRole(AriaRole.Button, new() { Name = "Search" }),
                Main.GetByText("Results near", new() { Exact = false }).Or(Main.Locator(".alert")));
        }, Main.GetByText("Results near", new() { Exact = false }).Or(Main.GetByText("No organizations accepting cases", new() { Exact = false })));
        await StepAsync("a group's public page", () => GoAsync("/o/paranormal365"));
        await StepAsync("the group's cases", () => GoAsync("/o/paranormal365/cases"));
        await StepAsync("one published case", () => FollowAsync("/o/paranormal365/cases/"), Main.Locator(".case-vote-widget"));
        await StepAsync("the vote button sends a stranger to sign in", async () =>
        {
            await ClickUntilUrlAsync(Main.Locator(".vote-actions__vote > .vote-btn"), "/login");
        }, Page.GetByRole(AriaRole.Button, new() { Name = "Sign In", Exact = true }));
        await StepAsync("what's on", () => GoAsync("/events"));
        await StepAsync("an event's page", () => FollowAsync("/o/"));
        await StepAsync("the feed", () => GoAsync("/feed"));
        await StepAsync("a post", () => FollowAsync("/feed/"));
        // The arc's front door for a stranger: one location, everybody's work at it. A visitor must
        // see the published half and be offered no box (2026-09-17).
        await StepAsync("a public place, as a stranger",
            () => GoAsync("/places/40000001-0000-0000-0000-000000000001"),
            Main.GetByText("Published investigations", new() { Exact = false })
                .Or(Main.GetByText("Nothing has been published", new() { Exact = false })));
        await StepAsync("equipment catalog", () => GoAsync("/equipment-catalog"));
        await StepAsync("gear people own", () => OrgTabAsync("Gear people own"));
        await StepAsync("an item somebody owns", () => FollowIfAnyAsync("/equipment/"));
        await StepAsync("a tour's public page", () => GoAsync("/o/pw-tour-1789070429/tours/church-street-walk"));
        await StepAsync("publications", () => GoAsync("/publications"));
        await StepAsync("pricing, a card and a button for every plan", () => GoAsync("/pricing"),
            Main.Locator("[data-testid=pricing-band] [data-testid=pricing-cta] a, [data-testid=pricing-band] [data-testid=pricing-cta] button, #pricing-not-on-sale"), expect: "Pricing");
        await StepAsync("ask for an investigation", () => GoAsync("/my-requests/new"));
        await StepAsync("help", () => GoAsync("/help"), expect: "Help");
        await StepAsync("a help article", () => FollowAsync("/help/"));
        await StepAsync("what's new", () => GoAsync("/changes"));
        await StepAsync("contact", () => GoAsync("/contact"));
        await StepAsync("privacy", () => GoAsync("/privacy"), expect: "Privacy");
        await StepAsync("terms", () => GoAsync("/terms"), expect: "Terms");
        await StepAsync("sign up", () => GoAsync("/signup"));
        await StepAsync("sign in", () => GoAsync("/login"), Page.GetByRole(AriaRole.Button, new() { Name = "Sign In", Exact = true }));
        await StepAsync("forgotten password", () => GoAsync("/forgot-password"));
        await StepAsync("a signed-in page refuses in words", () => GoAsync("/my-cases"));

        Finish();
    }

    // ── 2. Somebody who asked a group for help ────────────────────────────────

    [Test]
    public async Task A_client()
    {
        _persona = "2-client";
        var org = await OrgIdBySlugAsync("paranormal365");
        await LoginAsync(ClientEmail, ClientPassword);

        await StepAsync("home, signed in", () => GoAsync("/"));
        await StepAsync("my cases", () => GoAsync("/my-cases"));
        await StepAsync("one of my cases", () => FollowAsync("/my-cases/"));
        await StepAsync("my requests", () => GoAsync("/my-requests"));
        await StepAsync("one request", () => FollowAsync("/my-requests/"));
        await StepAsync("a new request", () => GoAsync("/my-requests/new"));
        await StepAsync("my investigations", () => GoAsync("/my-investigations"));
        await StepAsync("my evidence", () => GoAsync("/my-evidence"));
        await StepAsync("my events", () => GoAsync("/my-events"));
        await StepAsync("notifications", () => GoAsync("/notifications"));
        await StepAsync("profile", () => GoAsync("/profile"), Main.GetByRole(AriaRole.Tab, new() { Name = "About" }));
        await StepAsync("profile, contact", () => OrgTabAsync("Contact"));
        await StepAsync("profile, security", () => OrgTabAsync("Security"));
        await StepAsync("a group they are not in refuses", () => GoAsync($"/organizations/{org}"));
        await StepAsync("an admin page refuses", () => GoAsync("/admin/users"));

        Finish();
    }

    // ── 3. An ordinary member ─────────────────────────────────────────────────

    [Test]
    public async Task A_member()
    {
        _persona = "3-member";
        var org = await OrgIdBySlugAsync("paranormal365");
        await LoginAsync(MemberEmail, MemberPassword);

        await StepAsync("home, signed in", () => GoAsync("/"));
        await StepAsync("organizations", () => GoAsync("/organizations"));
        await StepAsync("the group", () => GoAsync($"/organizations/{org}"), Main.GetByRole(AriaRole.Tab));
        foreach (var tab in new[] { "Members", "Cases", "Investigations", "Calendar", "Messages", "Files", "Equipment" })
            await StepAsync($"group tab {tab}", () => OrgTabAsync(tab));
        await StepAsync("a case", () => GoAsync($"/organizations/{org}/cases"));
        await StepAsync("the case itself", () => OpenFirstCaseAsync(org));
        await StepAsync("research on the Belmont case", async () =>
        {
            await OpenCaseNamedAsync(org, "Belmont");
            await GoAsync(new Uri(Page.Url).AbsolutePath + "?tab=research");
        }, Main.Locator("[data-testid=case-research-boards]"));
        await StepAsync("a published case: vote and take it back", async () =>
        {
            await GoAsync("/o/paranormal365/cases");
            await FollowAsync("/o/paranormal365/cases/");
            var vote = Main.Locator(".vote-actions__vote > .vote-btn");
            await Expect(Main.Locator(".vote-actions__signin")).ToHaveCountAsync(0, new() { Timeout = 20_000 });
            if (await vote.GetAttributeAsync("aria-pressed") == "true")
            {
                await vote.ClickAsync();
                await Expect(vote).ToHaveAttributeAsync("aria-pressed", "false", new() { Timeout = 10_000 });
            }
            await ClickUntilAsync(vote, Main.Locator(".vote-choices"));
            await Main.Locator(".vote-choices").GetByRole(AriaRole.Button, new() { Name = "Inconclusive — can't say either way" }).ClickAsync();
            await Expect(vote).ToHaveAttributeAsync("aria-pressed", "true", new() { Timeout = 10_000 });
            await vote.ClickAsync();
            await Expect(vote).ToHaveAttributeAsync("aria-pressed", "false", new() { Timeout = 10_000 });
        });
        await StepAsync("my investigations", () => GoAsync("/my-investigations"));
        await StepAsync("my equipment", () => GoAsync("/my-equipment"));
        await StepAsync("my checkouts", () => GoAsync("/my-checkouts"));
        await StepAsync("media library", () => GoAsync("/media-library"));
        await StepAsync("my field sessions", () => GoAsync("/my-field-sessions"));
        await StepAsync("a field session plays back", () => FollowAsync("/field-sessions/"));
        await StepAsync("my videos", () => GoAsync("/my-videos"));
        await StepAsync("the feed", () => GoAsync("/feed"));
        await StepAsync("what's on", () => GoAsync("/events"));
        await StepAsync("my events", () => GoAsync("/my-events"));
        await StepAsync("group messages", () => GoAsync($"/organizations/{org}/messages"));
        // The place hub as a member sees it (2026-09-17): their own groups' visits and cases, what
        // others shared, the published cases, the posts, and the buttons that start work here.
        await StepAsync("a public place, signed in",
            () => GoAsync("/places/40000001-0000-0000-0000-000000000001"),
            Main.GetByText("Shared by other groups", new() { Exact = false }));
        await StepAsync("a new case names its place", async () =>
        {
            await GoAsync($"/organizations/{org}/cases/new?place=40000001-0000-0000-0000-000000000001");
            await WaitUntilLoadedAsync();
        }, Main.GetByTestId("case-place-chosen")
               .Or(Main.GetByText("What kind of place is this?", new() { Exact = false })));
        await StepAsync("notifications", () => GoAsync("/notifications"));
        await StepAsync("upload files", () => GoAsync("/upload-files"));
        await StepAsync("the admin area refuses", () => GoAsync("/admin/dashboard"));

        Finish();
    }

    // ── 3b. Somebody in no group at all — the free lane's own seat ────────────

    /// <summary>
    /// Wren: an account, and nothing else. The persona the free lane is for.
    /// </summary>
    /// <remarks>
    /// <para>Added 2026-09-17, because until then no walk covered somebody who belongs to nothing —
    /// every seat here is a member, a client, a viewer or an admin, and each of them passes doors
    /// she does not. Most of what she opens should REFUSE her in words rather than show an empty
    /// page, which is the thing this walk exists to catch.</para>
    ///
    /// <para>She may already have a space of her own, from a browser test that minted her one. The
    /// walk does not care: it reads what the page offers rather than asserting which offer it is.</para>
    /// </remarks>
    [Test]
    public async Task Somebody_in_no_group()
    {
        _persona = "3b-no-group";
        await LoginAsync(SoloEmail, SoloPassword);

        await StepAsync("home, signed in", () => GoAsync("/"));
        await StepAsync("a public place, where the free lane starts",
            () => GoAsync("/places/40000001-0000-0000-0000-000000000001"),
            Main.Locator(".place-investigate-solo").Or(Main.Locator(".place-investigate")));
        await StepAsync("the feed, which she may read", () => GoAsync("/feed"));
        await StepAsync("my investigations", () => GoAsync("/my-investigations"));
        await StepAsync("my evidence", () => GoAsync("/my-evidence"));
        await StepAsync("my files", () => GoAsync("/upload-files"));
        await StepAsync("my profile", () => GoAsync("/profile"));
        await StepAsync("organizations — hers, if she has made one", () => GoAsync("/organizations"));
        await StepAsync("find a group", () => GoAsync("/find"));
        await StepAsync("pricing", () => GoAsync("/pricing"), expect: "Pricing");
        await StepAsync("ask a group for help", () => GoAsync("/my-requests/new"));
        await StepAsync("notifications", () => GoAsync("/notifications"));
        await StepAsync("the admin area refuses", () => GoAsync("/admin/dashboard"));

        Finish();
    }

    // ── 4. A member who may look and change nothing ───────────────────────────

    [Test]
    public async Task A_viewer()
    {
        _persona = "4-viewer";
        var org = await OrgIdBySlugAsync("paranormal365");

        // Through the SuperAdmin's own "view as", not the viewer's password: the testing copy's viewer has a password of
        // their own, and a walk has no business changing it. Impersonation shows exactly their seat.
        await LoginAsync(SuperAdminEmail, SuperAdminPassword);
        await StepAsync("view the site as the viewer", async () =>
        {
            await GoAsync("/admin/users");
            await Main.GetByPlaceholder("Search by name or email…").FillAsync(ViewerEmail);
            await Main.GetByRole(AriaRole.Button, new() { Name = "Search" }).ClickAsync();
            var row = Main.Locator("tr", new() { HasTextString = "Victor Reyes" }).First;
            await Expect(row).ToBeVisibleAsync(new() { Timeout = 20_000 });
            await row.Locator("button[title='Impersonate this user']").First.ClickAsync();
            await Expect(Page.GetByText("Viewing as", new() { Exact = false })).ToBeVisibleAsync(new() { Timeout = 20_000 });
        });

        await StepAsync("the group", () => GoAsync($"/organizations/{org}"), Main.GetByRole(AriaRole.Tab));
        foreach (var tab in new[] { "Cases", "Investigations", "Calendar", "Messages", "Files", "Equipment" })
            await StepAsync($"group tab {tab}", () => OrgTabIfOfferedAsync(tab));
        await StepAsync("the case list, typed into the address bar", () => GoAsync($"/organizations/{org}/cases"));
        await StepAsync("the group's events, to read", () => GoAsync($"/organizations/{org}/events"),
            Main.Locator("#events-read-only"));
        await StepAsync("one event, to read", () => FollowAsync($"/organizations/{org}/events/"),
            Main.Locator("#event-read-only"));
        await StepAsync("billing refuses in words", () => GoAsync($"/organizations/{org}/billing"));
        await StepAsync("media library", () => GoAsync("/media-library"));
        await StepAsync("my equipment", () => GoAsync("/my-equipment"));
        await StepAsync("back to SuperAdmin", async () =>
        {
            await Page.GetByRole(AriaRole.Button, new() { Name = "Return to SuperAdmin" }).First.ClickAsync();
            await Expect(Page.GetByText("Viewing as", new() { Exact = false })).ToHaveCountAsync(0, new() { Timeout = 20_000 });
        });

        Finish();
    }

    // ── 5. A group's owner or administrator ───────────────────────────────────

    [Test]
    public async Task An_owner()
    {
        _persona = "5-owner";
        var org = await OrgIdBySlugAsync("paranormal365");
        await LoginAsync(UserEmail, UserPassword);

        await StepAsync("home, signed in", () => GoAsync("/"));
        await StepAsync("the group", () => GoAsync($"/organizations/{org}"), Main.GetByRole(AriaRole.Tab));
        foreach (var tab in new[] { "Details", "Members", "Cases", "Investigations", "Calendar", "Messages", "Files",
                                    "Requests", "Clients", "CMS", "Equipment", "Roles", "Addresses", "Settings" })
            await StepAsync($"group tab {tab}", () => OrgTabAsync(tab));
        await StepAsync("cases", () => GoAsync($"/organizations/{org}/cases"));
        await StepAsync("a case", () => OpenFirstCaseAsync(org));
        await StepAsync("a new case", () => GoAsync($"/organizations/{org}/cases/new"));
        await StepAsync("the Edit Case page", async () =>
        {
            await OpenCaseNamedAsync(org, "Belmont");
            await ClickUntilUrlAsync(Main.Locator("#case-edit"), "/edit$");
        }, Main.Locator("#case-edit-description .k-editor"));
        await StepAsync("leaving Edit Case unchanged goes back to the case", () => ClickUntilUrlAsync(Main.Locator("#case-edit-cancel"), $@"/organizations/{org}/cases/[0-9a-f\-]{{36}}$"));
        await StepAsync("case notes, formatted", () => GoAsync(new Uri(Page.Url).AbsolutePath + "?tab=notes"), Main.Locator("#case-notes-new"));
        await StepAsync("the timeline", () => GoAsync(new Uri(Page.Url).AbsolutePath + "?tab=timeline"));
        // The boards themselves live in another application on another host, so the walk stops at the door: it is
        // checking the site's own pages, and CanvasResearchHandoverTests covers what is through it.
        await StepAsync("research, with New board", () => GoAsync(new Uri(Page.Url).AbsolutePath + "?tab=research"), Main.Locator("#research-new-board"));
        var caseAddress = $"/organizations/{org}/cases";
        await StepAsync("an investigation from the list", async () =>
        {
            await GoAsync("/my-investigations");
            await FollowAsync("/organizations/");
        });
        await StepAsync("pending requests", () => GoAsync($"/organizations/{org}/pending-requests"));
        await StepAsync("members page", () => GoAsync($"/organizations/{org}/members"));
        await StepAsync("membership questions", () => GoAsync($"/organizations/{org}/membership-questions"));
        await StepAsync("calendar", () => GoAsync($"/organizations/{org}/calendar"));
        await StepAsync("messages", () => GoAsync($"/organizations/{org}/messages"));
        await StepAsync("files", () => GoAsync($"/organizations/{org}/files"));
        await StepAsync("client settings", () => GoAsync($"/organizations/{org}/client-settings"));
        await StepAsync("the group's website", () => GoAsync($"/organizations/{org}/cms"));
        await StepAsync("a website page", () => FollowIfAnyAsync($"/organizations/{org}/cms/pages/"));
        await StepAsync("edit the group", () => GoAsync($"/organizations/{org}/edit"));
        await StepAsync("billing", () => GoAsync($"/organizations/{org}/billing"));
        await StepAsync("promote the group", () => GoAsync($"/organizations/{org}/promote"));
        await StepAsync("equipment feedback", () => GoAsync($"/organizations/{org}/equipment-feedback"));
        await StepAsync("feed attributions", () => GoAsync($"/organizations/{org}/feed-attributions"));
        await StepAsync("tours", () => GoAsync($"/organizations/{org}/tours"));
        await StepAsync("a tour", () => FollowIfAnyAsync($"/organizations/{org}/tours/"));
        await StepAsync("events", () => GoAsync($"/organizations/{org}/events"));
        await StepAsync("an event", () => FollowAsync($"/organizations/{org}/events/"));
        await StepAsync("the venue", () => GoAsync($"/organizations/{org}/venue"));
        await StepAsync("organization security", () => GoAsync("/organization-security"));
        await StepAsync("start another group", () => GoAsync("/organizations/new"));
        await StepAsync("case video editor", async () =>
        {
            await GoAsync($"/organizations/{org}/cases");
            await OpenFirstCaseAsync(org);
            caseAddress = new Uri(Page.Url).AbsolutePath;
            await GoAsync(caseAddress + "/video-editor");
        });
        await StepAsync("case audio mix", () => GoAsync(caseAddress + "/audio-mix"));
        await StepAsync("moderation", () => GoAsync("/moderation/media"));

        Finish();
    }

    // ── 6. The people who run the site ────────────────────────────────────────

    [Test]
    public async Task A_superadmin()
    {
        _persona = "6-superadmin";
        await LoginAsync(SuperAdminEmail, SuperAdminPassword);

        await StepAsync("dashboard", () => GoAsync("/admin/dashboard"), expect: "Sign-ins and registrations");
        await StepAsync("dashboard, events tab", () => GoAsync("/admin/dashboard?tab=events"), Main.Locator("#events-dashboard"));
        await StepAsync("dashboard, event health tab", () => GoAsync("/admin/dashboard?tab=event-health"), Main.Locator("#event-health"));
        await StepAsync("every screen of one event, from the list", async () =>
        {
            await GoAsync("/admin/events");
            var row = Main.Locator(".admin-events-grid tr.k-master-row").First;
            await Expect(row).ToBeVisibleAsync(new() { Timeout = 30_000 });
            await ClickUntilAsync(row.Locator("td.k-hierarchy-cell"), Main.Locator("[id^='screens-']"));
        }, Main.Locator("[id^='screens-']"));
        foreach (var route in new[]
                 {
                     "/admin/users", "/admin/cases", "/admin/investigations", "/admin/events", "/admin/event-credits",
                     "/admin/org-subscriptions", "/admin/subscription-tiers", "/admin/member-seats", "/admin/coupons",
                     "/admin/billing-ledger", "/admin/tax-rates", "/admin/referrals", "/admin/org-ads", "/admin/roles",
                     "/admin/site-settings", "/admin/rate-limits", "/admin/mail", "/admin/email-templates",
                     "/admin/audit-log", "/admin/error-log",
                     "/admin/support-tickets", "/admin/feed-reports", "/admin/test-posts", "/admin/venue-claims",
                     "/admin/place-duplicates", "/admin/merge-groups", "/admin/orphaned-sessions", "/admin/video-assets",
                     "/admin/sidecar-telemetry", "/admin/file-types", "/admin/lookup-types", "/admin/equipment-taxonomy",
                     "/admin/experience-taxonomy", "/admin/delete-case", "/admin/delete-group", "/admin/delete-user",
                 })
            await StepAsync(route.Replace("/admin/", "admin "), () => GoAsync(route));
        await StepAsync("one user", async () =>
        {
            await GoAsync("/admin/users");
            await FollowAsync("/admin/users/");
        });

        // The route alone proves nothing here: the page renders its list before anything is
        // chosen, so a broken editor would walk past as a perfectly good screen.
        await StepAsync("writing one of the site's letters", async () =>
        {
            await GoAsync("/admin/email-templates");
            await ClickUntilAsync(
                Main.GetByText("Reset your password", new() { Exact = true }).First,
                Main.Locator("[data-testid=template-body]"));
        }, Main.Locator("[data-testid=template-body]"));

        Finish();
    }
}
