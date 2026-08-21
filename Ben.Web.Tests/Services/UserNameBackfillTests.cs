using Ben.Data.WebApi.Services;
using Xunit;

namespace Ben.Web.Tests.Services;

/// <summary>
/// Deriving a first and last name from a display name, including where it cannot.
/// </summary>
/// <remarks>
/// Accounts predating the legal-name fields have only a display name to go on. The split is a
/// guess, and these tests pin which guesses it makes — particularly the ones it deliberately
/// declines to make, since an invented surname is worse than a missing one.
/// </remarks>
public sealed class UserNameBackfillTests
{
    [Theory]
    [InlineData("Sarah Mitchell", "Sarah", "Mitchell")]
    [InlineData("James Thornton", "James", "Thornton")]
    [InlineData("  Emma Rodriguez  ", "Emma", "Rodriguez")]
    public void An_ordinary_two_part_name_splits_as_expected(string input, string first, string last)
    {
        var (f, l) = UserNameBackfillService.SplitDisplayName(input);

        Assert.Equal(first, f);
        Assert.Equal(last, l);
    }

    /// <summary>
    /// A single word becomes a first name with no surname invented.
    /// </summary>
    /// <remarks>
    /// "AverageBen" is a handle-ish display name, not a person's full name. Splitting it into
    /// something plausible would put a fictional surname on a real account.
    /// </remarks>
    [Theory]
    [InlineData("AverageBen")]
    [InlineData("Probe")]
    public void A_single_word_yields_a_first_name_only(string input)
    {
        var (first, last) = UserNameBackfillService.SplitDisplayName(input);

        Assert.Equal(input, first);
        Assert.Null(last);
    }

    /// <summary>
    /// Three or more parts split on the LAST space.
    /// </summary>
    /// <remarks>
    /// "Mary Anne Fletcher" is far likelier to be Mary Anne / Fletcher than Mary / Anne Fletcher.
    /// It gets a compound surname like "Ana de Armas" wrong the other way — unavoidable without
    /// asking the person, which is what the profile does.
    /// </remarks>
    [Fact]
    public void Multi_part_names_split_on_the_last_space()
    {
        var (first, last) = UserNameBackfillService.SplitDisplayName("Mary Anne Fletcher");

        Assert.Equal("Mary Anne", first);
        Assert.Equal("Fletcher", last);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Nothing_usable_yields_nothing(string input)
    {
        var (first, last) = UserNameBackfillService.SplitDisplayName(input);

        Assert.Null(first);
        Assert.Null(last);
    }

    /// <summary>A trailing space must not produce an empty surname.</summary>
    [Fact]
    public void A_trailing_space_does_not_become_an_empty_last_name()
    {
        var (first, last) = UserNameBackfillService.SplitDisplayName("Cher ");

        Assert.Equal("Cher", first);
        Assert.Null(last);
    }
}
