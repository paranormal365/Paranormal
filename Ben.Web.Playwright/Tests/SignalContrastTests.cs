using Microsoft.Playwright;
using NUnit.Framework;

namespace Ben.Web.Playwright.Tests;

/// <summary>
/// Every semantic colour the Signal skin derives must stay readable, in both themes.
/// </summary>
/// <remarks>
/// <para><b>Why this exists.</b> Under Signal the alert and subtle colours are not listed, they are
/// DERIVED — each a <c>color-mix()</c> of the semantic colour into the page's surface or ink (see
/// "Semantic tints" in <c>themes/signal.css</c>). That is what makes them follow a palette change,
/// and it is also what lets a palette change break them without anybody noticing. Two did, while
/// this was being built, and both were found only by measuring:</para>
/// <list type="bullet">
/// <item><description>light-mode warning text at <b>2.78:1</b> — yellow cannot carry most of itself
/// into the ink and stay legible on a pale tint (129 warning alerts on the site);</description></item>
/// <item><description>light-mode success text at <b>3.33:1</b> — retiring the Night skin had
/// silently dropped the site's own success colour to the template's light turquoise.</description></item>
/// </list>
/// <para><b>Measured, not read off the stylesheet.</b> The colours are resolved by the browser and
/// the ratio computed from what it actually paints, because a mix is only knowable once the cascade
/// has run — the second fault above was invisible in the CSS, which was correct; the variable it
/// read had changed underneath it.</para>
/// <para><b>One trap for whoever edits this.</b> A <c>color-mix()</c> result serialises as
/// <c>color(srgb 0.94 0.93 0.99)</c>, channels from 0 to 1, not <c>rgb(…)</c>. Parsed as 0–255 it
/// reads as black on black and every ratio comes out 1.00 — which is how the first measurement of
/// this went.</para>
/// <para>Uses the Development style guide because it needs no sign-in and renders on every e2e
/// stack. Elements are created in the page rather than found on it, so the check covers every
/// variant whether or not some screen happens to show one today.</para>
/// </remarks>
[TestFixture]
[Category("Signal")]
public class SignalContrastTests : BenTestBase
{
    private const double Minimum = 4.5;   // WCAG 2.2 AA, normal text

    [Test]
    public async Task Every_alert_and_coloured_word_is_readable_in_both_themes()
    {
        await Page.GotoAsync($"{BaseUrl}/styleguide");
        // The circuit first: the page is prerendered, then the circuit connects and REPLACES the
        // nodes, and getComputedStyle on a node that has just been swapped out returns empty
        // strings. Measured too early, that read as a crash in the colour parser on a fresh load.
        await WaitForTheCircuitAsync();
        await Expect(Page.Locator(".app-content")).ToBeVisibleAsync(new() { Timeout = 20_000 });

        // Returned as a JSON string and parsed here: Playwright for .NET handed a plain JS object
        // back as an EMPTY Dictionary<string, double>, which read as "nothing was measured".
        // Measured up to three times: on a freshly started stack the circuit can still swap the
        // content area out from under the measurement after WaitForTheCircuitAsync, and a swapped
        // node reads as an empty colour — a timing fault, not a contrast one.
        string json = "";
        for (var attempt = 1; ; attempt++)
        {
            try { json = await MeasureAsync(); break; }
            catch (PlaywrightException ex) when (attempt < 3 && (ex.Message.Contains("unreadable colour") || ex.Message.Contains("unreadable color")))
            {
                await Page.WaitForTimeoutAsync(1_000);
            }
        }
        var results = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, double>>(json)
                      ?? new Dictionary<string, double>();

        Assert.That(results, Is.Not.Empty, "Nothing was measured.");
        var failures = results.Where(r => r.Value < Minimum)
                              .Select(r => $"{r.Key}: {r.Value:0.00}:1")
                              .ToList();
        Assert.That(failures, Is.Empty,
            $"Below {Minimum}:1 — a token change in themes/signal.css has made these unreadable:\n  "
            + string.Join("\n  ", failures));
    }

