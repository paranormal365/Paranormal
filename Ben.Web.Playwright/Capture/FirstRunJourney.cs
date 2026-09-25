using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Playwright;
using NUnit.Framework;

namespace Ben.Web.Playwright.Capture;

/// <summary>
/// The journey the product is FOR, walked once as a new team (2026-09-20).
/// </summary>
/// <remarks>
/// <para>Ben's words: <i>"a team signs up, creates their organisation, adds members, runs an
/// investigation with evidence, then sells tickets to a public event."</i> That is the sentence
/// the whole site exists to make true, and nothing had ever walked it end to end — ProductWalk
/// visits screens as people who already exist, on data that was seeded for them.</para>
///
/// <para><b>Nothing is allowed to end the run.</b> A step that fails is recorded and the next one
/// is tried, because the point is one list of everything wrong rather than the first thing wrong.
/// Every step is photographed whatever happens, and every step records the heading it landed on,
/// so a step that "passed" onto the wrong screen is still visible in the report.</para>
///
/// <para><b>Opt-in:</b> <c>BEN_JOURNEY=1</c>, pictures and report under <c>BEN_JOURNEY_OUT</c>.
/// It writes real rows — an account, a group, a case, an event — so it belongs on a testing copy
/// and not on anything anybody relies on.</para>
/// </remarks>
[TestFixture]
[Category("Capture")]
[NonParallelizable]
public sealed class FirstRunJourney : BenTestBase
{
    private static string OutRoot => Environment.GetEnvironmentVariable("BEN_JOURNEY_OUT") ?? "first-run";

    private readonly StringBuilder _report = new();
    private int _step;
    private string _orgId = "";
    private string _caseUrl = "";

    /// <summary>A name nobody else has, so a re-run never collides with the last one.</summary>
    private readonly string _stamp = Guid.NewGuid().ToString("N")[..8];

    private string TeamName => $"Hollow Creek Paranormal {_stamp}";
    private string TeamSlug => $"hollow-creek-{_stamp}";
    private string LeadEmail => $"lead-{_stamp}@example.test";
    /// <summary>
    /// Generated per run, never written down.
    /// </summary>
    /// <remarks>
    /// This was an inline literal until <c>NoCredentialsInTheRepoTests</c> caught it. The
    /// repository is public and development shares production's database, so a password constant
    /// in a tracked file is a live credential for whatever account the fixture creates with it —
    /// and the journey deliberately signs somebody up.
    /// </remarks>
    private readonly string _password = NewTestPassword();

    [OneTimeSetUp]
    public void SkipUnlessAsked()
    {
        if (Environment.GetEnvironmentVariable("BEN_JOURNEY") != "1")
            Assert.Ignore("Set BEN_JOURNEY=1 to walk the first-run journey. It writes real rows.");

        // Not on the shared scratch database.
        //
        // This walk FOUNDS A GROUP every time it runs, and the groups outlive the run. Seven runs
        // on 2026-09-21 left enough of them in IsHauntedDb_e2e to fail two tests that have nothing
        // to do with this file — OrgList_ShowsBenCo and AThreadWithNoMessagesOpensWithoutKilling-
        // ThePage — and cost an hour working out whether the branch under test had broken them.
        // A fixture that writes rows other tests can see has to say so before it writes them.
        var db = Environment.GetEnvironmentVariable("BEN_E2E_DB");
        if (string.IsNullOrWhiteSpace(db) || db == "IsHauntedDb_e2e")
        {
            Assert.Ignore(
                "This walk founds a real group that outlives the run, so it must not share the "
              + "suite's database. Give it one of its own:  BEN_E2E_DB=IsHauntedDb_journey "
              + "BEN_JOURNEY=1 scripts/run-e2e.sh --filter FirstRunJourney");
        }
    }

