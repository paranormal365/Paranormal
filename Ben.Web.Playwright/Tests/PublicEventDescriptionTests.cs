using System.Text.Json;
using Microsoft.Playwright;
using NUnit.Framework;

namespace Ben.Web.Playwright.Tests;

/// <summary>
/// An event description is authored in a rich-text editor, so it IS markup — and a visitor must
/// never be shown its source.
/// </summary>
/// <remarks>
/// Ben found this on a live page: the description read <c>&lt;p&gt;Our annual public review…</c>,
/// tags and all. The page had escaped HTML that was always meant to be rendered.
///
/// The fix could not be "stop escaping it" on its own. This endpoint is anonymous, the markup comes
/// from whoever administers the group, and descriptions had been stored raw since events were
/// built — so un-escaping alone would have turned a cosmetic bug into stored XSS on a public page.
/// It is now cleaned when saved AND when served, and this test holds both halves of the outcome:
/// the markup renders, and a script does not survive to render.
/// </remarks>
[TestFixture]
[Category("PublicEvents")]
public class PublicEventDescriptionTests : BenTestBase
{
    /// <summary>Any public event whose description actually carries markup, or null.</summary>
    /// <summary>
    /// An event whose description carries markup, or null when this database has none.
    /// </summary>
    /// <remarks>
    /// <para><b>This used to scan the wrong response.</b> It read <c>api/public/events</c> and
    /// looked for a <c>description</c> on each row — but the list projection has never carried
    /// one, so the property was always absent, the method always returned null, and both tests
    /// below always ignored themselves. They reported as skipped on every run since they were
    /// written, in a database that did have events with markup: the two rules they exist to hold
    /// were never once checked.</para>
    ///
    /// <para>The description lives on the DETAIL response, so that is what is read now — the list
    /// only says which events there are to ask about.</para>
    /// </remarks>
    private async Task<(string org, string slug)?> FindEventWithMarkupAsync()
    {
        var list = await Page.APIRequest.GetAsync("http://localhost:5252/api/public/events");
        if (!list.Ok) return null;

        foreach (var e in (await list.JsonAsync())!.Value.EnumerateArray())
        {
            var org  = e.TryGetProperty("organizationUrlName", out var o) ? o.GetString() : null;
            var slug = e.TryGetProperty("urlName", out var u) ? u.GetString() : null;
            if (org is null || slug is null) continue;

            var one = await Page.APIRequest.GetAsync(DetailUrl(org, slug));
            if (!one.Ok) continue;

            var body = (await one.JsonAsync())!.Value;
            if (!body.TryGetProperty("description", out var d) || d.ValueKind != JsonValueKind.String)
                continue;
            if (d.GetString() is not { } text || !text.Contains('<')) continue;

            return (org, slug);
        }
        return null;
    }

    /// <summary>
    /// The public detail endpoint. Written once because the second test had it wrong — it asked
    /// <c>api/public/events/{org}/{slug}</c>, which 404s, and then ignored itself over the 404.
    /// </summary>
    private static string DetailUrl(string org, string slug) =>
        $"http://localhost:5252/api/public/organizations/{org}/events/{slug}";

    [Test]
    [Description("A visitor sees the formatted description, never its tags.")]
    public async Task An_event_description_is_rendered_as_markup_not_shown_as_source()
    {
        var found = await FindEventWithMarkupAsync();
        if (found is null)
            Assert.Ignore("No public event in this database has a description containing markup, "
                        + "so there is nothing here to render either way.");

        // Anonymous on purpose: this is the seat the bug was found in, and the seat that matters.
        await Page.GotoAsync($"{BaseUrl}/o/{found.Value.org}/events/{found.Value.slug}");
        await WaitUntilLoadedAsync();

        var description = Page.Locator(".event-description");
        await Expect(description).ToBeVisibleAsync(new() { Timeout = 20_000 });

        // Wait for the CONTENT, not just the box, and poll rather than read once. The prerendered
        // page carries the description; the circuit then connects, renders the loader in its place
        // and puts it back a moment later — so a single read here saw an emptied element and
        // failed on a page that was entirely correct. It only showed up when a test that signs
        // somebody in ran first, which is why this passed alone and failed in the suite.
        //
        // Polled by hand rather than with Expect so that a description that really did render as
        // text fails with the sentence below rather than with a selector timeout.
        var parsed = description.Locator("p, div, ul, ol, strong, em, br");
        var deadline = DateTime.UtcNow.AddSeconds(20);
        int count;
        while ((count = await parsed.CountAsync()) == 0 && DateTime.UtcNow < deadline)
            await Page.WaitForTimeoutAsync(250);

        // It really is markup, not merely text that happens to lack angle brackets.
        Assert.That(count, Is.GreaterThan(0),
            "The description rendered no elements at all, so nothing was parsed. Saw:\n"
          + await description.InnerTextAsync());

        // The bug, stated exactly: a tag readable as text. Checking the rendered TEXT rather than
        // the HTML is the whole point — innerHTML contains "<p>" when everything is correct.
        var shown = await description.InnerTextAsync();
        Assert.That(shown, Does.Not.Contain("<p>").And.Not.Contain("</p>"),
            "The description is being shown as its own source. It is authored as HTML and must be "
          + $"rendered as HTML. Saw:\n{shown}");
    }

    [Test]
    [Description("Rendering the description as HTML must not render a script with it.")]
    public async Task A_script_never_survives_into_a_public_description()
    {
        var found = await FindEventWithMarkupAsync();
        if (found is null) Assert.Ignore("No public event here carries markup.");

        // Asked of the ANONYMOUS endpoint, because that is the string the page is handed. If a
        // <script> can reach this response it can reach a visitor's browser, whatever the page
        // then does with it.
        var api = await Page.APIRequest.GetAsync(DetailUrl(found.Value.org, found.Value.slug));
        Assert.That(api.Ok, Is.True,
            "The public event endpoint did not answer for a slug its own listing just gave us.");

        var served = (await api.TextAsync()).ToLowerInvariant();
        Assert.That(served, Does.Not.Contain("<script"),
            "The public event payload carries a script tag. Descriptions are sanitized on save AND "
          + "on serve precisely so this cannot happen.");
        Assert.That(served, Does.Not.Contain("onerror="),
            "The public event payload carries an inline event handler.");
    }
}
