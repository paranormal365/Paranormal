using Ben.Video.Editor.Models;
using Xunit;

namespace Ben.Video.Tests.Models;

/// <summary>
/// What a media-bin card says about one source (V-4, site evaluation 2026-09-06).
/// </summary>
/// <remarks>
/// The Video bin marked two clips "on timeline" while Live playback, reading the same project,
/// said "2 clips are missing their media, so they play as black here". The bin's label was built
/// from the duration and the placement count and never asked whether the bytes were there.
///
/// That is the worse half of the disagreement: the bin is where somebody looks to see what the
/// project HAS, so "on timeline" reads as a promise that the thing will render.
/// </remarks>
public class BinCardStateTests
{
    [Fact]
    public void A_clip_that_is_not_placed_shows_only_its_length()
        => Assert.Equal("1:30", BinCardState.Meta("1:30", timesOnTimeline: 0, mediaMissing: false));

    [Fact]
    public void A_placed_clip_says_so()
        => Assert.Equal("1:30 · on timeline", BinCardState.Meta("1:30", 1, false));

    [Fact]
    public void A_clip_placed_more_than_once_carries_the_count()
        => Assert.Equal("1:30 · on timeline ×3", BinCardState.Meta("1:30", 3, false));

    /// <summary>The case the bin used to be silent about.</summary>
    [Fact]
    public void A_clip_whose_media_is_gone_says_that_instead()
        => Assert.Equal("1:30 · media missing", BinCardState.Meta("1:30", 0, mediaMissing: true));

    /// <summary>
    /// Missing replaces the placement count rather than joining it.
    /// </summary>
    /// <remarks>
    /// "on timeline · media missing" reads as two facts of equal weight. The truth is that the
    /// placement does not matter while there is nothing to play, and the reader's next action is
    /// to find the file — not to look at the timeline.
    /// </remarks>
    [Fact]
    public void Missing_media_replaces_the_placement_count()
    {
        var meta = BinCardState.Meta("1:30", timesOnTimeline: 2, mediaMissing: true);
        Assert.Equal("1:30 · media missing", meta);
        Assert.DoesNotContain("on timeline", meta);
    }

    /// <summary>The length survives in every case — it is what the card was opened for.</summary>
    [Theory]
    [InlineData(0, false)]
    [InlineData(1, false)]
    [InlineData(4, false)]
    [InlineData(0, true)]
    [InlineData(4, true)]
    public void The_duration_is_always_there(int placements, bool missing)
        => Assert.StartsWith("2:05", BinCardState.Meta("2:05", placements, missing));

    [Fact]
    public void Only_a_missing_clip_is_a_warning()
    {
        Assert.True(BinCardState.IsWarning(mediaMissing: true));
        Assert.False(BinCardState.IsWarning(mediaMissing: false));
    }
}
