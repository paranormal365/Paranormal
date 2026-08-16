using Ben.Video.Editor.Effects;

namespace Ben.Video.Tests.Services;

public sealed class GoogleFontsTests
{
    [Theory]
    [InlineData("Roboto")]
    [InlineData("Open Sans")]
    [InlineData("Playfair Display")]
    [InlineData("open sans")] // case-insensitive — a saved project shouldn't break over casing
    public void IsGoogleFont_KnownFamily_ReturnsTrue(string family)
    {
        Assert.True(GoogleFonts.IsGoogleFont(family));
    }

    [Theory]
    [InlineData("Arial")]
    [InlineData("Times New Roman")]
    [InlineData("Not A Real Font")]
    [InlineData("")]
    public void IsGoogleFont_SystemOrUnknownFamily_ReturnsFalse(string family)
    {
        Assert.False(GoogleFonts.IsGoogleFont(family));
    }

    [Fact]
    public void IsGoogleFont_Null_ReturnsFalse()
    {
        Assert.False(GoogleFonts.IsGoogleFont(null));
    }

    [Fact]
    public void Names_DoesNotOverlapStandardFonts()
    {
        var overlap = GoogleFonts.Names.Intersect(StandardFonts.Names, StringComparer.OrdinalIgnoreCase);
        Assert.Empty(overlap);
    }
}
