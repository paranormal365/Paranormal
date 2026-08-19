using Ben.Data.WebApi.Services;
using Ben.Web.Website.Library.Organization.Cms;
using Xunit;

namespace Ben.Web.Tests.Services;

/// <summary>
/// Cleaning author-written page markup before it is stored (backlog item #80, part 2).
/// </summary>
/// <remarks>
/// <para>The blocks we ship are safe because we wrote them. That is not the property that matters:
/// a block is dropped into an editor the author then types into, and <c>CustomHtml</c> sections have
/// always taken arbitrary markup regardless. What reaches storage is <b>author markup</b>, and once
/// it can be saved as a group template it is inserted by colleagues and rendered in their browsers.
/// The realistic case is somebody pasting a widget they found online, not an attacker.</para>
///
/// <para>So the tests come in pairs: the dangerous thing is removed, <b>and</b> the thing the blocks
/// genuinely need survives. A sanitizer that strips <c>data-bs-toggle</c> leaves every collapsible
/// rendering but inert, which is worse than visibly broken because nobody reports it.</para>
/// </remarks>
public sealed class CmsMarkupSanitizerTests
{
    private static readonly CmsMarkupSanitizer Sanitizer = new();

    // ── What must not survive ────────────────────────────────────────────────

    [Theory]
    [InlineData("""<p>Hello</p><script>alert(1)</script>""", "script")]
    [InlineData("""<img src="x" onerror="alert(1)">""", "onerror")]
    [InlineData("""<a href="javascript:alert(1)">Click</a>""", "javascript:")]
    [InlineData("""<div style="position:fixed;inset:0">Overlay</div>""", "position:fixed")]
    [InlineData("""<iframe src="https://example.com"></iframe>""", "iframe")]
    [InlineData("""<form action="https://evil.example"><input name="p"></form>""", "evil.example")]
    public void The_dangerous_part_is_removed(string markup, string mustNotSurvive)
    {
        var cleaned = Sanitizer.SanitizeHtml(markup);
        Assert.DoesNotContain(mustNotSurvive, cleaned, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// No page anybody can author may contain a form or an input, in any shape.
    /// </summary>
    /// <remarks>
    /// Ben's rule: forms and inputs exist on our pages only where our own code put them. A form
    /// authored into a CMS page renders on our domain under an organization's name, and a reader
    /// has no way to tell it from a real one — which makes it a credential-harvesting shape whether
    /// or not that was the intent. Anything collecting from a reader has to be a feature with an
    /// endpoint we wrote.
    /// </remarks>
    [Theory]
    [InlineData("""<form action="https://evil.example" method="post"><input name="password" type="password"><button>Sign in</button></form>""")]
    [InlineData("""<input type="text" name="card">""")]
    [InlineData("""<select name="x"><option>1</option></select>""")]
    [InlineData("""<textarea name="notes"></textarea>""")]
    [InlineData("""<fieldset><legend>Details</legend><label>Name</label></fieldset>""")]
    public void No_authored_page_may_carry_a_form_or_an_input(string markup)
    {
        var cleaned = Sanitizer.SanitizeHtml(markup);

        foreach (var tag in new[] { "<form", "<input", "<select", "<textarea", "<fieldset", "<legend", "<label" })
            Assert.DoesNotContain(tag, cleaned, StringComparison.OrdinalIgnoreCase);

        // And nothing that would post somewhere either.
        Assert.DoesNotContain("action=", cleaned, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("evil.example", cleaned, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Ordinary_text_around_a_removed_script_is_kept()
    {
        var cleaned = Sanitizer.SanitizeHtml("""<p>Keep me</p><script>alert(1)</script><p>And me</p>""");

        Assert.Contains("Keep me", cleaned);
        Assert.Contains("And me", cleaned);
        Assert.DoesNotContain("alert", cleaned);
    }

    // ── What must survive ────────────────────────────────────────────────────

    /// <summary>
    /// Every block we ship must come through untouched. A sanitizer that quietly broke the palette
    /// would be discovered by an author, not by a test, and only after they had built a page.
    /// </summary>
    [Fact]
    public void Every_shipped_block_survives_intact()
    {
        foreach (var snippet in CmsSnippets.All)
        {
            var markup  = CmsSnippets.Render(snippet);
            var cleaned = Sanitizer.SanitizeHtml(markup);

            // The wiring attributes, without which a collapsible or carousel renders but does
            // nothing at all.
            foreach (var attribute in new[] { "data-bs-toggle", "data-bs-target", "data-bs-parent", "data-bs-ride", "data-bs-slide" })
                if (markup.Contains(attribute, StringComparison.Ordinal))
                    Assert.True(cleaned.Contains(attribute, StringComparison.Ordinal),
                        $"'{snippet.Name}' lost {attribute}, so it will render and do nothing.");

            // The ids they are wired by.
            if (markup.Contains(" id=", StringComparison.Ordinal))
                Assert.Contains(" id=", cleaned, StringComparison.Ordinal);

            // And the classes that give it its shape.
            Assert.Contains("class=", cleaned, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void Ordinary_rich_text_is_left_alone()
    {
        const string markup = """<h3>Heading</h3><p><strong>Bold</strong> and <em>italic</em>, plus a <a href="https://example.com">link</a>.</p><ul><li>One</li><li>Two</li></ul>""";
        var cleaned = Sanitizer.SanitizeHtml(markup);

        foreach (var expected in new[] { "<h3", "<strong", "<em", "<ul", "<li", "https://example.com" })
            Assert.Contains(expected, cleaned, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void A_mailto_link_survives_but_an_unknown_scheme_does_not()
    {
        Assert.Contains("mailto:", Sanitizer.SanitizeHtml("""<a href="mailto:a@b.com">Mail</a>"""));
        Assert.DoesNotContain("data:", Sanitizer.SanitizeHtml("""<a href="data:text/html,<script>alert(1)</script>">x</a>"""));
    }

    // ── The JSON wrapper ─────────────────────────────────────────────────────

    [Fact]
    public void Only_the_markup_inside_a_sections_json_is_touched()
    {
        var cleaned = Sanitizer.SanitizeContentJson(
            """{"html":"<p>Hi</p><script>alert(1)</script>","showTitle":true,"sortOrder":3}""");

        Assert.DoesNotContain("script", cleaned, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Hi", cleaned);
        // The fields that are not markup are left exactly as they were.
        Assert.Contains("showTitle", cleaned);
        Assert.Contains("sortOrder", cleaned);
    }

    [Fact]
    public void Section_json_without_markup_is_unchanged_in_substance()
    {
        var cleaned = Sanitizer.SanitizeContentJson("""{"uploadFileIds":["a","b"]}""");
        Assert.Contains("uploadFileIds", cleaned);
    }

    /// <summary>
    /// Content that cannot be parsed is passed along rather than silently rewritten. Validation of
    /// the JSON itself belongs to the controller, and quietly replacing something we could not read
    /// would destroy an author's work to no benefit.
    /// </summary>
    [Fact]
    public void Unreadable_content_is_passed_through_rather_than_mangled()
    {
        const string broken = "{not json at all";
        Assert.Equal(broken, Sanitizer.SanitizeContentJson(broken));
    }

    [Fact]
    public void Nothing_becomes_an_empty_object()
    {
        Assert.Equal("{}", Sanitizer.SanitizeContentJson(null));
        Assert.Equal("{}", Sanitizer.SanitizeContentJson("   "));
    }
}
