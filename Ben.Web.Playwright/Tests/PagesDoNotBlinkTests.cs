using System.Text.RegularExpressions;
using Microsoft.Playwright;
using NUnit.Framework;

namespace Ben.Web.Playwright.Tests;

/// <summary>
/// A page the server has drawn must not turn back into a spinner when its live connection starts.
/// </summary>
/// <remarks>
/// <para><b>The fault.</b> Blazor Server draws a page twice: once on the server, which is what the
/// visitor sees first, and again when the circuit connects — as a brand-new component with none
/// of the first one's data. A page that loads in <c>OnInitializedAsync</c> therefore showed its
/// content, then its "Loading…" spinner, then its content again: a visible blink on every visit,
/// and the reason several browser tests counted things in the gap. The storefront branch fixed its
/// own pages by carrying the server's data into the live page ([PersistentState]); this covers
/// the rest of the site's visitor-facing pages.</para>
///
/// <para><b>How a blink is seen.</b> The server's copy of <c>main</c> is inspected when the document
/// finishes parsing. If it held no spinner — the page arrived drawn — then any spinner appearing
/// in <c>main</c> afterwards is the page rebuilding itself. A page whose server copy is itself a
/// spinner (content that waits for sign-in, say) is not a blink and is not flagged.</para>
///
/// <para><b>And the page must be alive.</b> A page whose connection the server refused never
/// blinks either — the store's first version of this test passed against exactly that. So each
/// case also fails on a refused connection and proves the circuit answers, by typing into the
/// sidebar's menu filter, which only the live page can act on.</para>
/// </remarks>
[TestFixture]
public class PagesDoNotBlinkTests : BenTestBase
{
    private const string Watcher = """
        window.__blink = { ssrSpinner: null, blinked: false, where: null };
        const spinnerIn = root => root && root.querySelector('.spinner-border');
        document.addEventListener('DOMContentLoaded', () => {
            window.__blink.ssrSpinner = !!spinnerIn(document.querySelector('main'));
            new MutationObserver(() => {
                if (window.__blink.ssrSpinner || window.__blink.blinked) return;
                const s = spinnerIn(document.querySelector('main'));
                if (s) {
                    window.__blink.blinked = true;
                    const box = s.closest('[role=status]');
                    window.__blink.where = (box && box.innerText || '').trim().slice(0, 80);
                }
            }).observe(document, { childList: true, subtree: true });
        });
        """;

    private async Task CheckAsync(string path)
    {
        var dropped = new List<string>();
        Page.Console += (_, m) => { if (m.Type == "error" && m.Text.Contains("Connection closed with an error")) dropped.Add(m.Text); };
        await Page.AddInitScriptAsync(Watcher);

        await Page.GotoAsync($"{BaseUrl}{path}");
        await WaitForTheCircuitAsync();
        await Page.WaitForTimeoutAsync(2500);   // the live page's first render and whatever it fetches

        Assert.That(dropped, Is.Empty, $"{path}: the server closed the live connection — the page is drawn but dead.");

        // Alive: the sidebar's filter is a circuit round trip.
        var home = Page.Locator("nav a[href='/']").First;
        await Expect(home).ToBeVisibleAsync(new() { Timeout = 10_000 });
        await FillAndConfirmAsync("#searchInput", "zzqq-no-such-menu-item");
        await Expect(home).ToBeHiddenAsync(new() { Timeout = 10_000 });

        var ssrSpinner = await Page.EvaluateAsync<bool?>("window.__blink.ssrSpinner");
        var blinked = await Page.EvaluateAsync<bool>("window.__blink.blinked");
        var where = await Page.EvaluateAsync<string?>("window.__blink.where");
        TestContext.Out.WriteLine($"{path}: server copy had a spinner = {ssrSpinner}; blinked = {blinked} ({where})");
        Assert.That(blinked, Is.False,
            $"{path} arrived drawn, then showed a spinner (\"{where}\") when its live connection started — it rebuilt itself from nothing.");
    }

    [TestCase("/")]
    [TestCase("/events")]
    [TestCase("/publications")]
    [TestCase("/find")]
    [TestCase("/equipment-catalog")]
    [TestCase("/pricing")]
    [TestCase("/o/paranormal365")]
    [TestCase("/o/paranormal365/cases")]
    [TestCase("/places/40000001-0000-0000-0000-000000000001")]   // the seeded public place the product walk visits
    public Task The_page_does_not_blink_when_it_comes_alive(string path) => CheckAsync(path);

    /// <summary>
    /// What a page carries into its live copy is sent back to the server in the connection's first
    /// message, and SignalR refuses a message over its limit (32 KB, Ben.Web.Website/Program.cs
    /// keeps the default) by hanging up — the page is then drawn but dead. Each page keeps under
    /// half of it, so a group with a long history is caught here, not by a visitor.
    /// </summary>
    [TestCase("/events")]
    [TestCase("/find")]
    [TestCase("/pricing")]
    [TestCase("/o/paranormal365")]
    [TestCase("/o/paranormal365/cases")]
    public async Task The_carried_state_fits_the_connection(string path)
    {
        var html = await (await Page.APIRequest.GetAsync($"{BaseUrl}{path}")).TextAsync();
        var state = Regex.Match(html, "<!--Blazor-Server-Component-State:(.*?)-->", RegexOptions.Singleline);
        var bytes = state.Success ? state.Groups[1].Value.Length : 0;
        TestContext.Out.WriteLine($"{path}: {bytes:N0} bytes carried");
        Assert.That(bytes, Is.LessThan(16 * 1024), $"{path} carries {bytes:N0} bytes into its live page; the connection takes 32 KB at most.");
    }
}
