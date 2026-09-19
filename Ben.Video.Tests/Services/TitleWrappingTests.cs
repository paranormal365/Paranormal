using Ben.Video.Editor.Models;

namespace Ben.Video.Tests.Services;

/// <summary>
/// A long title breaks into lines instead of running off the frame.
/// </summary>
/// <remarks>
/// Titles never wrapped. A sentence of any length drew as one line and ran off both sides, and the
/// only remedy was typing the breaks yourself. Callouts had wrapped for a while and the wrapping
/// code is shared — only titles had no way to ask for it (2026-09-05 audit, titles-6).
/// </remarks>
public sealed class TitleWrappingTests
{
    private static TextOverlay Long() => new()
    {
        Name     = "title",
        Text     = "The basement door opened by itself at fourteen minutes past two in the morning",
        FontSize = 48,
    };

    private static int TspanCount(string svg) =>
        System.Text.RegularExpressions.Regex.Matches(svg, "<tspan").Count;

    [Fact]
    public void Without_a_width_a_long_title_is_still_one_line()
    {
        var svg = TextOverlayRenderer.Render(Long(), 1920, 1080);

        Assert.Equal(1, TspanCount(svg));
    }

    [Fact]
    public void With_a_width_it_breaks_into_several()
    {
        var overlay = Long();
        overlay.MaxWidth = 0.5;

        var svg = TextOverlayRenderer.Render(overlay, 1920, 1080);

        Assert.True(TspanCount(svg) > 1);
    }

    /// <summary>
    /// A narrower limit gives more lines. The point is that the number follows the width rather
    /// than the wrap happening at some fixed place.
    /// </summary>
    [Fact]
    public void A_narrower_width_gives_more_lines()
    {
        var wide   = Long(); wide.MaxWidth = 0.8;
        var narrow = Long(); narrow.MaxWidth = 0.3;

        Assert.True(TspanCount(TextOverlayRenderer.Render(narrow, 1920, 1080))
                  > TspanCount(TextOverlayRenderer.Render(wide, 1920, 1080)));
    }

    /// <summary>Wrapping never loses a word.</summary>
    [Fact]
    public void Every_word_survives_the_wrap()
    {
        var overlay = Long();
        overlay.MaxWidth = 0.4;

        var svg = TextOverlayRenderer.Render(overlay, 1920, 1080);

        foreach (var word in overlay.Text.Split(' '))
            Assert.Contains(word, svg);
    }

    [Fact]
    public void Rich_text_wraps_too()
    {
        var overlay = Long();
        overlay.MaxWidth = 0.4;
        overlay.Runs = [new TextRun { Text = overlay.Text }];

        var svg = TextOverlayRenderer.Render(overlay, 1920, 1080);

        Assert.True(TspanCount(svg) > 1);
    }
}
