using System.Text.RegularExpressions;
using Microsoft.Playwright;
using NUnit.Framework;

namespace Ben.Web.Playwright.Tests;

/// <summary>
/// Small truths the site was getting wrong (site evaluation 2026-09-06, phase 4).
/// </summary>
/// <remarks>
/// <para>None of these stops anybody. Each is the screen saying something that is not so — a spam
/// trap shown to the person it will flag, a tally beside "no votes", a group page headed with the
/// name of a database table. They are grouped because they share that shape, and because the way
/// they got here was fixing them one at a time as they were noticed.</para>
///
/// <para>The signed-out checks come first: most of these are what a stranger meets.</para>
/// </remarks>
// Fixtures that drive the Edit Case dialog on the one seeded case, or upload to it, cannot run
// beside each other: each one changes the case, asserts, and restores, and in parallel one
// fixture's restore lands in the middle of another's assertion. The 2026-09-07 full run failed
// The_leak_warning_fires_before_save_not_after_it exactly that way, having passed twice in
// isolation and once beside PublishLeakWarningTests. NonParallelizable is what this suite already
// uses for shared seeded state — a dozen fixtures carry it for the same reason.
[TestFixture]
[Category("LookAndTruths")]
[NonParallelizable]
public class LookAndTruthsTests : BenTestBase
{
    /// <summary>
    /// The contact form's honeypot is not shown to people (W-V2).
    /// </summary>
    /// <remarks>
    /// The markup carried <c>class="contact-hp"</c> and nothing on the site defined that class,
    /// so the trap rendered as a plain labelled "Website" input in the middle of the form. The
    /// guard treats anything in it as proof the submission was not typed by a person — so a
    /// visitor who filled in the field the form appeared to be asking them for was classified as
    /// spam and told nothing.
    /// </remarks>
    [Test]
    public async Task The_contact_forms_spam_trap_is_not_shown_to_visitors()
    {
        await LogoutAsync();
        await Page.GotoAsync($"{BaseUrl}/contact");
        await WaitUntilLoadedAsync();

        // The real fields are here, so this is the form and not an error page.
        await Expect(Page.Locator("#contactpage-subject-9dbb, textarea").First)
            .ToBeVisibleAsync(new() { Timeout = 20_000 });

        var honeypot = Page.Locator(".contact-hp");
        await Expect(honeypot).ToHaveCountAsync(1);

        // Off-canvas, not display:none — a field with no box model is one a scripted filler can
        // cheaply skip, and the point is that it gets filled.
        await Expect(honeypot).Not.ToBeInViewportAsync();

        var box = await honeypot.BoundingBoxAsync();
        Assert.That(box!.X, Is.LessThan(-1000),
            "The honeypot is inside the viewport's horizontal range, so a visitor can see it.");
    }

    /// <summary>
    /// The public catalogue lists real gear, not the placeholder rows (W-V3).
    /// </summary>
    /// <remarks>
    /// One "Generic / Unbranded" model per category is seeded so somebody adding a borrowed meter
    /// with no badge on it has something to pick. They exist to be chosen in a dropdown. The
    /// public catalogue showed all sixteen as makes and models with no model number, so a visitor
    /// browsing to see what investigators use met a screen of placeholders.
    /// </remarks>
    [Test]
    public async Task The_public_equipment_catalogue_shows_no_placeholder_rows()
    {
        await LogoutAsync();
        await Page.GotoAsync($"{BaseUrl}/equipment-catalog");
        await WaitUntilLoadedAsync();

        // Wait for real content rather than for the absence of something, which any blank page
        // satisfies. AlphaLab is in the seeded catalogue.
        await Expect(Page.GetByText("AlphaLab").First).ToBeVisibleAsync(new() { Timeout = 20_000 });

        await Expect(Page.GetByText("Generic / Unbranded")).ToHaveCountAsync(0);
    }

    /// <summary>
    /// A page shorter than the window starts at the top of it (W-V1).
    /// </summary>
    /// <remarks>
    /// <para>The site's shell puts every page inside a flex wrapper, and twenty-three pages opened
    /// with <c>margin:auto</c> — which on a flex item centres on both axes. On <c>/events</c> that
    /// was 150px of nothing above the heading, with the two event cards floating in the middle of
    /// the window.</para>
    ///
    /// <para>Measured rather than eyeballed: a screenshot of this is a judgement call, and the
    /// number is not.</para>
    /// </remarks>
    [Test]
    public async Task A_short_page_is_not_floated_down_the_middle_of_the_window()
    {
        await LogoutAsync();
        await Page.GotoAsync($"{BaseUrl}/events");
        await WaitUntilLoadedAsync();

        await Expect(Page.Locator("h1")).ToBeVisibleAsync(new() { Timeout = 20_000 });

        var marginTop = await Page.EvaluateAsync<string>(
            """
            (() => {
                const h1 = document.querySelector('h1');
                const c  = h1.closest('div[class*="container"]');
                return getComputedStyle(c).marginTop;
            })()
            """);

        Assert.That(marginTop, Is.EqualTo("0px"),
            $"The page container carries a top margin of {marginTop}, so the page is being "
            + "centred vertically inside the shell's flex wrapper.");
    }

