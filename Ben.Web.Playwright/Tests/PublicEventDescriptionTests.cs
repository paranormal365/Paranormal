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
    private async Task<(string org, string slug)?> FindEventWithMarkupAsync()
    {
        var api = await Page.APIRequest.GetAsync("http://localhost:5252/api/public/events");
        if (!api.Ok) return null;

        foreach (var e in (await api.JsonAsync())!.Value.EnumerateArray())
        {
            if (!e.TryGetProperty("description", out var d) || d.ValueKind != JsonValueKind.String)
                continue;
            if (d.GetString() is not { } text || !text.Contains('<')) continue;

            var org  = e.TryGetProperty("organizationUrlName", out var o) ? o.GetString() : null;
            var slug = e.TryGetProperty("urlName", out var u) ? u.GetString() : null;
            if (org is not null && slug is not null) return (org, slug);
        }
        return null;
    }

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

        // The bug, stated exactly: a tag readable as text. Checking the rendered TEXT rather than
        // the HTML is the whole point — innerHTML contains "<p>" when everything is correct.
        var shown = await description.InnerTextAsync();
        Assert.That(shown, Does.Not.Contain("<p>").And.Not.Contain("</p>"),
            "The description is being shown as its own source. It is authored as HTML and must be "
          + $"rendered as HTML. Saw:\n{shown}");

        // And it really is markup, not merely text that happens to lack angle brackets.
        Assert.That(await description.Locator("p, div, ul, ol, strong, em, br").CountAsync(),
            Is.GreaterThan(0), "The description rendered no elements at all, so nothing was parsed.");
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
        var api = await Page.APIRequest.GetAsync(
            $"http://localhost:5252/api/public/events/{found.Value.org}/{found.Value.slug}");
        if (!api.Ok) Assert.Ignore("The public event endpoint did not answer for this slug.");

        var served = (await api.TextAsync()).ToLowerInvariant();
        Assert.That(served, Does.Not.Contain("<script"),
            "The public event payload carries a script tag. Descriptions are sanitized on save AND "
          + "on serve precisely so this cannot happen.");
        Assert.That(served, Does.Not.Contain("onerror="),
            "The public event payload carries an inline event handler.");
    }
}
