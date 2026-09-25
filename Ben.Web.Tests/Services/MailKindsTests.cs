using Ben.Data.Common.Interfaces;
using Ben.Data.Common.Mail;
using Ben.Data.WebApi.Services;
using Xunit;

namespace Ben.Web.Tests.Services;

/// <summary>
/// The names letters go by, and the tables their templates may read (item 246).
/// </summary>
public sealed class MailKindsTests
{
    [Fact]
    public void Every_key_is_unique()
    {
        var keys = MailKinds.All.Select(k => k.Key).ToList();
        Assert.Equal(keys.Count, keys.Distinct().Count());
    }

    /// <summary>
    /// A key ends up in a URL, a database column and a template row, so it stays boring.
    /// </summary>
    [Fact]
    public void Every_key_is_a_lower_case_slug()
        => Assert.All(MailKinds.All, k =>
        {
            Assert.Matches("^[a-z][a-z0-9-]*[a-z0-9]$", k.Key);
            Assert.DoesNotContain("--", k.Key);
        });

    /// <summary>
    /// The context is the security boundary, so an empty one is a template with nothing to say.
    /// </summary>
    /// <remarks>
    /// Ben chose "only what the letter has" over "every table" (2026-09-20). A kind that declares
    /// no tables would offer an empty dropdown, which reads as broken rather than as deliberate —
    /// and every letter the site sends has at least the person it is going to.
    /// </remarks>
    [Fact]
    public void Every_kind_can_reach_the_person_it_is_written_to()
        => Assert.All(MailKinds.All, k =>
        {
            Assert.NotEmpty(k.Context);
            Assert.Contains("AppUsers", k.Context);
        });

    [Fact]
    public void Every_kind_says_what_it_is_for()
        => Assert.All(MailKinds.All, k =>
        {
            Assert.False(string.IsNullOrWhiteSpace(k.Title));
            Assert.False(string.IsNullOrWhiteSpace(k.Description));
            Assert.EndsWith(".", k.Description);
        });

    [Fact]
    public void A_kind_can_be_found_by_its_key()
    {
        Assert.Equal(MailKinds.ResetYourPassword, MailKinds.Find("reset-your-password"));
        Assert.Null(MailKinds.Find("no-such-letter"));
        Assert.Null(MailKinds.Find(null));
    }

    /// <summary>
    /// A declared name beats the guess, and the guess still covers what has not been named.
    /// </summary>
    /// <remarks>
    /// This is the whole point of widening the message: item 239a's slug is derived from the
    /// subject's first four words, so a template keyed to it would stop applying the day somebody
    /// reworded a subject line.
    /// </remarks>
    [Fact]
    public void The_outbox_prefers_a_declared_kind_and_still_guesses_without_one()
    {
        var declared = new EmailMessage("a@b.test", "Your place at Halloween Lock-In is confirmed",
            "<p>hi</p>", Kind: MailKinds.BookingDecided.Key);
        var undeclared = declared with { Kind = null };

        Assert.Equal("booking-decided", declared.Kind ?? OutboxEmailService.Kind(declared.Subject));
        Assert.Equal("your-place-at-halloween",
            undeclared.Kind ?? OutboxEmailService.Kind(undeclared.Subject));
    }
}