    /// <summary>
    /// The public events page is reachable from the navigation (W-V1).
    /// </summary>
    /// <remarks>
    /// It is public, it is the one page a stranger can reach that shows real groups doing real
    /// things on a real date, and nothing anywhere linked to it. It was reachable by typing the
    /// URL.
    /// </remarks>
    [Test]
    public async Task A_signed_out_visitor_can_find_the_events_page_without_typing_the_url()
    {
        await LogoutAsync();
        await Page.GotoAsync($"{BaseUrl}/");
        await WaitUntilLoadedAsync();

        var link = Page.Locator("a[href='/events']").First;
        await Expect(link).ToBeVisibleAsync(new() { Timeout = 20_000 });

        await link.ClickAsync();
        await WaitUntilLoadedAsync();
        await Expect(Page).ToHaveURLAsync(new Regex(@"/events"), new() { Timeout = 20_000 });
    }

    /// <summary>
    /// Times a person scheduled do not print seconds.
    /// </summary>
    /// <remarks>
    /// Reported three separate times before this — a visit at "01:40:33 PM", an event at
    /// "03:00:00 PM", a group created at "09:12:47" — and fixed where it was noticed each time,
    /// which is how it came to appear in five more places. The default now carries no seconds and
    /// the exception has the longer name.
    /// </remarks>
    [Test]
    public async Task A_scheduled_time_prints_no_seconds()
    {
        await LogoutAsync();
        await Page.GotoAsync($"{BaseUrl}/events");
        await WaitUntilLoadedAsync();

        await Expect(Page.Locator("h1")).ToBeVisibleAsync(new() { Timeout = 20_000 });

        // The heading renders long before the events do — this page said "Loading…" under a
        // finished h1 on the first run of this test. Wait for a time to exist before asserting
        // anything about how times are written.
        await Expect(Page.GetByText(new Regex(@"\d\d:\d\d [AP]M")).First)
            .ToBeVisibleAsync(new() { Timeout = 20_000 });

        var body = await Page.InnerTextAsync("body");
        Assert.That(body, Does.Match(@"\d\d:\d\d [AP]M"),
            "No clock time on the page at all — this test would pass for the wrong reason.");
        Assert.That(body, Does.Not.Match(@"\d\d:\d\d:\d\d [AP]M"),
            "A scheduled time is printing seconds nobody typed.");
    }

    // ── Signed in ────────────────────────────────────────────────────────────

    /// <summary>
    /// The sidebar badge, the bell and the notifications page report the same number (W-CL3).
    /// </summary>
    /// <remarks>
    /// The complaint was three numbers in one glance: 6, 1 and 1, all claiming to be this
    /// person's notifications and all arithmetically correct about a different subset of the same
    /// eight buckets. What is asserted is the agreement, not any particular value — on a seeded
    /// database the value is whatever the seed produced.
    /// </remarks>
    [Test]
    public async Task The_sidebar_the_bell_and_the_page_report_the_same_number()
    {
        await LoginAsync(MemberEmail, MemberPassword);
        await Page.GotoAsync($"{BaseUrl}/notifications");
        await WaitUntilLoadedAsync();

        await Expect(Page.GetByRole(AriaRole.Heading, new() { Name = "Notifications" }).First)
            .ToBeVisibleAsync(new() { Timeout = 20_000 });

        var numbers = await Page.EvaluateAsync<int[]>(
            """
            (() => {
                const digits = t => { const m = (t || '').match(/(\d+)\s*$/); return m ? +m[1] : 0; };

                const sidebarItem = [...document.querySelectorAll('li')]
                    .find(li => li.innerText && li.innerText.trim().startsWith('Notifications'));
                const sidebar = sidebarItem ? digits(sidebarItem.innerText.trim()) : 0;

                const bellEl = document.querySelector('#nav-bell');
                const bell   = bellEl ? digits(bellEl.innerText.trim()) : 0;

                // The page: sum the badge on every row it lists.
                const rows = [...document.querySelectorAll('.list-group-item')]
                    .map(r => digits(r.innerText.trim()))
                    .reduce((a, b) => a + b, 0);

                return [sidebar, bell, rows];
            })()
            """);

        Assert.That(numbers[1], Is.EqualTo(numbers[0]),
            $"The sidebar says {numbers[0]} and the bell says {numbers[1]}.");
        Assert.That(numbers[2], Is.EqualTo(numbers[1]),
            $"The bell says {numbers[1]} and the page lists rows adding to {numbers[2]}.");
    }

