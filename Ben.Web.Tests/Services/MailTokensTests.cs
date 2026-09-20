using Ben.Data.Common.Mail;
using Xunit;

namespace Ben.Web.Tests.Services;

/// <summary>
/// What a template's tokens become (item 246).
/// </summary>
public sealed class MailTokensTests
{
    // 9:05 AM in Nashville on 20 September 2026 — 14:05 UTC, because central time is UTC-5 in
    // September. Ben's own example, kept as the fixture so the direction cannot drift.
    private static readonly DateTime NashvilleNineOhFive = new(2026, 9, 20, 14, 5, 0, DateTimeKind.Utc);

    private static TimeZoneInfo Zone(string id) => TimeZoneInfo.FindSystemTimeZoneById(id);

    private static MailTokens.Context For(
        string zone = "America/Chicago",
        Dictionary<string, IReadOnlyDictionary<string, object?>>? tables = null)
        => new(tables ?? [], Zone(zone), NashvilleNineOhFive, "IsHaunted.com", "https://ishaunted.com");

    [Fact]
    public void The_common_tokens_read_as_a_person_would_write_them()
    {
        var c = For();

        Assert.Equal("09/20/2026", MailTokens.Render("{Date}", c));
        Assert.Equal("9:05 AM", MailTokens.Render("{Time}", c));
        Assert.Equal("September 20, 2026", MailTokens.Render("{FullDate}", c));
        Assert.Equal("September 20, 2026 9:05 AM", MailTokens.Render("{FullDateTime}", c));
        Assert.Equal("2026", MailTokens.Render("{Year}", c));
    }

    /// <summary>
    /// The reader's clock, not the server's.
    /// </summary>
    /// <remarks>
    /// New York is an hour AHEAD of Tennessee, so 9:05 in Nashville is 10:05 there — the opposite
    /// direction to the one in the original ask, and getting it backwards would make people an
    /// hour late rather than an hour early.
    /// </remarks>
    [Theory]
    [InlineData("America/Chicago",     "9:05 AM")]
    [InlineData("America/New_York",   "10:05 AM")]
    [InlineData("America/Los_Angeles", "7:05 AM")]
    [InlineData("Europe/London",       "3:05 PM")]
    public void A_time_is_shown_where_the_reader_is(string zone, string expected)
        => Assert.Equal(expected, MailTokens.Render("{Time}", For(zone)));

    [Fact]
    public void A_column_is_read_from_the_row_the_letter_carries()
    {
        var c = For(tables: new() { ["AppUsers"] = new Dictionary<string, object?>
            { ["DisplayName"] = "Marguerite", ["Id"] = 7 } });

        Assert.Equal("Hello Marguerite, you are number 7.",
            MailTokens.Render("Hello {AppUsers.DisplayName}, you are number {AppUsers.Id}.", c));
    }

    [Fact]
    public void Table_and_column_names_do_not_have_to_be_typed_exactly()
    {
        var c = For(tables: new() { ["AppUsers"] = new Dictionary<string, object?>
            { ["DisplayName"] = "Marguerite" } });

        Assert.Equal("Marguerite", MailTokens.Render("{appusers.displayname}", c));
    }

    /// <summary>
    /// The one that matters: an email is read in software nobody here controls.
    /// </summary>
    [Fact]
    public void A_value_is_escaped_rather_than_trusted()
    {
        var c = For(tables: new() { ["AppUsers"] = new Dictionary<string, object?>
            { ["DisplayName"] = "<script>alert('x')</script>" } });

        var html = MailTokens.Render("<p>Hello {AppUsers.DisplayName}</p>", c);

        Assert.DoesNotContain("<script>", html);
        Assert.Contains("&lt;script&gt;", html);
    }

    [Fact]
    public void A_date_column_is_shown_in_the_readers_zone_not_as_a_machine_wrote_it()
    {
        var c = For("America/New_York", new() { ["HostedEvents"] = new Dictionary<string, object?>
            { ["StartsUtc"] = new DateTime(2026, 10, 31, 23, 30, 0, DateTimeKind.Utc) } });

        Assert.Equal("October 31, 2026 7:30 PM", MailTokens.Render("{HostedEvents.StartsUtc}", c));
    }

    [Fact]
    public void Something_nothing_can_fill_in_becomes_nothing()
    {
        var c = For(tables: new() { ["AppUsers"] = new Dictionary<string, object?>() });

        Assert.Equal("Hello .", MailTokens.Render("Hello {AppUsers.NoSuchColumn}.", c));
        Assert.Equal("Hello .", MailTokens.Render("Hello {NoSuchTable.Whatever}.", c));
    }

    [Fact]
    public void Text_that_only_looks_like_a_token_is_left_alone()
    {
        var c = For();
        Assert.Equal("Costs {99} and {a b}", MailTokens.Render("Costs {99} and {a b}", c));
    }

    // ── what the editor refuses to save ──────────────────────────────────────

    [Fact]
    public void A_token_outside_this_letters_tables_is_named_as_unresolvable()
    {
        var bad = MailTokens.Unresolvable(
            "{AppUsers.DisplayName} {Cases.Title} {FullDate}", MailKinds.ResetYourPassword);

        // A reset letter carries the person and nothing else.
        Assert.Equal(["Cases.Title"], bad);
    }

    [Fact]
    public void A_bare_word_that_is_not_a_common_token_is_unresolvable()
        => Assert.Equal(["Nonsense"], MailTokens.Unresolvable("{Nonsense}", MailKinds.ResetYourPassword));

    [Fact]
    public void A_template_that_only_uses_what_it_has_is_accepted()
        => Assert.Empty(MailTokens.Unresolvable(
            "Hello {AppUsers.DisplayName}, it is {FullDate}. — {SiteName}",
            MailKinds.ResetYourPassword));
}
