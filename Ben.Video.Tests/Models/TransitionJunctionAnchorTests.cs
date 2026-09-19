using Ben.Video.Editor.Models;

namespace Ben.Video.Tests.Models;

public class TransitionJunctionAnchorTests
{
    [Fact]
    public void Abutting_clips_anchor_on_the_cut_itself()
    {
        Assert.Equal(3.0, TransitionJunctionAnchor.Seconds(firstClipEndSeconds: 3.0, secondClipStartSeconds: 3.0));
    }

    [Fact]
    public void A_gap_puts_the_button_in_the_middle_of_the_gap()
    {
        Assert.Equal(3.5, TransitionJunctionAnchor.Seconds(firstClipEndSeconds: 3.0, secondClipStartSeconds: 4.0));
    }

    [Fact]
    public void Overlapping_clips_keep_the_button_inside_the_overlap()
    {
        // The second clip starts before the first one ends — the timeline draws that truthfully
        // (2026-09-05 audit, F5), so the button has to land inside the overlap, not outside it.
        var seconds = TransitionJunctionAnchor.Seconds(firstClipEndSeconds: 4.0, secondClipStartSeconds: 3.0);

        Assert.InRange(seconds, 3.0, 4.0);
    }

    [Fact]
    public void It_never_answers_with_a_negative_mark()
    {
        Assert.Equal(0, TransitionJunctionAnchor.Seconds(firstClipEndSeconds: -2.0, secondClipStartSeconds: 0));
    }
}
