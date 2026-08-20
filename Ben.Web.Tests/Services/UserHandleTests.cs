using Ben.Data.Common.Helpers;
using Xunit;

namespace Ben.Web.Tests.Services;

/// <summary>
/// The rules for an <c>@name</c>.
/// </summary>
/// <remarks>
/// These matter more than most validation, because Ben's decision is that a handle is chosen once
/// and never changed. A rule that is too loose cannot be tightened later without breaking names
/// people already have, and a name accepted by mistake is permanent.
/// </remarks>
public sealed class UserHandleTests
{
    // ── Normalising ───────────────────────────────────────────────────────────

    [Theory]
    [InlineData("SarahM", "sarahm")]
    [InlineData("@sarahm", "sarahm")]
    [InlineData("  @SarahM  ", "sarahm")]
    [InlineData("SARAHM", "sarahm")]
    [InlineData(null, "")]
    public void A_candidate_normalises_to_what_gets_stored(string? input, string expected)
    {
        // Storing the canonical form is what makes the unique index mean anything: without it,
        // "SarahM" and "sarahm" are two rows and @sarahm is ambiguous.
        Assert.Equal(expected, UserHandle.Normalize(input));
    }

    // ── Validity ──────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("sarahm")]
    [InlineData("sarah_mitchell")]
    [InlineData("evp2026")]
    [InlineData("abc")]
    public void Legal_names_are_accepted(string handle)
    {
        Assert.True(UserHandle.IsValid(handle, out var error), error);
    }

    [Theory]
    [InlineData("", "Choose a name.")]
    [InlineData("ab", "at least")]
    [InlineData("2spooky", "start with a letter")]
    [InlineData("_leading", "start with a letter")]
    [InlineData("has space", "letters, numbers and underscores")]
    [InlineData("has-hyphen", "letters, numbers and underscores")]
    [InlineData("has.dot", "letters, numbers and underscores")]
    public void Illegal_names_are_refused_with_a_reason(string handle, string expectedFragment)
    {
        Assert.False(UserHandle.IsValid(handle, out var error));
        Assert.Contains(expectedFragment, error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void A_name_longer_than_the_column_is_refused()
    {
        Assert.False(UserHandle.IsValid(new string('a', UserHandle.MaxLength + 1), out var error));
        Assert.Contains("at most", error, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Hyphens and dots are refused even though the mention parser tolerates them in a token.
    /// </summary>
    /// <remarks>
    /// Deliberate asymmetry. The parser is permissive so that <c>@sarah.mitchell</c> is recognised
    /// as an attempted mention rather than silently split; the handle rules are strict so that no
    /// real name ends in a character indistinguishable from punctuation. "@sarah." at the end of a
    /// sentence must not be a different person from "@sarah".
    /// </remarks>
    [Fact]
    public void Punctuation_that_a_sentence_could_supply_is_not_allowed_in_a_name()
    {
        Assert.False(UserHandle.IsValid("sarah.", out _));
        Assert.False(UserHandle.IsValid("sarah-", out _));
    }

    // ── Reserved ──────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("admin")]
    [InlineData("support")]
    [InlineData("ishaunted")]
    [InlineData("moderator")]
    [InlineData("feed")]
    [InlineData("settings")]
    public void Reserved_names_are_refused(string handle)
    {
        // Two kinds, both worth keeping: route words, which would make a profile URL read like a
        // section of the site; and names somebody would trust in a mention.
        Assert.False(UserHandle.IsValid(handle, out var error));
        Assert.Contains("reserved", error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Reserved_matching_is_case_insensitive_because_the_name_is_normalised_first()
    {
        Assert.False(UserHandle.IsValid("ADMIN", out _));
        Assert.False(UserHandle.IsValid("@Admin", out _));
    }

    // ── Suggesting ────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("Sarah Mitchell", null, "sarahmitchell")]
    [InlineData("Mary-Jane O'Brien", null, "maryjaneobrien")]
    [InlineData(null, "sarah.mitchell@benco.dev", "sarahmitchell")]
    [InlineData("", "j@example.com", "user")]     // one letter is below the minimum
    [InlineData(null, null, "user")]
    public void A_suggestion_is_derived_from_whatever_the_account_has(
        string? displayName, string? email, string expected)
    {
        Assert.Equal(expected, UserHandle.Suggest(displayName, email));
    }

    [Fact]
    public void Every_suggestion_is_itself_a_legal_name()
    {
        // The paths that use Suggest have no human to correct a bad answer — an Entra sign-in, an
        // event magic link, the seeders. A suggestion the column would reject means an account
        // created with no handle, which is an account that cannot be mentioned.
        foreach (var (name, email) in new (string?, string?)[]
        {
            ("Sarah Mitchell", null), ("!!!", null), ("2026", null), (null, "9@x.com"),
            (null, null), ("", ""), ("admin", null), ("A", null),
            (new string('x', 100), null),
        })
        {
            var suggestion = UserHandle.Suggest(name, email);
            Assert.True(UserHandle.IsValid(suggestion, out var error),
                $"Suggest({name ?? "null"}, {email ?? "null"}) produced \"{suggestion}\": {error}");
        }
    }

    [Fact]
    public void A_suggestion_never_lands_on_a_reserved_word()
    {
        // "admin" as a display name would otherwise suggest "admin", and the only thing standing
        // between that and somebody's profile would be the uniquifying suffix.
        var suggestion = UserHandle.Suggest("Admin", null);

        Assert.True(UserHandle.IsValid(suggestion, out var error), error);
        Assert.NotEqual("admin", suggestion);
    }
}
