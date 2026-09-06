using Ben.Service.Models.Entities;
using Ben.Web.Website.Library.Manage.Audio;
using Xunit;

namespace Ben.Web.Tests.Services;

/// <summary>
/// Which notes belong to a stretch of a recording.
/// </summary>
/// <remarks>
/// Notes are stored against a time range, not against a clip, so this question gets asked in two
/// places and has to give the same answer in both. The rule lived inline in the region explorer and
/// nowhere else, so a note written while listening to a region was visible only in the panel it was
/// typed in — save the region as a clip and nothing said why it had been worth saving
/// (2026-09-06 audio walk, finding M).
/// </remarks>
public sealed class RegionNoteMatchingTests
{
    private static UploadFileRegionNoteRecord Note(
        double start, double end, double? at = null, string html = "<p>heard a name</p>",
        int createdMinutesAgo = 0)
        => new()
        {
            Id           = Guid.NewGuid(),
            UploadFileId = Guid.NewGuid(),
            RegionStart  = start,
            RegionEnd    = end,
            TimeOffset   = at,
            NoteHtml     = html,
            DateCreated  = DateTime.UtcNow.AddMinutes(-createdMinutesAgo),
        };

    [Fact]
    public void A_note_about_this_very_region_belongs_to_it()
        => Assert.True(RegionNoteMatching.IsAbout(Note(74.6, 93.2), 74.6, 93.2));

    /// <summary>
    /// Bounds make a round trip through the browser as floating-point seconds and come back a hair
    /// different, so exact equality finds nothing at all.
    /// </summary>
    [Fact]
    public void A_boundary_that_drifted_by_a_few_milliseconds_still_matches()
        => Assert.True(RegionNoteMatching.IsAbout(Note(74.6001, 93.1998), 74.6, 93.2));

    [Fact]
    public void A_note_about_a_different_stretch_does_not()
        => Assert.False(RegionNoteMatching.IsAbout(Note(10, 20), 74.6, 93.2));

    /// <summary>
    /// Half a second is not drift, it is a different boundary — and it is well inside what somebody
    /// can place by dragging.
    /// </summary>
    [Fact]
    public void A_boundary_half_a_second_out_is_a_different_region()
        => Assert.False(RegionNoteMatching.IsAbout(Note(74.6, 93.7), 74.6, 93.2));

    /// <summary>A note pinned at a moment belongs to every region that contains that moment.</summary>
    [Fact]
    public void A_point_note_inside_the_range_belongs_to_it()
        => Assert.True(RegionNoteMatching.IsAbout(Note(0, 0, at: 82.0), 74.6, 93.2));

    [Fact]
    public void A_point_note_outside_the_range_does_not()
        => Assert.False(RegionNoteMatching.IsAbout(Note(0, 0, at: 300.0), 74.6, 93.2));

    [Fact]
    public void Notes_come_back_oldest_first()
    {
        var notes = new[]
        {
            Note(1, 2, html: "<p>second</p>", createdMinutesAgo: 5),
            Note(1, 2, html: "<p>first</p>",  createdMinutesAgo: 50),
            Note(9, 9, html: "<p>elsewhere</p>"),
        };

        var forRange = RegionNoteMatching.For(notes, 1, 2);

        Assert.Equal(2, forRange.Count);
        Assert.Equal("<p>first</p>",  forRange[0].NoteHtml);
        Assert.Equal("<p>second</p>", forRange[1].NoteHtml);
    }

    [Fact]
    public void A_range_nobody_wrote_about_has_no_notes()
        => Assert.Empty(RegionNoteMatching.For([Note(1, 2)], 400, 500));
}