    [OneTimeTearDown]
    public void Write()
    {
        var dir = Path.Combine(OutRoot);
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "report.md"),
            $"# First run: a team signs up and sells tickets\n\n"
          + $"Team: {TeamName}  \nLead: {LeadEmail}\n\n"
          + $"| # | Step | Ended on | Heading | What happened |\n|---|---|---|---|---|\n{_report}");
    }

    /// <summary>
    /// Waits for the page to stop saying it is loading, and reports how that ended.
    /// </summary>
    /// <remarks>
    /// <para><c>WaitUntilLoadedAsync</c> looks for the word "Loading", and several screens spin
    /// with no words at all — so it returned at once and the walk photographed a spinner and
    /// called it ok. A wait that cannot notice is the same failure shape as a test that cannot
    /// fail, so this one watches the spinner too and <b>says</b> when the page never settled.</para>
    /// </remarks>
    private async Task<string?> SettleAsync(int timeoutMs = 30_000)
    {
        var started = DateTime.UtcNow;
        var deadline = started.AddMilliseconds(timeoutMs);
        while (DateTime.UtcNow < deadline)
        {
            var busy = false;
            try
            {
                busy = await Main.GetByText("Loading", new() { Exact = false }).CountAsync() > 0
                    || await Spinners.CountAsync() > 0;
            }
            catch (Exception) { return "the page went away while it was loading"; }

            if (!busy) return null;
            await Task.Delay(200);
        }
        return $"STILL LOADING after {timeoutMs / 1000}s";
    }

    /// <summary>One thing a person does. Photographed, recorded, never allowed to stop the walk.</summary>
    private async Task StepAsync(string name, Func<Task> act, string? expect = null)
    {
        _step++;
        var problems = new List<string>();

        try
        {
            await act();
            await WaitForTheCircuitAsync();

            if (expect is not null)
                await Expect(Page.GetByText(expect, new() { Exact = false }).First)
                    .ToBeVisibleAsync(new() { Timeout = 15_000 });
        }
        catch (Exception ex)
        {
            problems.Add(ex.Message.Split('\n')[0].Trim());
        }

        // Always, even when the step's own action threw: a step that failed on a page which never
        // finished loading must say so, because that is usually the reason it failed.
        if (await SettleAsync() is string stuck) problems.Add(stuck);

        // The banner is read from the CHROME, not the content: a page is allowed to talk about an
        // error without being one, which is how /changes fooled two crawlers.
        var heading = "";
        try
        {
            var banner = await Page.EvaluateAsync<bool>(@"() => {
                const main = document.querySelector('.app-content, main, .content-wrapper');
                const body = document.body.innerText || '';
                const inner = main ? (main.innerText || '') : '';
                const chrome = inner ? body.split(inner).join(' ') : body;
                const err = document.querySelector('#blazor-error-ui');
                return (!!err && getComputedStyle(err).display !== 'none')
                    || /An unhandled error has occurred/i.test(chrome);
            }");
            if (banner) problems.Add("unhandled error banner");

            // What a person would say they are looking at. A step that lands somewhere plausible
            // but wrong reports "ok" otherwise, which is the failure shape this walk exists for.
            heading = await Page.EvaluateAsync<string>(@"() => {
                const main = document.querySelector('.app-content, main, .content-wrapper') || document.body;
                const h = main.querySelector('h1, h2, h3');
                return h ? (h.innerText || '').trim().slice(0, 60) : '';
            }");
        }
        catch (Exception) { /* the page is gone; the step's own failure already says so */ }

        var file = $"{_step:00}-{Regex.Replace(name.ToLowerInvariant(), "[^a-z0-9]+", "-").Trim('-')}.png";
        Directory.CreateDirectory(OutRoot);
        try { await Page.ScreenshotAsync(new() { Path = Path.Combine(OutRoot, file), FullPage = true }); }
        catch (Exception) { problems.Add("no picture"); }

        var where = Page.Url.Replace(BaseUrl, "");
        var result = problems.Count == 0 ? "ok" : string.Join("; ", problems);
        _report.AppendLine($"| {_step} | {name} | `{where}` | {heading} | {result} |");
        TestContext.Out.WriteLine($"[{_step:00}] {name} — {where} — {heading} — {result}");
    }

    /// <summary>Clicks something if it is there, and says so in the report when it is not.</summary>
    private async Task<bool> ClickIfThereAsync(ILocator what)
    {
        if (await what.CountAsync() == 0) return false;
        await what.First.ClickAsync();
        return true;
    }

    private ILocator ButtonOrLink(string name)
        => Page.GetByRole(AriaRole.Button, new() { Name = name, Exact = false })
               .Or(Page.GetByRole(AriaRole.Link, new() { Name = name, Exact = false }));

    [Test]
    [Description("A team signs up, makes a group, adds somebody, investigates, and sells a ticket.")]
    public async Task A_team_arrives_and_sells_a_ticket()
    {
        // The shell scrolls inside .app-content rather than the document, so Playwright's FullPage
        // returns the viewport and nothing more. A tall window is what actually photographs a page.
        await Page.SetViewportSizeAsync(1280, 2000);

        // ── 1. Somebody finds the site and signs up ──────────────────────────
        await StepAsync("the front door", () => Page.GotoAsync($"{BaseUrl}/"));

        await StepAsync("fill in the sign-up form", async () =>
        {
            await Page.GotoAsync($"{BaseUrl}/signup");
            await FillAndConfirmAsync("#signup-first", "Casey");
            await FillAndConfirmAsync("#signup-last", "Hollow");
            await FillAndConfirmAsync("#signup-name", "Casey Hollow");
            await TypeHandleAsync($"casey{_stamp}");
            await FillAndConfirmAsync("#signup-email", LeadEmail);
            await FillAndConfirmAsync("#signup-password", _password);
        });

        await StepAsync("create the account", async () =>
        {
            if (!await ClickIfThereAsync(ButtonOrLink("Create account")))
                throw new Exception("no Create account button");
        });

        // Signing up needs a confirmed address on this site, so the rest is walked as a seat that
        // already exists. What the sign-up screens do is still photographed above — the point of
        // those two steps is whether somebody could get through them, not to become that person.
        await StepAsync("sign in as a group's owner", () => LoginAsync(UserEmail, UserPassword));

        // ── 2. They start a group, for real ──────────────────────────────────
        await StepAsync("start a group", () => Page.GotoAsync($"{BaseUrl}/organizations/new"));

        await StepAsync("name the group", async () =>
        {
            await FillAndConfirmAsync("#newgroup-name", TeamName);
            await FillAndConfirmAsync("#newgroup-url", TeamSlug);
            if (!await ClickIfThereAsync(Page.GetByRole(AriaRole.Button, new() { Name = "Next", Exact = true })))
                throw new Exception("no Next on the identity step");
        });

        await StepAsync("where they are based", async () =>
        {
            if (await Page.Locator("#newgroup-city").CountAsync() > 0)
            {
                await FillAndConfirmAsync("#newgroup-street", "9 Cotton Lane");
                await FillAndConfirmAsync("#newgroup-city", "Franklin");
                await FillAndConfirmAsync("#newgroup-state", "TN");
                await FillAndConfirmAsync("#newgroup-zip", "37064");
            }
            if (!await ClickIfThereAsync(Page.GetByRole(AriaRole.Button, new() { Name = "Next", Exact = true })))
                throw new Exception("no Next on the base step");
        });

        await StepAsync("first settings", async () =>
        {
            if (!await ClickIfThereAsync(Page.GetByRole(AriaRole.Button, new() { Name = "Next", Exact = true })))
                throw new Exception("no Next on the settings step");
        });

        await StepAsync("finish starting the group", async () =>
        {
            if (!await ClickIfThereAsync(Page.GetByRole(AriaRole.Button, new() { Name = "Create the group", Exact = false })
                                             .Or(Page.GetByRole(AriaRole.Button, new() { Name = "Finish", Exact = false }))))
                throw new Exception("nothing on the review step creates the group");
            await Page.WaitForURLAsync(u => Regex.IsMatch(u, @"/organizations/[0-9a-fA-F-]{36}"),
                new() { Timeout = 20_000 });
        });

        var made = Regex.Match(Page.Url, @"/organizations/([0-9a-fA-F-]{36})");
        if (made.Success) _orgId = made.Groups[1].Value;

        // ── the list of groups, and a way back in ────────────────────────────
        await StepAsync("the groups they are in", () => Page.GotoAsync($"{BaseUrl}/organizations"));

        await StepAsync("open the group from the list", async () =>
        {
            await Page.GotoAsync($"{BaseUrl}/organizations");
            await WaitForTheCircuitAsync();
            await SettleAsync();
            await Expect(Page.Locator("tbody tr").First).ToBeVisibleAsync(new() { Timeout = 20_000 });
            var row = Page.Locator("tr", new() { HasTextString = TeamName }).First;
            var opener = await row.CountAsync() > 0
                ? row.GetByRole(AriaRole.Button, new() { Name = "View" })
                     .Or(row.GetByRole(AriaRole.Link, new() { Name = "View" }))
                : Page.GetByRole(AriaRole.Button, new() { Name = "View" });
            if (!await ClickIfThereAsync(opener))
                throw new Exception("nothing in the row opens the group");
            await Page.WaitForURLAsync(u => Regex.IsMatch(u, @"/organizations/[0-9a-fA-F-]{36}$"),
                new() { Timeout = 15_000 });
        });

        if (string.IsNullOrEmpty(_orgId))
        {
            var fromList = Regex.Match(Page.Url, @"/organizations/([0-9a-fA-F-]{36})");
            if (fromList.Success) _orgId = fromList.Groups[1].Value;
        }

        if (string.IsNullOrEmpty(_orgId))
        {
            // The new group could not be reached. Fall back to a seeded one so everything after
            // this is still walked — and say loudly in the report that this happened.
            await StepAsync("THE NEW GROUP COULD NOT BE REACHED — walking a seeded one instead", async () =>
            {
                _orgId = await OrgIdBySlugAsync("paranormal365");
                await Page.GotoAsync($"{BaseUrl}/organizations/{_orgId}");
            });
        }

        await StepAsync("the group's own page", () => Page.GotoAsync($"{BaseUrl}/organizations/{_orgId}"));

        // ── 3. Members ───────────────────────────────────────────────────────
        await StepAsync("members", () => Page.GotoAsync($"{BaseUrl}/organizations/{_orgId}/members"));

        await StepAsync("invite somebody", async () =>
        {
            if (!await ClickIfThereAsync(ButtonOrLink("Invite")))
                throw new Exception("nothing on the members page invites anybody");
        });

        await StepAsync("what a member may do", async () =>
        {
            await Page.GotoAsync($"{BaseUrl}/organizations/{_orgId}/members");
            var t = Page.GetByRole(AriaRole.Tab, new() { Name = "Roles", Exact = false })
                    .Or(Main.Locator(".nav-tabs .nav-link", new() { HasTextString = "Roles" }));
            if (!await ClickIfThereAsync(t))
                throw new Exception("nothing on the members screen says what a member may do");
        });

        // ── 4. A case, and an investigation with evidence ────────────────────
        await StepAsync("the group's cases", () => Page.GotoAsync($"{BaseUrl}/organizations/{_orgId}/cases"));

        await StepAsync("open a new case", async () =>
        {
            await Page.GotoAsync($"{BaseUrl}/organizations/{_orgId}/cases/new");
            await FillAndConfirmAsync("#casecreatepage-case-title-b1b1", $"Hollow Creek Road, Franklin TN {_stamp}");
            await FillAndConfirmAsync("#casecreatepage-street-address-5b76", "41 Hollow Creek Road");
            await FillAndConfirmAsync("#casecreatepage-city-4662", "Franklin");
            await FillAndConfirmAsync("#casecreatepage-state-7b45", "TN");
            await FillAndConfirmAsync("#casecreatepage-zip-code-ba79", "37064");

            // Open Case stays disabled until the kind of place is answered, and the question is
            // below the fold — so the walk answers it the way a person would have to.
            // The title is the first field on a freshly navigated page, so the first interactive
            // render can wipe what was typed into it — the documented Blazor Server race. Put it
            // back now that the circuit is certainly up, and say so if it will not hold.
            await FillAndConfirmAsync("#casecreatepage-case-title-b1b1", $"Hollow Creek Road, Franklin TN {_stamp}");

            // Answering the place question and SEEING it answered are two different claims, so
            // both are made. A form that takes an answer without showing it is what a person hits
            // when they click, see nothing happen, and click the other one.
            var chosen = Page.Locator("#case-place-kind-public");
            await chosen.ClickAsync();
            if (!await chosen.IsCheckedAsync())
                throw new Exception("clicking Public location leaves neither place option showing as chosen");

            var open = Page.GetByRole(AriaRole.Button, new() { Name = "Open Case", Exact = false }).First;
            try { await Expect(open).ToBeEnabledAsync(new() { Timeout = 10_000 }); }
            catch (Exception) { throw new Exception("Open Case is still disabled with the whole form filled in"); }
            await open.ClickAsync();
            await Page.WaitForURLAsync(u => Regex.IsMatch(u, @"/cases/[0-9a-fA-F-]{36}"),
                new() { Timeout = 20_000 });
        });

        if (Regex.IsMatch(Page.Url, @"/cases/[0-9a-fA-F-]{36}")) _caseUrl = Page.Url;

        await StepAsync("the case", async () =>
        {
            if (_caseUrl.Length > 0) await Page.GotoAsync(_caseUrl);
            else
            {
                await Page.GotoAsync($"{BaseUrl}/organizations/{_orgId}/cases");
                if (!await ClickIfThereAsync(Page.Locator("a[href*='/cases/'], button:has-text('View')")))
                    throw new Exception("no case to open");
            }
        });

        foreach (var tab in new[] { "Timeline", "Investigations", "Evidence", "Files", "Research", "Notes" })
        {
            var name = tab;
            await StepAsync($"the case's {name.ToLowerInvariant()}", async () =>
            {
                if (_caseUrl.Length == 0) throw new Exception("no case was made, so its tabs cannot be opened");
                var t = Page.GetByRole(AriaRole.Tab, new() { Name = name, Exact = false })
                        .Or(Main.Locator(".nav-tabs .nav-link", new() { HasTextString = name }));
                if (!await ClickIfThereAsync(t))
                    throw new Exception($"no {name} tab on a case");
            });
        }

        // ── 5. A public event, with tickets ──────────────────────────────────
        await StepAsync("the group's events", () => Page.GotoAsync($"{BaseUrl}/organizations/{_orgId}/events"));

        await StepAsync("start an event", async () =>
        {
            var add = Page.Locator("#add-event")
                      .Or(ButtonOrLink("New event")).Or(ButtonOrLink("Add event")).Or(ButtonOrLink("Add it"));
            if (await add.CountAsync() == 0)
                throw new Exception("nothing on the events page starts an event");
            await add.First.ScrollIntoViewIfNeededAsync();
        });

        await StepAsync("what the group's plan costs", () => Page.GotoAsync($"{BaseUrl}/organizations/{_orgId}/billing"));
        await StepAsync("the price list", () => Page.GotoAsync($"{BaseUrl}/pricing"));
        await StepAsync("what the public sees", () => Page.GotoAsync($"{BaseUrl}/events"));

        await StepAsync("a stranger looks at what's on", async () =>
        {
            await LogoutAsync();
            await Page.GotoAsync($"{BaseUrl}/events");
        });

        await StepAsync("a stranger opens an event", async () =>
        {
            var row = Page.Locator("a[href*='/events/']").First;
            if (!await ClickIfThereAsync(row)) throw new Exception("nothing on What's On opens an event");
            await Page.WaitForURLAsync(u => u.Contains("/events/"), new() { Timeout = 15_000 });
        });

        await StepAsync("the event page finishes loading", async () =>
        {
            await Expect(Page.Locator("#event-act, #hosted-booking, #attend-send, .event-act").First)
                .ToBeVisibleAsync(new() { Timeout = 25_000 });
        });

        await StepAsync("a stranger tries to book", async () =>
        {
            // A stranger with no account books by leaving an address, so the address goes in first.
            var email = Main.Locator("input[type=email]").First;
            if (await email.CountAsync() > 0)
                await email.FillAsync($"guest-{_stamp}@example.test");

            var ask = Main.GetByRole(AriaRole.Button, new() { Name = "Ask for a place", Exact = false })
                      .Or(Main.GetByRole(AriaRole.Button, new() { Name = "I'm coming", Exact = false }))
                      .Or(Main.GetByRole(AriaRole.Button, new() { Name = "Send me the link", Exact = false }));
            if (await ask.CountAsync() == 0)
                throw new Exception("no way to ask for a place from a public event page");
            try { await Expect(ask.First).ToBeEnabledAsync(new() { Timeout = 10_000 }); }
            catch (Exception) { throw new Exception("the only way in on this page stays disabled"); }
            await ask.First.ClickAsync();

            // The end of the journey has to SAY something. A click that leaves the form exactly as
            // it was is indistinguishable from a click that did nothing, which is the one thing a
            // stranger handing over their address must never be left wondering.
            // Both of these are words that exist on this page ONLY after the click, so neither can
            // be satisfied by the sentence that was already there. The first version of this check
            // matched "link" in the form's own intro and therefore could not fail.
            try
            {
                await Expect(Main.GetByText("Check your email", new() { Exact = false })
                        .Or(Main.GetByText("couldn't be sent", new() { Exact = false }))
                        .First)
                    .ToBeVisibleAsync(new() { Timeout = 15_000 });
            }
            catch (Exception)
            {
                throw new Exception("asking for a place said nothing back — the form is unchanged");
            }
        });

        // How many of the nights on What's On can actually be opened. A list whose rows go nowhere
        // is the shape this codebase keeps shipping, so it is counted rather than eyeballed — and
        // the count is proved to have found some rows first, because "0 of 0" is not a pass.
        await StepAsync("how many of tonight's events can be opened", async () =>
        {
            await Page.GotoAsync($"{BaseUrl}/events");
            await SettleAsync();

            var rows = await Main.Locator(".ev-row").CountAsync();
            if (rows == 0) throw new Exception("What's On listed nothing, so nothing was checked");

            var openable = await Main.Locator(".ev-row .ev-row__title a").CountAsync();
            if (openable < rows)
                throw new Exception($"{openable} of {rows} listed nights can be opened; the rest are plain text");
        });
    }
}
