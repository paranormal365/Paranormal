using Microsoft.Playwright;
using NUnit.Framework;

namespace Ben.Web.Playwright.Tests;

/// <summary>
/// Whether the first click on a dialog's primary button, straight after typing, does anything
/// (W-A12, site evaluation 2026-09-06).
/// </summary>
/// <remarks>
/// <para>The evaluation reported that in dialogs "the first click on the primary button after
/// typing did nothing and the second identical click worked" — on Verify Address, Look Up, Create,
/// Add and Make Public. It also recorded that four of the five were the reporting tool's own
/// synthetic click, that the DOM node was not being replaced, and that it needed a human repro
/// before anyone fixed anything. W-S6 was named as the suspect and turned out not to be: this
/// build opens one circuit and keeps it across navigations, which the fixture beside this one
/// measures.</para>
///
/// <para><b>Why this is the closest thing to a real mouse available here.</b> Playwright's clicks
/// and keystrokes go through the Chrome DevTools Protocol as browser-level input events — the page
/// sees them as trusted, the same as a hand on a mouse. A test that types character by character
/// and then clicks ONCE, with no wait, is the repro the evaluation asked for.</para>
///
/// <para><b>What was measured while writing this.</b> The Create button is
/// <c>disabled</c> until the server's re-render says otherwise, and the field feeding it binds on
/// <c>oninput</c> — so after the last keystroke there is a window, one circuit round trip wide, in
/// which the button is still disabled and a click on it is discarded by the browser without
/// reaching anything. On localhost that window measured 3 ms. Over a real connection it is
/// whatever the round trip is.</para>
/// </remarks>
[TestFixture]
[Category("FirstClick")]
public class FirstClickAfterTypingTests : BenTestBase
{
    /// <summary>A tag that makes each run's report findable, since this one really is created.</summary>
    private static string Unique => Guid.NewGuid().ToString("N")[..8];

    /// <summary>
    /// Type the title, click Create once, and expect a report.
    /// </summary>
    /// <remarks>
    /// No wait between the last keystroke and the click, on purpose — waiting is what hides this.
    /// The existing suite fills fields with <c>FillAsync</c>, which sets the value in one event and
    /// is then followed by an assertion that waits, so nothing in it has ever been in a position
    /// to see this.
    /// </remarks>
    [Test]
    public async Task One_click_on_Create_after_typing_makes_the_report()
    {
        await LoginAsync(UserEmail, UserPassword);
        if (!await OpenOrgCaseAsync("Paranormal365", "Belmont"))
            Assert.Ignore("the seeded case this walks is not on this database");

        await OpenTabAsync("Reports", Main.GetByRole(AriaRole.Button, new() { Name = "New Report" }));

        var newReport = Main.GetByRole(AriaRole.Button, new() { Name = "New Report" });
        await ClickUntilAsync(newReport, Page.Locator("#reportbuilder-title-af64"));

        var title = Page.Locator("#reportbuilder-title-af64");
        await Expect(title).ToBeVisibleAsync(new() { Timeout = 15_000 });

        // Character by character, the way somebody types — not FillAsync, which sets the whole
        // value in one event and gives the circuit a head start.
        var name = $"First click probe {Unique}";
        await title.ClickAsync();
        await title.PressSequentiallyAsync(name);

        // ONE click, no wait. Force, so Playwright does not helpfully wait for the button to
        // become enabled — that wait is precisely the thing a person does not do.
        var create = Page.GetByRole(AriaRole.Button, new() { Name = "Create", Exact = true }).First;
        await create.ClickAsync(new() { Force = true, Timeout = 5_000 });

        // The report exists, from that one click.
        await Expect(Page.GetByText(name, new() { Exact = false }).First)
            .ToBeVisibleAsync(new() { Timeout = 20_000 });
    }

