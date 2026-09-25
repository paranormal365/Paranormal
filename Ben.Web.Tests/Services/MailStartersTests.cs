using Ben.Data.Common.Mail;
using Xunit;

namespace Ben.Web.Tests.Services;

/// <summary>
/// Letters already written, and whether they would actually be accepted (item 246).
/// </summary>
public sealed class MailStartersTests
{
    /// <summary>
    /// The test the whole idea rests on.
    /// </summary>
    /// <remarks>
    /// A starter offered for a letter it does not fit is worse than no starter: an author loads
    /// it, edits it, presses Save, and is refused for tokens they never typed. So every starter is
    /// run through the same two checks the save endpoint applies, against every kind it claims.
    /// </remarks>
    [Fact]
    public void Every_starter_would_be_accepted_by_every_letter_it_claims()
    {
        // Driven from what is actually OFFERED for each letter, not from what each starter claims
        // — the offering is what an author sees, and it is where a starter that cannot work has to
        // be excluded.
        foreach (var kind in MailKinds.All)
        {
            foreach (var starter in MailStarters.For(kind))
            {
                var unresolvable = MailTokens.Unresolvable(starter.Subject, kind)
                    .Concat(MailTokens.Unresolvable(starter.BodyHtml, kind))
                    .Distinct()
                    .ToList();

                Assert.True(unresolvable.Count == 0,
                    $"Starter \"{starter.Key}\" offered for \"{kind.Key}\" uses "
                  + $"{string.Join(", ", unresolvable)}, which that letter cannot fill in.");

                var missing = MailTokens.MissingRequired(starter.Subject, starter.BodyHtml, kind);
                Assert.True(missing.Count == 0,
                    $"Starter \"{starter.Key}\" offered for \"{kind.Key}\" leaves out "
                  + $"{string.Join(", ", missing)}, without which the letter does nothing.");
            }
        }
    }

    /// <summary>
    /// A starter that claims no particular letter must be safe for all of them.
    /// </summary>
    /// <remarks>
    /// It is covered by the test above, but stated separately because it is the rule that makes
    /// "Suits: []" mean something rather than being an omission.
    /// </remarks>
    [Fact]
    public void A_starter_that_fits_anything_names_nothing_but_the_person()
    {
        var anywhere = MailStarters.All.Where(s => s.Suits.Count == 0).ToList();
        Assert.NotEmpty(anywhere);

        // Its TOKENS must resolve everywhere. Whether it is offered everywhere is a separate
        // question, answered by For(): a letter that needs a confirmation link will not be given a
        // starter that has none.
        foreach (var starter in anywhere)
            foreach (var kind in MailKinds.All)
                Assert.Empty(MailTokens.Unresolvable(starter.BodyHtml, kind));
    }

    [Fact]
    public void Every_starter_is_a_whole_letter()
        => Assert.All(MailStarters.All, s =>
        {
            Assert.False(string.IsNullOrWhiteSpace(s.Subject));
            Assert.False(string.IsNullOrWhiteSpace(s.BodyHtml));
            Assert.EndsWith(".", s.What);

            // Built from blocks, so it inherits their email-safety rather than being hand-written
            // markup that happens to look right in a browser.
            Assert.Contains("<table", s.BodyHtml, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("<style", s.BodyHtml, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("class=", s.BodyHtml, StringComparison.OrdinalIgnoreCase);
        });

    [Fact]
    public void The_letters_Ben_named_all_have_one()
    {
        Assert.NotNull(MailStarters.Find("receipt"));
        Assert.NotNull(MailStarters.Find("invoice"));
        Assert.NotNull(MailStarters.Find("invitation"));
        Assert.NotNull(MailStarters.Find("confirmation"));
    }

    [Fact]
    public void Every_letter_has_something_to_start_from()
        => Assert.All(MailKinds.All, k => Assert.NotEmpty(MailStarters.For(k)));

    [Fact]
    public void Keys_are_unique()
    {
        var keys = MailStarters.All.Select(s => s.Key).ToList();
        Assert.Equal(keys.Count, keys.Distinct().Count());
    }
}
