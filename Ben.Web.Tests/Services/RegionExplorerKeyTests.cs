using Ben.Web.Website.Library.Manage.Audio;
using Xunit;

namespace Ben.Web.Tests.Services;

/// <summary>
/// When the region explorer must fetch audio again.
/// </summary>
/// <remarks>
/// It asked whether it had loaded anything at all — <c>if (!Visible || _source is not null)
/// return;</c> — so the first region somebody explored was the audio they heard for every region
/// after it, while the title, the notes panel and the Save button moved on to the new one.
/// Exploring a second region and saving it produced a file that was not the sound that had been
/// playing (2026-09-06 audio walk, finding H).
/// </remarks>
public sealed class RegionExplorerKeyTests
{
    private static readonly Guid AFile      = Guid.NewGuid();
    private static readonly Guid AnotherFile = Guid.NewGuid();

    [Fact]
    public void Nothing_loaded_yet_means_load_it()
        => Assert.True(RegionExplorerKey.ShouldReload(null, new RegionExplorerKey(AFile, 10, 20)));

    [Fact]
    public void Nothing_to_show_means_do_not_load()
        => Assert.False(RegionExplorerKey.ShouldReload(new RegionExplorerKey(AFile, 10, 20), null));

    [Fact]
    public void The_same_region_is_not_fetched_again()
        => Assert.False(RegionExplorerKey.ShouldReload(
            new RegionExplorerKey(AFile, 10, 20), new RegionExplorerKey(AFile, 10, 20)));

    /// <summary>The failure this exists for: a second region must not play the first one's audio.</summary>
    [Fact]
    public void A_different_region_of_the_same_recording_is_fetched()
        => Assert.True(RegionExplorerKey.ShouldReload(
            new RegionExplorerKey(AFile, 10, 20), new RegionExplorerKey(AFile, 74.6, 93.2)));

    [Fact]
    public void A_region_that_starts_in_the_same_place_but_ends_elsewhere_is_fetched()
        => Assert.True(RegionExplorerKey.ShouldReload(
            new RegionExplorerKey(AFile, 10, 20), new RegionExplorerKey(AFile, 10, 45)));

    [Fact]
    public void The_same_range_of_a_different_recording_is_fetched()
        => Assert.True(RegionExplorerKey.ShouldReload(
            new RegionExplorerKey(AFile, 10, 20), new RegionExplorerKey(AnotherFile, 10, 20)));

    /// <summary>
    /// Bounds are floating-point seconds that round-trip through the browser, so exact equality
    /// would re-download the same audio on every render.
    /// </summary>
    [Fact]
    public void A_boundary_that_drifted_by_a_microsecond_is_the_same_load()
        => Assert.False(RegionExplorerKey.ShouldReload(
            new RegionExplorerKey(AFile, 10, 20), new RegionExplorerKey(AFile, 10.000001, 19.999998)));

    [Fact]
    public void A_boundary_a_tenth_of_a_second_out_is_a_different_load()
        => Assert.True(RegionExplorerKey.ShouldReload(
            new RegionExplorerKey(AFile, 10, 20), new RegionExplorerKey(AFile, 10.1, 20)));

    // ── Building one ──────────────────────────────────────────────────────────

    [Fact]
    public void A_region_with_real_bounds_makes_a_key()
    {
        var key = RegionExplorerKey.For(AFile, 74.6, 93.2);

        Assert.NotNull(key);
        Assert.Equal(74.6, key!.Value.Start);
        Assert.Equal(18.6, key.Value.DurationSeconds, 3);
    }

    [Theory]
    [InlineData(null, 20.0)]
    [InlineData(10.0, null)]
    [InlineData(20.0, 20.0)]   // a region of no length is nothing to explore
    [InlineData(20.0, 10.0)]   // inverted
    public void Anything_that_is_not_a_stretch_makes_no_key(double? start, double? end)
        => Assert.Null(RegionExplorerKey.For(AFile, start, end));

    [Fact]
    public void A_missing_file_makes_no_key()
        => Assert.Null(RegionExplorerKey.For(Guid.Empty, 10, 20));
}
