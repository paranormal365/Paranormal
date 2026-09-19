using Ben.Data.Common.Helpers;
using Xunit;

namespace Ben.Web.Tests.Services;

/// <summary>
/// What counts as a mention and what counts as a tag.
/// </summary>
/// <remarks>
/// Two sides depend on this agreeing with itself: the WebApi fills the mention and hashtag tables
/// from it when a post is written, and the website turns the same text into links when the post is
/// read. A disagreement between them shows up as a post whose visible links do not match the
/// notifications it sent, which is why the parser is one function in Common rather than two
/// implementations that look similar.
/// </remarks>
public sealed class FeedTextParserTests
{
    // ── Mentions ──────────────────────────────────────────────────────────────

    [Fact]
    public void A_mention_is_found_and_returned_without_its_at_sign()
    {
        Assert.Equal(["sarahmitchell"], FeedTextParser.FindMentions("thanks @sarahmitchell for the tip"));
    }

    /// <summary>
    /// An email address in a post is not a mention.
    /// </summary>
    /// <remarks>
    /// The one that would actually have happened. Without the look-behind, "ben@example.com" in a
    /// post mentions whoever is called "example" — a notification to a stranger, caused by
    /// somebody quoting an address.
    /// </remarks>
    [Fact]
    public void An_email_address_is_not_a_mention()
    {
        Assert.Empty(FeedTextParser.FindMentions("write to ben@example.com about it"));
    }

    [Fact]
    public void A_mention_must_start_a_word()
    {
        Assert.Empty(FeedTextParser.FindMentions("some.thing@nested"));
        Assert.Empty(FeedTextParser.FindMentions("aa@bb"));
    }

    [Fact]
    public void Trailing_punctuation_is_not_part_of_the_name()
    {
        Assert.Equal(["sarah"], FeedTextParser.FindMentions("ask @sarah."));
        Assert.Equal(["sarah"], FeedTextParser.FindMentions("ask @sarah, please"));
    }

    [Fact]
    public void Dots_and_hyphens_inside_a_name_are_kept()
    {
        Assert.Equal(["sarah.mitchell"], FeedTextParser.FindMentions("@sarah.mitchell said"));
        Assert.Equal(["mary-jane"], FeedTextParser.FindMentions("@mary-jane said"));
    }

    [Fact]
    public void The_same_name_twice_is_one_mention()
    {
        // Otherwise naming somebody twice in one post notifies them twice.
        Assert.Equal(["sarah"], FeedTextParser.FindMentions("@sarah and also @Sarah"));
    }

    [Fact]
    public void A_bare_at_sign_is_not_a_mention()
    {
        Assert.Empty(FeedTextParser.FindMentions("meet @ the bridge"));
        Assert.Empty(FeedTextParser.FindMentions("@"));
    }

    // ── Hashtags ──────────────────────────────────────────────────────────────

    [Fact]
    public void A_tag_is_lower_cased_and_loses_its_hash()
    {
        Assert.Equal(["evp"], FeedTextParser.FindHashtags("clear #EVP last night"));
    }

    [Fact]
    public void Tags_differing_only_in_case_are_one_tag()
    {
        // The whole reason for normalising at write time: otherwise a tag page shows a third of
        // its posts, which is worse than having no tag page.
        Assert.Equal(["evp"], FeedTextParser.FindHashtags("#EVP #evp #Evp"));
    }

    [Fact]
    public void A_tag_cannot_start_with_a_digit()
    {
        // "#1" is a numbered list and "#2026" is a year. Neither is a topic.
        Assert.Empty(FeedTextParser.FindHashtags("finding #1 of the night, in #2026"));
    }

    [Fact]
    public void A_tag_may_contain_digits_after_the_first_character()
    {
        Assert.Equal(["evp2026"], FeedTextParser.FindHashtags("#evp2026"));
    }

    [Fact]
    public void A_bare_hash_is_not_a_tag()
    {
        Assert.Empty(FeedTextParser.FindHashtags("room # 3"));
    }

    [Fact]
    public void Several_tags_come_back_in_the_order_they_appear()
    {
        Assert.Equal(["bellwitch", "evp", "orbs"],
            FeedTextParser.FindHashtags("at the #bellwitch cave — #evp and #orbs"));
    }

    // ── Name normalisation ────────────────────────────────────────────────────

    [Theory]
    [InlineData("Sarah Mitchell", "sarahmitchell")]
    [InlineData("sarah mitchell", "sarahmitchell")]
    [InlineData("Sarah-Mitchell", "sarahmitchell")]
    [InlineData("  Sarah   Mitchell  ", "sarahmitchell")]
    [InlineData("O'Brien", "obrien")]
    [InlineData(null, "")]
    [InlineData("   ", "")]
    public void A_display_name_normalises_to_what_somebody_would_type(string? input, string expected)
    {
        Assert.Equal(expected, FeedTextParser.NormalizeName(input));
    }

    [Fact]
    public void Two_different_people_can_normalise_alike()
    {
        // Stated as a test because it is the case the caller must handle by refusing rather than
        // guessing: notifying the wrong Sarah Mitchell is worse than notifying neither.
        Assert.Equal(FeedTextParser.NormalizeName("Sarah Mitchell"),
                     FeedTextParser.NormalizeName("sarah-mitchell"));
    }

    // ── Both at once ──────────────────────────────────────────────────────────

    [Fact]
    public void A_post_can_carry_both_and_they_do_not_interfere()
    {
        const string post = "great work @sarahmitchell — the #evp at #bellwitch was the clearest yet";

        Assert.Equal(["sarahmitchell"], FeedTextParser.FindMentions(post));
        Assert.Equal(["evp", "bellwitch"], FeedTextParser.FindHashtags(post));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Nothing_in_nothing_out(string? text)
    {
        Assert.Empty(FeedTextParser.FindMentions(text));
        Assert.Empty(FeedTextParser.FindHashtags(text));
    }
}