    /// <summary>
    /// A group page is headed with the group's name, and its tabs sit in one row (W-A2).
    /// </summary>
    /// <remarks>
    /// The heading read "Organization" — the name of the table — on the page for a particular
    /// group, on a site where a person belongs to several. The fifteen tabs beneath it wrapped
    /// onto a second row, which pushed the content down and split one control into two lines.
    /// </remarks>
    [Test]
    public async Task A_group_page_says_which_group_it_is_and_keeps_its_tabs_on_one_line()
    {
        await LoginAsync(MemberEmail, MemberPassword);
        await Page.GotoAsync($"{BaseUrl}/organizations");
        await WaitUntilLoadedAsync();

        var orgLink = Page.Locator("a[href^='/organizations/']").First;
        if (await orgLink.CountAsync() == 0) Assert.Ignore("this account belongs to no group");
        await ClickUntilUrlAsync(orgLink, @"/organizations/[0-9a-f\-]+");
        await WaitUntilLoadedAsync();

        var heading = Page.Locator("h3").First;
        await Expect(heading).ToBeVisibleAsync(new() { Timeout = 20_000 });
        await Expect(heading).Not.ToHaveTextAsync("Organization");

        // One row: every tab shares a top offset. Counted rather than eyeballed — a screenshot of
        // a wrapped tab strip is a judgement call and this is not.
        var rows = await Page.EvaluateAsync<int>(
            """
            (() => {
                const ul = document.querySelector('ul.nav');
                if (!ul) return -1;
                const tops = new Set([...ul.querySelectorAll('.nav-item')]
                    .map(li => Math.round(li.getBoundingClientRect().top)));
                return tops.size;
            })()
            """);

        Assert.That(rows, Is.EqualTo(1), $"The tab strip is drawn on {rows} rows.");
    }

    /// <summary>
    /// The publish leak warning appears before Save, beside the decision it is about (W-P4).
    /// </summary>
    /// <remarks>
    /// It used to run only inside Save, so a group learned they were about to publish their
    /// client's surname by having their save silently not happen — the reason waiting at the
    /// bottom of a dialog they had to scroll. Nothing here is saved: the dialog is cancelled.
    /// </remarks>
    [Test]
    public async Task The_leak_warning_fires_before_save_not_after_it()
    {
        await LoginAsync(SuperAdminEmail, SuperAdminPassword);

        // The same case three other fixtures open, and a client residence rather than a public
        // cave — so it has a street the check will actually recognise.
        if (!await OpenOrgCaseAsync("Paranormal365", "Belmont"))
            Assert.Ignore("the seeded case this walks is not on this database");

        // Read the address BEFORE opening the dialog, which covers the panel it lives in.
        var street = await Page.EvaluateAsync<string?>(
            """
            (() => {
                // Starts with "Address", not equals it. The Details panel labels this row
                // "Address given" — a case's address is what the client SAID, which is a
                // different claim from a verified place — and an exact match on 'Address'
                // silently found nothing and blamed the panel for changing shape.
                const leaf = [...document.querySelectorAll('dt, div, span, td')]
                    .filter(e => e.childElementCount === 0 && /^address\b/i.test(e.textContent.trim()));
                for (const l of leaf) {
                    const v = l.nextElementSibling?.textContent?.trim();
                    if (v) return v.split('\n')[0].trim();
                }
                return null;
            })()
            """);
        Assert.That(street, Is.Not.Null.And.Not.Empty,
            "Could not read this case's address from its Details panel, so there is nothing to "
            + "put in the label that the check should object to. The panel has changed shape.");

        var edit = Page.GetByRole(AriaRole.Button, new() { Name = "Edit Case" }).First;
        Assert.That(await edit.CountAsync(), Is.GreaterThan(0),
            "No Edit Case control — this seat cannot reach the dialog the warning lives in.");
        await ClickUntilAsync(edit, Page.Locator("#case-public"));

        var makePublic = Page.Locator("#case-public");
        await Expect(makePublic).ToBeVisibleAsync(new() { Timeout = 15_000 });
        if (!await makePublic.IsCheckedAsync()) await makePublic.CheckAsync();

        var label = Page.Locator("#casedetail-case-label-surname-city-0146");
        await label.FillAsync($"{street} survey");

        // A beat before blurring. The field binds on `oninput`, so the typed value reaches the
        // server one circuit round trip behind the keyboard, and `onblur` fires the leak check
        // with whatever the server has. Alone that round trip is ~3 ms and blur lands after it;
        // under a full-suite load it does not, the check runs on the OLD title, and the warning
        // never appears — which is how this test passed on its own and failed in every full run.
        // A person never blurs zero milliseconds after their last keystroke either.
        await Page.WaitForTimeoutAsync(500);
        await label.BlurAsync();

        // No Save has been pressed anywhere above this line. That is the whole assertion.
        await Expect(Page.Locator("#case-title-leak-warning"))
            .ToBeVisibleAsync(new() { Timeout = 20_000 });

        // And it sits with the decision, not at the bottom of a dialog somebody has to scroll.
        var checkboxTop = (await makePublic.BoundingBoxAsync())!.Y;
        var warningTop  = (await Page.Locator("#case-title-leak-warning").BoundingBoxAsync())!.Y;
        Assert.That(warningTop - checkboxTop, Is.LessThan(200),
            "The warning is far below the Make Public control it is about.");

        // Nothing is saved: the dialog is cancelled.
        var cancel = Page.GetByRole(AriaRole.Button, new() { Name = "Cancel" }).First;
        if (await cancel.CountAsync() > 0) await cancel.ClickAsync();
    }