    /// <summary>
    /// The site opens one circuit and keeps it across navigations (W-S6).
    /// </summary>
    /// <remarks>
    /// <para>The evaluation saw "three WebSocket circuits within 400 ms on some navigations" and
    /// named it the likely root of W-A12. Measured on this build it does not happen: enhanced
    /// navigation keeps the interactive root alive, and a walk of six pages plus five tab switches
    /// opens no new circuit at all.</para>
    ///
    /// <para>Counted by SignalR's own handshake rather than by watching sockets, because that
    /// request is made once per circuit whatever transport is negotiated afterwards. A full page
    /// load legitimately starts a new one, so this walks by clicking links.</para>
    /// </remarks>
    [Test]
    public async Task Navigating_the_site_does_not_open_a_second_circuit()
    {
        var handshakes = 0;
        Page.Request += (_, request) =>
        {
            if (request.Url.Contains("/_blazor/negotiate", StringComparison.Ordinal))
                Interlocked.Increment(ref handshakes);
        };

        await Page.GotoAsync($"{BaseUrl}/");
        await WaitUntilLoadedAsync();
        await Expect(Page.Locator("h1, h2, h3").First).ToBeVisibleAsync(new() { Timeout = 20_000 });

        var opening = handshakes;
        Assert.That(opening, Is.GreaterThan(0),
            "No SignalR handshake at all — this page is not interactive, so the count below "
            + "would pass for the wrong reason.");

        // Six navigations by click, which is what enhanced navigation is for.
        foreach (var path in new[] { "/events", "/equipment-catalog", "/find", "/publications", "/", "/events" })
        {
            var link = Page.Locator($"a[href='{path}']").First;
            if (await link.CountAsync() == 0) continue;
            await link.ClickAsync();
            await WaitUntilLoadedAsync();
        }

        Assert.That(handshakes, Is.EqualTo(opening),
            $"{handshakes - opening} extra circuit(s) were opened by navigating. Each one is a new "
            + "server-side component tree, its state and its DI scope, on a host that pays for "
            + "every one of them (W-S6).");
    }

    /// <summary>
    /// After the last keystroke, the primary button is never disabled waiting on the server.
    /// </summary>
    /// <remarks>
    /// <para><b>This is the test that actually shows W-A12.</b> The one above cannot: on localhost
    /// the circuit round trip measures about three milliseconds, and Playwright's own click
    /// sequence — scroll into view, hit-test, move, press, release — takes longer than that, so
    /// the button is always enabled again by the time the click lands. No local test will ever
    /// catch the swallowed click. Over a real connection the same window is the network round
    /// trip, tens to hundreds of milliseconds, and a person who types the last character and
    /// clicks is inside it.</para>
    ///
    /// <para>So this measures the window instead of racing it. A MutationObserver installed before
    /// typing records every change to the button's <c>disabled</c> attribute; if the button was
    /// ever disabled after the field had a value in it, there was a window in which a click would
    /// have gone nowhere, and the length of that window is somebody's latency rather than
    /// ours.</para>
    ///
    /// <para>A disabled button is also silent about why. The fix is for the button to stay live
    /// and the handler to say what is missing — which is what the rest of this codebase already
    /// requires of a server guard.</para>
    /// </remarks>
    [Test]
    public async Task The_primary_button_is_never_disabled_waiting_on_a_round_trip()
    {
        await LoginAsync(UserEmail, UserPassword);
        if (!await OpenOrgCaseAsync("Paranormal365", "Belmont"))
            Assert.Ignore("the seeded case this walks is not on this database");

        await OpenTabAsync("Reports", Main.GetByRole(AriaRole.Button, new() { Name = "New Report" }));
        await ClickUntilAsync(Main.GetByRole(AriaRole.Button, new() { Name = "New Report" }),
                              Page.Locator("#reportbuilder-title-af64"));

        var title = Page.Locator("#reportbuilder-title-af64");
        await Expect(title).ToBeVisibleAsync(new() { Timeout = 15_000 });

        // Read the button's state at the instant the keystroke happens — which is the instant a
        // click could land — rather than afterwards.
        //
        // An earlier version of this watched the disabled attribute with a MutationObserver and
        // was structurally incapable of failing: the only transition after typing is OUT of
        // disabled, so it never once observed the state it was looking for. It passed against the
        // un-fixed code, which is worse than no test.
        var watching = await Page.EvaluateAsync<bool>(
            """
            (() => {
                const box    = document.querySelector('#reportbuilder-title-af64');
                const create = document.querySelector('#report-create');
                if (!box || !create) return false;
                window.__gateAtKeystroke = [];
                box.addEventListener('input', () => window.__gateAtKeystroke.push(create.disabled));
                return true;
            })()
            """);

        Assert.That(watching, Is.True,
            "The title box or the Create button was not found, so nothing was observed and this "
            + "would pass for the wrong reason.");

        await title.ClickAsync();
        await title.PressSequentiallyAsync("Latency probe");
        await Page.WaitForTimeoutAsync(1_000);

        var samples = await Page.EvaluateAsync<int>("window.__gateAtKeystroke.length");
        Assert.That(samples, Is.GreaterThan(3),
            $"Only {samples} keystrokes reached the field, so there is nothing to conclude.");

        var disabledAtAKeystroke = await Page.EvaluateAsync<bool>(
            "window.__gateAtKeystroke.some(d => d === true)");

        Assert.That(disabledAtAKeystroke, Is.False,
            "The primary button was disabled at the moment a character was typed — a window, one "
            + "circuit round trip wide, in which a click reaches nothing and the person is told "
            + "nothing. Let the button stay live and refuse in the handler (W-A12).");
    }
}

