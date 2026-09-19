using Ben.Video.Editor.Services;

namespace Ben.Video.Tests.Services;

/// <summary>
/// Where a clip's stored media actually is.
/// </summary>
/// <remarks>
/// The live player looked only under the clip's own id and played black for a clip that was
/// plainly on the timeline, because a clip placed from the media bin shares the bin entry's copy
/// rather than making a second one. Found by opening the page (phase 12).
/// </remarks>
public sealed class MediaStorageTests
{
    private static readonly Guid Clip = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid Bin  = Guid.Parse("22222222-2222-2222-2222-222222222222");

    [Fact]
    public void A_clip_imported_straight_onto_the_timeline_has_one_place_to_look() =>
        Assert.Equal([Clip], MediaStorage.CandidateIds(Clip, null));

    [Fact]
    public void A_clip_placed_from_the_bin_has_two()
    {
        var ids = MediaStorage.CandidateIds(Clip, Bin);

        Assert.Equal(2, ids.Count);
        Assert.Contains(Bin, ids);
    }

    /// <summary>
    /// The clip's own copy first: a clip whose media was replaced has one, and it is the newer
    /// answer.
    /// </summary>
    [Fact]
    public void The_clips_own_copy_is_tried_first() =>
        Assert.Equal(Clip, MediaStorage.CandidateIds(Clip, Bin)[0]);

    [Fact]
    public void A_bin_id_that_is_the_clips_own_id_is_not_a_second_place_to_look() =>
        Assert.Single(MediaStorage.CandidateIds(Clip, Clip));

    /// <summary>An empty id is not an id; looking under it would read every project's nothing.</summary>
    [Fact]
    public void An_empty_bin_id_is_not_a_place_to_look() =>
        Assert.Equal([Clip], MediaStorage.CandidateIds(Clip, Guid.Empty));
}