    private Task<string> MeasureAsync() => Page.EvaluateAsync<string>(
            """
            async () => {
                const rgb = s => {
                    const m = s && s.match(/-?[\d.]+/g);
                    if (!m) throw new Error('unreadable colour: "' + s + '"');
                    const n = m.map(Number);
                    return s.startsWith('color(') ? n.slice(0, 3).map(v => v * 255) : n.slice(0, 3);
                };
                const lum = ([r, g, b]) => {
                    const f = v => { v /= 255; return v <= 0.03928 ? v / 12.92 : ((v + 0.055) / 1.055) ** 2.4; };
                    return 0.2126 * f(r) + 0.7152 * f(g) + 0.0722 * f(b);
                };
                const ratio = (a, b) => {
                    const x = lum(a), y = lum(b);
                    return (Math.max(x, y) + 0.05) / (Math.min(x, y) + 0.05);
                };
                const host = document.querySelector('.app-content');
                const out = {};
                for (const mode of ['light', 'dark']) {
                    document.documentElement.setAttribute('data-bs-theme', mode);
                    await new Promise(r => setTimeout(r, 120));
                    const page = rgb(getComputedStyle(host).backgroundColor);
                    for (const v of ['primary', 'info', 'success', 'warning', 'danger', 'secondary', 'light', 'dark']) {
                        const d = document.createElement('div');
                        d.className = 'alert alert-' + v; d.textContent = 'x'; host.appendChild(d);
                        const cs = getComputedStyle(d);
                        out[`${mode} alert-${v}`] = ratio(rgb(cs.color), rgb(cs.backgroundColor));
                        d.remove();
                    }
                    // Links and the small accent words (kickers), on the page and on a card. The
                    // accent itself was 3.9:1 on a dark card — fine for a fill, short for words —
                    // so words take --ben-link (2026-10-01).
                    {
                        const card = document.createElement('div');
                        card.className = 'card'; host.appendChild(card);
                        const a = document.createElement('a');
                        a.href = '#'; a.textContent = 'x'; card.appendChild(a);
                        out[`${mode} link on a card`] = ratio(rgb(getComputedStyle(a).color), rgb(getComputedStyle(card).backgroundColor));
                        card.remove();
                        const a2 = document.createElement('a');
                        a2.href = '#'; a2.textContent = 'x'; host.appendChild(a2);
                        out[`${mode} link on the page`] = ratio(rgb(getComputedStyle(a2).color), page);
                        a2.remove();
                        const k = document.createElement('div');
                        k.className = 'ben-kicker'; k.textContent = 'x'; host.appendChild(k);
                        out[`${mode} kicker on the page`] = ratio(rgb(getComputedStyle(k).color), page);
                        k.remove();
                    }
                    for (const v of ['success', 'danger', 'warning']) {
                        const t = document.createElement('span');
                        t.className = 'text-' + v; t.textContent = 'x'; host.appendChild(t);
                        out[`${mode} text-${v} on the page`] = ratio(rgb(getComputedStyle(t).color), page);
                        t.remove();
                    }
                }
                return JSON.stringify(out);
            }
            """);

    [Test]
    public async Task A_box_you_write_paragraphs_in_has_the_card_corner_not_the_pill()
    {
        // Ben, 2026-10-02: in a report's Executive Summary the caret sat on the pill's top-left
        // curve. One-line fields keep the pill; multi-line boxes, Bootstrap's and Telerik's, take
        // the card's 12px corner.
        await Page.GotoAsync($"{BaseUrl}/help");
        var radii = await Page.EvaluateAsync<string[]>(@"() => {
            const host = document.createElement('div');
            host.innerHTML = '<input class=""form-control"" id=""r1""><textarea class=""form-control"" id=""r2"" rows=""4""></textarea>'
                           + '<span class=""k-input k-textarea"" id=""r3""><textarea class=""k-input-inner""></textarea></span>';
            document.body.appendChild(host);
            return ['r1','r2','r3'].map(id => getComputedStyle(document.getElementById(id)).borderTopLeftRadius);
        }");
        Assert.That(radii[0], Is.EqualTo("999px"), "a one-line field should still be a pill");
        Assert.That(radii[1], Is.EqualTo("12px"), "a Bootstrap textarea should have the card corner");
        Assert.That(radii[2], Is.EqualTo("12px"), "a Telerik textarea should have the card corner");
    }
}
