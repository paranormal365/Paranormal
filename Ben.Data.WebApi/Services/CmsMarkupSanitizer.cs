using Ganss.Xss;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Ben.Data.WebApi.Services;

/// <summary>
/// Cleans author-written markup before it is stored.
/// </summary>
/// <remarks>
/// <para><b>Why provenance is not enough.</b> The block snippets are written by us, so the markup we
/// ship is safe. But a snippet is dropped into an editor the author then types into, and
/// <c>CustomHtml</c> sections have always accepted arbitrary HTML regardless of snippets — so what
/// actually reaches storage is <b>author markup</b>, not ours. Saving one of those as an
/// organization template means one member's markup is inserted by their colleagues and rendered in
/// their browsers and on the public site.</para>
///
/// <para>The realistic case is not an attacker. It is somebody pasting a widget they found online
/// into a page, and that markup then travelling round the group. Sanitizing on save makes the
/// guarantee <b>structural</b> instead of a matter of who typed it — which also covers the
/// <c>CustomHtml</c> path that predates all of this.</para>
///
/// <para><b>On save, not on render.</b> Rendering happens on every page view and in several places;
/// saving happens once, in few. Cleaning at the boundary means what is in the database is what will
/// be shown, so nothing downstream has to remember.</para>
///
/// <para>Allow-list, never a block-list. The set below is what the shipped snippets actually need —
/// Bootstrap's structural classes and the <c>data-bs-*</c> attributes its collapsibles and carousels
/// are wired with. Anything else is dropped rather than guessed at.</para>
/// </remarks>
public interface ICmsMarkupSanitizer
{
    /// <summary>Cleans a fragment of HTML.</summary>
    string SanitizeHtml(string? html);

    /// <summary>
    /// Cleans the markup inside a section's <c>ContentJson</c>, leaving its other fields alone.
    /// </summary>
    /// <remarks>
    /// Only the <c>html</c> property carries markup; the rest of a section's JSON is ids and flags.
    /// Unparseable JSON is returned untouched — the controller's own validation owns that, and
    /// silently rewriting something we could not read would be worse than passing it along.
    /// </remarks>
    string SanitizeContentJson(string? contentJson);
}

/// <inheritdoc />
public sealed class CmsMarkupSanitizer : ICmsMarkupSanitizer
{
    private readonly HtmlSanitizer _sanitizer;

    public CmsMarkupSanitizer()
    {
        _sanitizer = new HtmlSanitizer();

        // The structural tags the snippets and ordinary rich text use.
        foreach (var tag in new[]
                 {
                     "div", "span", "p", "br", "hr",
                     "h1", "h2", "h3", "h4", "h5", "h6",
                     "ul", "ol", "li", "dl", "dt", "dd",
                     "strong", "em", "b", "i", "u", "s", "small", "mark", "sub", "sup",
                     "blockquote", "pre", "code",
                     "a", "img", "figure", "figcaption",
                     "table", "thead", "tbody", "tfoot", "tr", "th", "td", "caption",
                     "button",
                 })
            _sanitizer.AllowedTags.Add(tag);

        // ── Ben's rule, 2026-08-17 ───────────────────────────────────────────
        // "Forms and input is not allowed on any pages of ours unless they are created by our code.
        // Any outside code has to run through us and prevent any malicious intent."
        //
        // A form on a page anybody can author is a credential-harvesting shape: it renders on our
        // domain, under an organization's name, and a reader has no way to tell it apart from ours.
        // Anything that collects from a reader must be a real feature with a real endpoint we
        // wrote — never markup somebody pasted.
        //
        // These come out of the library's defaults, which are broader than a content page needs.
        // A test asserts they never survive; it caught them being allowed in the first place.
        foreach (var tag in new[] { "form", "input", "select", "textarea", "label", "fieldset", "legend", "output" })
            _sanitizer.AllowedTags.Remove(tag);

        _sanitizer.AllowedAttributes.Add("class");
        _sanitizer.AllowedAttributes.Add("id");
        _sanitizer.AllowedAttributes.Add("role");
        _sanitizer.AllowedAttributes.Add("type");
        _sanitizer.AllowedAttributes.Add("colspan");
        _sanitizer.AllowedAttributes.Add("rowspan");

        // Bootstrap wires collapsibles and carousels through these; without them the blocks render
        // but do nothing, which is worse than visibly broken.
        foreach (var attribute in new[]
                 {
                     "data-bs-toggle", "data-bs-target", "data-bs-parent",
                     "data-bs-ride", "data-bs-slide", "data-bs-slide-to", "data-bs-dismiss",
                 })
            _sanitizer.AllowedAttributes.Add(attribute);

        // Accessibility attributes the snippets set. aria-* is allowed wholesale: every one of them
        // is descriptive, none can execute, and enumerating them would only mean dropping the ones
        // somebody legitimately needs next year.
        _sanitizer.AllowedAttributes.Add("aria-expanded");
        _sanitizer.AllowedAttributes.Add("aria-controls");
        _sanitizer.AllowedAttributes.Add("aria-hidden");
        _sanitizer.AllowedAttributes.Add("aria-label");
        _sanitizer.AllowedAttributes.Add("aria-current");

        // No inline styles. They are the usual way markup smuggles in a full-page overlay, and the
        // snippets do their layout with classes anyway.
        _sanitizer.AllowedAttributes.Remove("style");

        _sanitizer.AllowedSchemes.Clear();
        _sanitizer.AllowedSchemes.Add("http");
        _sanitizer.AllowedSchemes.Add("https");
        _sanitizer.AllowedSchemes.Add("mailto");
    }

    /// <inheritdoc />
    public string SanitizeHtml(string? html)
        => string.IsNullOrWhiteSpace(html) ? string.Empty : _sanitizer.Sanitize(html);

    /// <inheritdoc />
    public string SanitizeContentJson(string? contentJson)
    {
        if (string.IsNullOrWhiteSpace(contentJson)) return "{}";

        JsonNode? node;
        try { node = JsonNode.Parse(contentJson); }
        catch (JsonException) { return contentJson; }

        if (node is not JsonObject obj) return contentJson;

        if (obj.TryGetPropertyValue("html", out var htmlNode) && htmlNode is JsonValue value
            && value.TryGetValue<string>(out var html))
            obj["html"] = SanitizeHtml(html);

        return obj.ToJsonString();
    }
}
