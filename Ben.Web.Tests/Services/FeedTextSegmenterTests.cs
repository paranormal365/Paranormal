using Ben.Data.Common.Helpers;
using Xunit;

namespace Ben.Web.Tests.Services;

/// <summary>
/// Splitting a post into the runs a renderer draws.
/// </summary>
/// <remarks>
/// <para>Different from <c>FeedTextParserTests</c>, which covers "which names and tags does this
/// post contain". This covers <b>where</b> they are, and the difference is not academic: the parser
/// returns each token once, so a post naming somebody twice yields one token — and a renderer
/// driven by that list would draw the first mention as a link and the second as plain text.</para>
///
/// <para>The case worth the most here is the one that would be a security-adjacent embarrassment
/// rather than a cosmetic bug: an email address in a post must not become a link to whoever is
/// called "example".</para>
/// </remarks>
public sealed class FeedTextSegmenterTests
{
    private static string Rebuilt(string body)
        => string.Concat(FeedTextSegmenter.Segment(body).Select(s => s.Text));

    // ── The property everything rests on ──────────────────────────────────────

    [Theory]
    [InlineData("plain text with nothing in it")]
    [InlineData("@sarahmitchell")]
    [InlineData("hi @sarahmitchell and @jamesthornton")]
    [InlineData("#evp #orbs at the #bellwitch")]
    [InlineData("write to ben@example.com about #evp")]
    [InlineData("@a")]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("@@@")]
    [InlineData("### room # 3")]
    [InlineData("email@one.com @two #three")]
    public void The_segments_always_rebuild_the_original_exactly(string body)
    {
        // Nothing may be dropped, duplicated or reordered. A renderer that silently loses a
        // character of somebody's post is worse than one that fails to linkify.
        Assert.Equal(body, Rebuilt(body));
    }

    // ── Mentions ──────────────────────────────────────────────────────────────

    [Fact]
    public void A_mention_becomes_its_own_segment_carrying_the_handle()
    {
        var segments = FeedTextSegmenter.Segment("thanks @SarahMitchell for the tip");

        var mention = Assert.Single(segments, s => s.Kind == FeedSegmentKind.Mention);
        Assert.Equal("@SarahMitchell", mention.Text);      // as typed
        Assert.Equal("sarahmitchell", mention.Value);      // normalised, for lookup
    }

    /// <summary>
    /// An email address is not a mention.
    /// </summary>
    /// <remarks>
    /// The one that would actually have happened, and the reason the segmenter hands the parser the
    /// character before the marker: slicing the body at the '@' puts it at the start of a string,
    /// where it always looks like the start of a word.
    /// </remarks>
    [Fact]
    public void An_email_address_is_left_as_plain_text()
    {
        var segments = FeedTextSegmenter.Segment("write to ben@example.com about it");

        Assert.DoesNotContain(segments, s => s.Kind == FeedSegmentKind.Mention);
        Assert.Equal("write to ben@example.com about it", Rebuilt("write to ben@example.com about it"));
    }

    [Fact]
    public void The_same_name_twice_is_two_segments()
    {
        // The parser would report one token. A renderer driven by the parser alone would linkify
        // the first and leave the second as text.
        var mentions = FeedTextSegmenter.Segment("@sarah and again @sarah")
            .Where(s => s.Kind == FeedSegmentKind.Mention)
            .ToList();

        Assert.Equal(2, mentions.Count);
        Assert.All(mentions, m => Assert.Equal("sarah", m.Value));
    }

    [Fact]
    public void A_mention_at_the_very_start_is_found()
    {
        var first = FeedTextSegmenter.Segment("@sarahmitchell said so")[0];

        Assert.Equal(FeedSegmentKind.Mention, first.Kind);
        Assert.Equal("sarahmitchell", first.Value);
    }

    [Fact]
    public void Trailing_punctuation_stays_outside_the_mention()
    {
        var segments = FeedTextSegmenter.Segment("ask @sarah.");

        var mention = Assert.Single(segments, s => s.Kind == FeedSegmentKind.Mention);
        Assert.Equal("@sarah", mention.Text);
        Assert.Equal("ask @sarah.", Rebuilt("ask @sarah."));
    }

    // ── Hashtags ──────────────────────────────────────────────────────────────

    [Fact]
    public void A_tag_carries_its_lower_cased_value_and_the_text_as_typed()
    {
        var tag = Assert.Single(
            FeedTextSegmenter.Segment("a clear #EVP"), s => s.Kind == FeedSegmentKind.Hashtag);

        Assert.Equal("#EVP", tag.Text);
        Assert.Equal("evp", tag.Value);
    }

    [Fact]
    public void Several_tags_come_out_in_order()
    {
        var values = FeedTextSegmenter.Segment("at the #bellwitch cave — #evp and #orbs")
            .Where(s => s.Kind == FeedSegmentKind.Hashtag)
            .Select(s => s.Value)
            .ToList();

        Assert.Equal(["bellwitch", "evp", "orbs"], values);
    }

    [Fact]
    public void A_bare_hash_or_at_is_plain_text()
    {
        Assert.DoesNotContain(FeedTextSegmenter.Segment("room # 3"), s => s.Kind == FeedSegmentKind.Hashtag);
        Assert.DoesNotContain(FeedTextSegmenter.Segment("meet @ the bridge"), s => s.Kind == FeedSegmentKind.Mention);
    }

    // ── Both together ─────────────────────────────────────────────────────────

    [Fact]
    public void A_post_with_both_splits_into_the_right_runs_in_the_right_order()
    {
        const string body = "great work @sarahmitchell — the #evp was the clearest yet";

        var kinds = FeedTextSegmenter.Segment(body).Select(s => s.Kind).ToList();

        Assert.Equal(
            [FeedSegmentKind.Text, FeedSegmentKind.Mention, FeedSegmentKind.Text,
             FeedSegmentKind.Hashtag, FeedSegmentKind.Text],
            kinds);
    }

    [Fact]
    public void Adjacent_tokens_do_not_swallow_each_other()
    {
        var kinds = FeedTextSegmenter.Segment("@sarah #evp").Select(s => s.Kind).ToList();

        Assert.Equal([FeedSegmentKind.Mention, FeedSegmentKind.Text, FeedSegmentKind.Hashtag], kinds);
    }

    [Fact]
    public void An_empty_body_produces_nothing()
    {
        Assert.Empty(FeedTextSegmenter.Segment(""));
        Assert.Empty(FeedTextSegmenter.Segment(null));
    }
}

