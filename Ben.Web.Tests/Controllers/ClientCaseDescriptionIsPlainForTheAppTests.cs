using Ben.Data.Common.Text;
using Xunit;

namespace Ben.Web.Tests.Controllers;

/// <summary>
/// The description the iPhone app reads is plain text, always (2026-09-20).
/// </summary>
/// <remarks>
/// <para><b>What happened.</b> Case descriptions became a formatting editor, so
/// <c>Case.Description</c> holds HTML. The shipped app draws it with SwiftUI's <c>Text</c>, which
/// renders a string exactly as given — so every client on the app read
/// <c>&lt;p&gt;Things going bump in the night&lt;/p&gt;</c>. Ben found it on a real case.</para>
///
/// <para><b>Why the fix is on this side.</b> The app cannot change until its next build. This is
/// the same trade <c>CaseMessageBodies</c> already made for messages: the field the app reads
/// stays plain, and the formatted copy is added beside it under a name the app has never heard of
/// and therefore ignores.</para>
/// </remarks>
public sealed class ClientCaseDescriptionIsPlainForTheAppTests
{
    [Fact]
    public void A_paragraph_loses_its_tags_and_keeps_its_words()
        => Assert.Equal("Things going bump in the night",
            PlainTextHtml.ToText("<p>Things going bump in the night</p>"));

    [Fact]
    public void Several_paragraphs_stay_apart()
        => Assert.Equal("One thing.\n\nThen another.",
            PlainTextHtml.ToText("<p>One thing.</p><p>Then another.</p>"));

    [Fact]
    public void Formatting_inside_a_sentence_goes_without_eating_the_words()
        => Assert.Equal("It was very loud.",
            PlainTextHtml.ToText("<p>It was <strong>very</strong> loud.</p>"));

    [Fact]
    public void An_entity_is_decoded_rather_than_shown()
        => Assert.Equal("Tom & Jerry's",
            PlainTextHtml.ToText("<p>Tom &amp; Jerry&#39;s</p>"));

    /// <summary>A description written before the editor existed is untouched.</summary>
    [Fact]
    public void Plain_text_that_was_never_html_survives_exactly()
        => Assert.Equal("Just words, no tags.",
            PlainTextHtml.ToText("Just words, no tags."));

    [Fact]
    public void Nothing_stays_nothing()
    {
        Assert.Equal(string.Empty, PlainTextHtml.ToText(null));
        Assert.Equal(string.Empty, PlainTextHtml.ToText(""));
    }

    /// <summary>
    /// The rule, stated where somebody changing the record will read it.
    /// </summary>
    /// <remarks>
    /// A source check rather than a behaviour one, because the mistake it guards is a one-word
    /// edit: passing <c>c.Description</c> straight through again would compile, pass every other
    /// test, and put the tags back in front of every client on the app.
    /// </remarks>
    [Fact]
    public void The_controller_converts_rather_than_passing_the_html_through()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Ben.slnx"))) dir = dir.Parent;
        Assert.NotNull(dir);

        var source = File.ReadAllText(Path.Combine(
            dir!.FullName, "Ben.Data.WebApi", "Controllers", "MyCaseController.cs"));

        Assert.Contains("Description:             PlainTextHtml.ToText(c.Description)", source);
        Assert.Contains("DescriptionHtml:         c.Description", source);
    }
}
