using Ben.Video.Editor.Models;

namespace Ben.Video.Tests.Models;

public class TransitionStyleNameTests
{
    [Fact]
    public void Every_style_has_a_name_written_for_a_person()
    {
        foreach (var style in Enum.GetValues<TransitionStyle>())
        {
            var name = TransitionStyleName.For(style);

            Assert.False(string.IsNullOrWhiteSpace(name));
            // The failure this guards is a new enum member falling through to ToString(): identifier
            // spelling reaching a menu. Single words (Fade, Zoom, Radial) are their own label and
            // are exempt; anything camel-cased has to have been given a real name.
            var isIdentifierSpelling = name == style.ToString()
                                       && name.Skip(1).Any(char.IsUpper);
            Assert.False(isIdentifierSpelling, $"{style} has no display name — it would read as '{name}'.");
        }
    }

    [Fact]
    public void The_names_read_as_English()
    {
        Assert.Equal("Wipe left", TransitionStyleName.For(TransitionStyle.WipeLeft));
        Assert.Equal("Fade through black", TransitionStyleName.For(TransitionStyle.FadeBlack));
        Assert.Equal("Circle close", TransitionStyleName.For(TransitionStyle.CircleClose));
    }
}