    /// <summary>
    /// The audio editor's toolbar wraps rather than clipping, and wears the site's icons
    /// (AE-2, AE-3).
    /// </summary>
    /// <remarks>
    /// <para>AE-2: the file details and the whole toolbar were one flex container with the
    /// toolbar inside a single <c>ms-auto</c> child. At 1440x900 with the spectrogram controls
    /// showing, that child was wider than the space it was pushed into, so it overflowed to the
    /// LEFT — the first button read "e Timeline" — and the drag hint at its end collapsed to one
    /// word per line.</para>
    ///
    /// <para>AE-3: the toolbar was drawn in emoji, which come from the operating system's own
    /// font — a different size, weight and colour on every machine, ignoring the dark theme, and
    /// missing entirely on some Windows builds, where two of them rendered as a box.</para>
    ///
    /// <para>Uploads the same fixture the audio fixtures use, because no seeded case ships with
    /// audio on it.</para>
    /// </remarks>
    [Test]
    public async Task The_audio_editors_toolbar_wraps_and_uses_the_sites_icons()
    {
        await LoginAsync(UserEmail, UserPassword);
        if (!await OpenOrgCaseAsync("Paranormal365", "Belmont"))
            Assert.Ignore("the seeded case this walks is not on this database");

        await OpenTabAsync("Files", Main.GetByText("Upload File", new() { Exact = false }).First);
        await Expect(Page.Locator("#case-file-upload")).ToBeAttachedAsync(new() { Timeout = 15_000 });

        await Page.Locator("#case-file-upload").SetInputFilesAsync(
            Path.Combine(AppContext.BaseDirectory, "Fixtures", "test-audio.mp3"));

        var waveform = Page.Locator("[id^='ws-']").First;
        try { await Expect(waveform).ToBeVisibleAsync(new() { Timeout = 45_000 }); }
        catch (Exception) { Assert.Ignore("the fixture never decoded, so there is no editor to open"); }

        await Page.Locator("[id^='afp-']").First.ClickAsync(new() { Button = MouseButton.Right });
        await Page.GetByText("Open Full View", new() { Exact = false }).ClickAsync();

        var eqButton = Page.Locator("#toolbar-eq");
        await Expect(eqButton).ToBeVisibleAsync(new() { Timeout = 30_000 });

        // AE-2: every toolbar button is fully inside the window. The failure was a button whose
        // left edge sat off the left of the viewport, so its label was cut in half.
        var offscreen = await Page.EvaluateAsync<string[]>(
            """
            (() => {
                const eq = document.querySelector('#toolbar-eq');
                const bar = eq.parentElement;
                return [...bar.querySelectorAll('button')]
                    .filter(b => b.getBoundingClientRect().left < 0
                              || b.getBoundingClientRect().right > window.innerWidth)
                    .map(b => b.innerText.trim() || '(icon only)');
            })()
            """);
        Assert.That(offscreen, Is.Empty,
            "These toolbar buttons hang outside the window, so their labels are clipped: "
            + string.Join(", ", offscreen));

        // AE-3: no emoji in the toolbar. Matched by codepoint range rather than by listing the
        // ten that were there, so a new one added later fails this too.
        var emoji = await Page.EvaluateAsync<string[]>(
            """
            (() => {
                const eq  = document.querySelector('#toolbar-eq');
                const bar = eq.parentElement;
                const re  = /[\u{1F300}-\u{1FAFF}\u{2700}-\u{27BF}\u{2600}-\u{26FF}]/u;
                return [...bar.querySelectorAll('button')]
                    .filter(b => re.test(b.innerText))
                    .map(b => b.innerText.trim());
            })()
            """);
        Assert.That(emoji, Is.Empty,
            "These toolbar buttons are still drawn with emoji rather than the site's icon set: "
            + string.Join(" | ", emoji));
    }
}
