using Ben.Web.Website.Library.Manage.Audio;
using Xunit;

namespace Ben.Web.Tests.Services;

/// <summary>
/// Which region a destructive edit acts on.
/// </summary>
/// <remarks>
/// <para>This decision used to live in <c>AudioFilePreview</c> as a pair of loose fields and a set
/// of ids the component had added itself. That worked for the overlays it drew and not at all for
/// the ones JavaScript drew: silence detection adds its regions inside <c>detectSilence</c>, so
/// their ids never reached C#. Each arrived looking user-drawn, each cleared the others to enforce
/// "one region at a time", and the last one became the edit target — so with a region drawn at
/// 1:14–1:33, turning silence detection on moved Cut's target to 3:00.6–3:06.5 and deleted the
/// selection (2026-09-06 audio walk, finding B).</para>
///
/// <para>Extracted here so the rule can be stated once and checked without a browser.</para>
/// </remarks>
public sealed class RegionSelectionTests
{
    private static WsRegionData Region(string id, double start, double end, string? kind = null)
        => new() { Id = id, Start = start, End = end, KindName = kind };

    [Fact]
    public void Nothing_is_selected_to_begin_with()
    {
        var selection = new RegionSelection();

        Assert.False(selection.HasTarget);
        Assert.Null(selection.Target);
    }

    [Fact]
    public void A_region_a_person_drew_becomes_the_target()
    {
        var selection = new RegionSelection();

        Assert.True(selection.Created(Region("wavesurfer_1", 74.6, 93.2, "user")));

        Assert.True(selection.HasTarget);
        Assert.Equal(74.6, selection.Target!.Start);
    }

    /// <summary>
    /// The walk's exact sequence: draw a region, then turn on silence detection.
    /// </summary>
    /// <remarks>
    /// Twenty silence regions arrive one after another. Not one of them may take the selection, and
    /// the drawn region has to survive all of them — otherwise Cut destroys a stretch the machine
    /// picked rather than the one the person picked.
    /// </remarks>
    [Fact]
    public void Silence_detection_does_not_take_the_selection()
    {
        var selection = new RegionSelection();
        selection.Created(Region("wavesurfer_1", 74.6, 93.2, "user"));

        for (var i = 0; i < 20; i++)
            Assert.False(selection.Created(Region($"silence-{i}", 180.6 + i, 186.5 + i, "silence")));

        Assert.Equal("wavesurfer_1", selection.Target!.Id);
        Assert.Equal(74.6, selection.Target.Start);
    }

    [Theory]
    [InlineData("silence")]
    [InlineData("marker")]
    [InlineData("clip")]
    [InlineData("overlay")]
    public void Nothing_the_editor_drew_can_be_an_edit_target(string kind)
    {
        var selection = new RegionSelection();

        Assert.False(selection.Created(Region("r", 1, 2, kind)));
        Assert.False(selection.HasTarget);
    }

    /// <summary>
    /// The default matters more than any of the named kinds.
    /// </summary>
    /// <remarks>
    /// A region the player was not told about is one a person dragged — the plugin creates those
    /// itself and nobody registers them. Guessing the other way silently drops the selection
    /// somebody just made, which is the failure that is impossible to see and easy to ship.
    /// </remarks>
    [Fact]
    public void A_region_with_no_stated_kind_is_treated_as_drawn()
    {
        var selection = new RegionSelection();

        Assert.True(selection.Created(Region("wavesurfer_2", 5, 9, kind: null)));
        Assert.Equal("wavesurfer_2", selection.Target!.Id);
    }

    [Fact]
    public void An_unrecognised_kind_is_also_treated_as_drawn()
    {
        var selection = new RegionSelection();

        Assert.True(selection.Created(Region("wavesurfer_3", 5, 9, "something-a-newer-player-sends")));
    }

    [Fact]
    public void Drawing_a_second_region_replaces_the_first()
    {
        var selection = new RegionSelection();
        selection.Created(Region("first",  1, 2, "user"));
        selection.Created(Region("second", 8, 9, "user"));

        Assert.Equal("second", selection.Target!.Id);
    }

    /// <summary>
    /// Dragging or resizing the region has to move the target with it.
    /// </summary>
    /// <remarks>
    /// Otherwise the target keeps the bounds from the moment it was drawn, so the panel's readout
    /// and the waveform under it disagree, and the edit uses the numbers nobody can see.
    /// </remarks>
    [Fact]
    public void Resizing_the_selection_moves_the_target_with_it()
    {
        var selection = new RegionSelection();
        selection.Created(Region("r", 1, 2, "user"));

        selection.Updated(Region("r", 1.5, 6.25, "user"));

        Assert.Equal(1.5,  selection.Target!.Start);
        Assert.Equal(6.25, selection.Target.End);
    }

    [Fact]
    public void Resizing_something_else_leaves_the_target_alone()
    {
        var selection = new RegionSelection();
        selection.Created(Region("r", 1, 2, "user"));

        selection.Updated(Region("marker-9", 40, 41, "marker"));

        Assert.Equal(1, selection.Target!.Start);
    }

    /// <summary>
    /// A target that outlives its region leaves Cut enabled and pointed at a range nobody can see.
    /// </summary>
    [Fact]
    public void Deleting_the_selection_leaves_nothing_selected()
    {
        var selection = new RegionSelection();
        selection.Created(Region("r", 1, 2, "user"));

        selection.Removed("r");

        Assert.False(selection.HasTarget);
    }

    [Fact]
    public void Deleting_some_other_region_leaves_the_selection_alone()
    {
        var selection = new RegionSelection();
        selection.Created(Region("r", 1, 2, "user"));

        selection.Removed("silence-4");

        Assert.True(selection.HasTarget);
    }

    [Fact]
    public void Clearing_leaves_nothing_selected()
    {
        var selection = new RegionSelection();
        selection.Created(Region("r", 1, 2, "user"));

        selection.Clear();

        Assert.False(selection.HasTarget);
    }
}
