using Ben.Web.Website.Library.Kit;
using Xunit;

namespace Ben.Web.Tests.Website;

/// <summary>
/// The native colour input shows anything it cannot parse as black, silently. These cover the
/// forms a stored colour actually arrives in, so an unreadable value falls back visibly rather
/// than overwriting the saved colour with black.
/// </summary>
public class HexColorTests
{
    [Theory]
    [InlineData("#aabbcc", "#aabbcc")]
    [InlineData("aabbcc", "#aabbcc")]      // saved without the hash
    [InlineData("#AABBCC", "#aabbcc")]     // case-normalised so comparisons work
    [InlineData("  #aabbcc  ", "#aabbcc")] // trimmed
    [InlineData("#abc", "#aabbcc")]        // CSS shorthand the input will not take
    [InlineData("#aabbccdd", "#aabbcc")]   // alpha dropped rather than rejected
    public void Normalizes(string input, string expected)
        => Assert.Equal(expected, HexColor.Normalize(input));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("rebeccapurple")]          // named colours are legal CSS, not legal here
    [InlineData("rgb(1,2,3)")]
    [InlineData("#12345")]                 // wrong length
    [InlineData("#gggggg")]                // not hex
    public void UnusableValues_FallBack(string? input)
        => Assert.Equal("#336699", HexColor.Normalize(input, "#336699"));

    [Fact]
    public void UnusableFallback_StillYieldsAUsableColour()
    {
        // Otherwise a bad default would put us back where we started: a value the input renders
        // as black without saying why.
        Assert.Equal("#000000", HexColor.Normalize(null, "not-a-colour"));
    }

    [Fact]
    public void TryNormalize_ReportsFailureRatherThanGuessing()
    {
        Assert.Null(HexColor.TryNormalize("rebeccapurple"));
        Assert.Equal("#aabbcc", HexColor.TryNormalize("#AABBCC"));
    }

    [Theory]
    [InlineData("#abc", "#aabbcc")]
    [InlineData("AABBCC", "#aabbcc")]
    [InlineData("#aabbccff", "#aabbcc")]
    public void AreSame_IgnoresHowTheColourIsWritten(string left, string right)
        => Assert.True(HexColor.AreSame(left, right));

    [Fact]
    public void AreSame_IsFalseForDifferentColours()
        => Assert.False(HexColor.AreSame("#aabbcc", "#aabbcd"));
}