/// <summary>
/// Web addresses in a body (item 233, Ben 2026-09-11).
/// </summary>
/// <remarks>
/// The interesting cases are all about where a link STOPS. A body is a sentence, and a sentence
/// puts punctuation after a link that is not part of it — which is exactly the kind of edge the
/// segmenter was moved out of a Razor file to make reachable.
/// </remarks>
public sealed class FeedTextSegmenterUrlTests
{
    private static IReadOnlyList<FeedSegment> Urls(string body) =>
        [.. FeedTextSegmenter.Segment(body).Where(s => s.Kind == FeedSegmentKind.Url)];

    [Theory]
    [InlineData("look at https://example.com/a", "https://example.com/a")]
    [InlineData("http://example.com works too", "http://example.com")]
    [InlineData("HTTPS://EXAMPLE.COM shouting", "HTTPS://EXAMPLE.COM")]
    public void An_address_is_found(string body, string expected) =>
        Assert.Equal(expected, Assert.Single(Urls(body)).Value);

    [Theory]
    // The sentence's own punctuation is not part of the address.
    [InlineData("see https://example.com/a.", "https://example.com/a")]
    [InlineData("see https://example.com/a, then go", "https://example.com/a")]
    [InlineData("see https://example.com/a!?", "https://example.com/a")]
    [InlineData("\"https://example.com/a\"", "https://example.com/a")]
    [InlineData("(https://example.com/a)", "https://example.com/a")]
    public void Sentence_punctuation_is_left_out(string body, string expected) =>
        Assert.Equal(expected, Assert.Single(Urls(body)).Value);

    [Fact]
    public void A_closing_bracket_that_belongs_to_the_address_is_kept()
    {
        // The case this rule exists for: people paste these.
        const string body = "https://en.wikipedia.org/wiki/Nosferatu_(1922_film)";
        Assert.Equal(body, Assert.Single(Urls(body)).Value);
    }

    [Theory]
    [InlineData("nothing here at all")]
    [InlineData("example.com without a scheme")]          // guessing turns full stops into links
    [InlineData("mailto:somebody@example.com")]
    [InlineData("https://")]                               // a scheme and nothing after it
    [InlineData("ahttps://example.com")]                   // not at a word boundary
    public void Everything_else_is_plain_text(string body) => Assert.Empty(Urls(body));

    [Fact]
    public void An_address_does_not_swallow_what_follows_it()
    {
        var segments = FeedTextSegmenter.Segment("go to https://example.com/a and tell @sarah");

        Assert.Equal("https://example.com/a", segments.Single(s => s.Kind == FeedSegmentKind.Url).Value);
        // The mention after it still resolves — a URL that ran to the end of the body would have
        // eaten it, which is the failure this asserts against.
        Assert.Contains(segments, s => s.Kind == FeedSegmentKind.Mention && s.Value == "sarah");
    }

    [Fact]
    public void The_runs_still_rebuild_the_original_body()
    {
        // The property that keeps rendering honest: whatever the split decides, putting the runs
        // back together must give exactly what the author typed.
        const string body = "read (https://example.com/a), then ask @sarah about #ghosts.";
        Assert.Equal(body, string.Concat(FeedTextSegmenter.Segment(body).Select(s => s.Text)));
    }
}
